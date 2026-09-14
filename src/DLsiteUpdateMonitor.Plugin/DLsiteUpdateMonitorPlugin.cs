using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DLsiteUpdateMonitor.Core.Http;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Parsing;
using DLsiteUpdateMonitor.Core.Persistence;
using DLsiteUpdateMonitor.Core.Services;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace DLsiteUpdateMonitor
{
    public sealed class DLsiteUpdateMonitorPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly SemaphoreSlim operationLock = new SemaphoreSlim(1, 1);
        private readonly DlsiteLinkResolver linkResolver = new DlsiteLinkResolver();
        private readonly TrackingRepository repository;
        private TrackingDatabase tracking;
        private string startupWarning;
        private bool persistenceBlocked;

        private DlsiteHttpClient httpClient;
        private SnapshotCache cache;
        private TrackingStateMachine stateMachine;
        private UpdateCheckService checkService;
        private PlayniteTagService tagService;

        public override Guid Id { get; } = Guid.Parse("334542c6-1f81-4cc5-afd5-e052b021d37e");
        public PluginSettings Settings { get; private set; }

        public DLsiteUpdateMonitorPlugin(IPlayniteAPI api) : base(api)
        {
            Settings = new PluginSettings(this);
            repository = new TrackingRepository(GetPluginUserDataPath());
            try
            {
                var load = repository.Load();
                tracking = load.Database;
                startupWarning = load.Warning;
            }
            catch (UnsupportedTrackingSchemaException ex)
            {
                persistenceBlocked = true;
                tracking = new TrackingDatabase();
                startupWarning = "追跡データはこのプラグインより新しいSchemaで作成されています。安全のため更新チェックと保存を無効化しました。\n" + ex.Message;
            }
            tagService = new PlayniteTagService(api);
            BuildRuntimeServices();
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;
        public override UserControl GetSettingsView(bool firstRunView) => new PluginSettingsView();

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(startupWarning))
            {
                Logger.Warn(startupWarning);
                PlayniteApi.Dialogs.ShowMessage(startupWarning, "DLsite Update Monitor");
                startupWarning = null;
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Never serialize the tracking dictionary concurrently with an active check/mutation.
            // Periodic saves during checks mean skipping this final save is safer than racing it.
            if (!operationLock.Wait(0))
            {
                Logger.Warn("Application stopped while a DLsite monitor operation was active; final save was skipped to avoid a concurrent snapshot write.");
                return;
            }

            try
            {
                if (!persistenceBlocked)
                {
                    try { repository.Save(tracking, DateTimeOffset.UtcNow); }
                    catch (Exception ex) { Logger.Error(ex, "Failed to save tracking database during shutdown."); }
                }
                httpClient?.Dispose();
            }
            finally
            {
                operationLock.Release();
                operationLock.Dispose();
            }
        }

        public void ReloadRuntimeSettings()
        {
            if (operationLock.CurrentCount == 0)
            {
                // A running check keeps the old immutable runtime options until it finishes.
                return;
            }
            BuildRuntimeServices();
            // Reconcile existing plugin-owned tags immediately when the user toggles tag integration.
            ApplyTags(tracking.Games.Keys.ToList());
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "全ゲームを今すぐ確認",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => CheckGames(PlayniteApi.Database.Games.ToList(), true)
            };
            yield return new MainMenuItem
            {
                Description = "キャッシュを利用して確認",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => CheckGames(PlayniteApi.Database.Games.ToList(), false)
            };
            yield return new MainMenuItem
            {
                Description = "DLsiteリンク診断",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => DiagnoseLinks()
            };
            yield return new MainMenuItem
            {
                Description = "キャッシュをクリア",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => { cache.Clear(); PlayniteApi.Dialogs.ShowMessage("キャッシュをクリアしました。", "DLsite Update Monitor"); }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "今すぐ確認",
                MenuSection = "DLsite Update Monitor",
                Action = a => CheckGames(a.Games, true)
            };
            yield return new GameMenuItem
            {
                Description = "DLsiteページを開く",
                MenuSection = "DLsite Update Monitor",
                Action = a => OpenDlsitePage(a.Games)
            };
            yield return new GameMenuItem
            {
                Description = "現在の変更を適用済みにする",
                MenuSection = "DLsite Update Monitor",
                Action = a => Acknowledge(a.Games, false)
            };
            yield return new GameMenuItem
            {
                Description = "現在の変更を無視する",
                MenuSection = "DLsite Update Monitor",
                Action = a => Acknowledge(a.Games, true)
            };
            yield return new GameMenuItem
            {
                Description = "監視状態をリセット",
                MenuSection = "DLsite Update Monitor",
                Action = a => ResetMonitoring(a.Games)
            };
        }

        private void BuildRuntimeServices()
        {
            var oldClient = httpClient;
            var options = new DlsiteHttpOptions
            {
                MinimumRequestInterval = TimeSpan.FromSeconds(Settings.RequestIntervalSeconds),
                Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds),
                RetryCount = Settings.RetryCount
            };
            httpClient = DlsiteHttpClient.CreateDefault(options);
            cache = new SnapshotCache(TimeSpan.FromHours(Settings.CacheHours));
            stateMachine = new TrackingStateMachine(historyLimit: Settings.HistoryLimit);
            checkService = new UpdateCheckService(httpClient, new DlsitePageParser(), stateMachine, cache);
            oldClient?.Dispose();
        }

        private void CheckGames(List<Game> games, bool forceRefresh)
        {
            if (games == null || games.Count == 0) return;
            if (!EnsurePersistenceWritable()) return;
            if (!operationLock.Wait(0))
            {
                PlayniteApi.Dialogs.ShowMessage("別のDLsite更新チェックを実行中です。", "DLsite Update Monitor");
                return;
            }

            var results = new List<GameRunResult>();
            try
            {
                var progressResult = PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.ProgressMaxValue = games.Count;
                    progress.CurrentProgressValue = 0;
                    Task.Run(async () =>
                    {
                        var productResults = new Dictionary<string, ProductCheckResult>(StringComparer.OrdinalIgnoreCase);
                        var processed = 0;
                        foreach (var game in games)
                        {
                            if (progress.CancelToken.IsCancellationRequested) break;
                            processed++;
                            progress.CurrentProgressValue = processed;
                            progress.Text = $"確認中... ({processed}/{games.Count}) {game.Name}";

                            var resolution = linkResolver.Resolve(game.Links?.Select(l => l.Url));
                            if (resolution.Status == LinkResolutionStatus.NoDlsiteLink)
                            {
                                results.Add(GameRunResult.CreateSkipped(game));
                                continue;
                            }

                            var record = GetOrCreateRecord(game.Id);
                            if (resolution.Status != LinkResolutionStatus.Resolved)
                            {
                                var message = resolution.Reason ?? "DLsiteリンクを一意に解決できません。";
                                stateMachine.RecordCheckFailure(record, CheckHealth.LinkError, new CheckError
                                {
                                    Type = CheckHealth.LinkError,
                                    OccurredAtUtc = DateTimeOffset.UtcNow,
                                    Message = message
                                }, DateTimeOffset.UtcNow);
                                results.Add(GameRunResult.Failed(game, record, CheckHealth.LinkError, message));
                                continue;
                            }

                            var target = resolution.Target;
                            ProductCheckResult checkedResult;
                            ProductCheckResult priorProductResult;
                            if (productResults.TryGetValue(target.ProductId, out priorProductResult))
                            {
                                if (HasReusableRemoteObservation(priorProductResult))
                                {
                                    // Remote observation reuse is independent from the first game's local
                                    // comparison state. A bad acknowledged snapshot in game A must not poison
                                    // game B when both point at the same RJ product.
                                    checkedResult = await checkService.CheckAsync(record, target, false, progress.CancelToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    // No safe remote observation exists, so share the first product-level failure
                                    // and never retry the same product again inside one batch.
                                    stateMachine.RecordCheckFailure(record, priorProductResult.Health, new CheckError
                                    {
                                        Type = priorProductResult.Health,
                                        OccurredAtUtc = DateTimeOffset.UtcNow,
                                        Message = priorProductResult.Message,
                                        Url = target.RegisteredUrl
                                    }, DateTimeOffset.UtcNow);
                                    checkedResult = new ProductCheckResult
                                    {
                                        ProductId = target.ProductId,
                                        Health = priorProductResult.Health,
                                        Message = priorProductResult.Message
                                    };
                                }
                            }
                            else
                            {
                                try
                                {
                                    checkedResult = await checkService.CheckAsync(record, target, forceRefresh, progress.CancelToken).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, "Unexpected check failure for " + game.Name);
                                    stateMachine.RecordCheckFailure(record, CheckHealth.NetworkError, new CheckError
                                    {
                                        Type = CheckHealth.NetworkError,
                                        OccurredAtUtc = DateTimeOffset.UtcNow,
                                        Message = ex.Message,
                                        Url = target.RegisteredUrl
                                    }, DateTimeOffset.UtcNow);
                                    checkedResult = new ProductCheckResult
                                    {
                                        ProductId = target.ProductId,
                                        Health = CheckHealth.NetworkError,
                                        Message = ex.Message
                                    };
                                }
                                productResults[target.ProductId] = checkedResult;
                            }

                            results.Add(GameRunResult.From(game, record, checkedResult));
                            if (processed % 10 == 0)
                            {
                                repository.Save(tracking, DateTimeOffset.UtcNow);
                            }
                        }
                        repository.Save(tracking, DateTimeOffset.UtcNow);
                    }).GetAwaiter().GetResult();
                }, new GlobalProgressOptions("DLsite更新情報を確認中...", true) { IsIndeterminate = false });

                if (progressResult.Error != null)
                {
                    Logger.Error(progressResult.Error, "DLsite update check failed.");
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "更新チェック中にエラーが発生しました。追跡データの安全性を優先してタグ反映を中止しました。\n\n"
                        + progressResult.Error.Message,
                        "DLsite Update Monitor");
                    return;
                }

                ApplyTags(results.Select(r => r.GameId).Distinct().ToList());
                ShowSummary(results, progressResult.Canceled);
            }
            finally
            {
                operationLock.Release();
            }
        }

        private void ApplyTags(List<Guid> gameIds)
        {
            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var id in gameIds)
                    {
                        var game = PlayniteApi.Database.Games.Get(id);
                        if (game == null) continue;
                        GameTrackingRecord record;
                        if (!tracking.Games.TryGetValue(id, out record)) continue;
                        tagService.Apply(game, record.MonitoringState, Settings.EnableTags);
                        PlayniteApi.Database.Games.Update(game);
                    }
                }
            });
        }

        private void ShowSummary(List<GameRunResult> results, bool canceled)
        {
            var monitored = results.Count(r => !r.Skipped);
            var baseline = results.Count(r => r.ComparisonOutcome == ComparisonOutcome.BaselineCreated);
            var update = results.Count(r => r.State == MonitoringState.PendingUpdateInfo || r.State == MonitoringState.PendingUpdateAndFileChange);
            var file = results.Count(r => r.State == MonitoringState.PendingFileChange);
            var errors = results.Count(r => r.Health != CheckHealth.Healthy && !r.Skipped);
            var skipped = results.Count(r => r.Skipped);

            var text = (canceled ? "※ ユーザー操作により途中でキャンセルされました。処理済み分のみ反映しています。\n\n" : "")
                + $"対象: {monitored} 件\n"
                + $"監視開始: {baseline} 件\n"
                + $"更新あり: {update} 件\n"
                + $"配布物変更: {file} 件\n"
                + $"エラー/要確認: {errors} 件\n"
                + $"DLsiteリンクなし: {skipped} 件";
            PlayniteApi.Dialogs.ShowMessage(text, "DLsite Update Monitor");
        }

        private void DiagnoseLinks()
        {
            var resolved = 0;
            var missing = 0;
            var invalid = 0;
            var ambiguous = 0;
            foreach (var game in PlayniteApi.Database.Games)
            {
                switch (linkResolver.Resolve(game.Links?.Select(l => l.Url)).Status)
                {
                    case LinkResolutionStatus.Resolved: resolved++; break;
                    case LinkResolutionStatus.NoDlsiteLink: missing++; break;
                    case LinkResolutionStatus.Invalid: invalid++; break;
                    case LinkResolutionStatus.Ambiguous: ambiguous++; break;
                }
            }

            PlayniteApi.Dialogs.ShowMessage(
                $"正常: {resolved}\nリンクなし: {missing}\n不正なDLsiteリンク: {invalid}\n複数作品リンク: {ambiguous}",
                "DLsiteリンク診断");
        }

        private void OpenDlsitePage(List<Game> games)
        {
            if (games == null || games.Count == 0) return;
            if (games.Count > 1)
            {
                PlayniteApi.Dialogs.ShowMessage("複数選択時はDLsiteページを開きません。1ゲームだけ選択してください。", "DLsite Update Monitor");
                return;
            }
            var result = linkResolver.Resolve(games[0].Links?.Select(l => l.Url));
            if (result.Status != LinkResolutionStatus.Resolved)
            {
                PlayniteApi.Dialogs.ShowMessage(result.Reason ?? "有効なDLsite作品リンクがありません。", "DLsite Update Monitor");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(result.Target.RegisteredUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "DLsite Update Monitor");
            }
        }

        private void Acknowledge(List<Game> games, bool ignored)
        {
            if (games == null || games.Count == 0) return;
            if (!EnsurePersistenceWritable()) return;
            if (!TryEnterMutationOperation()) return;
            try
            {
                var changed = new List<Guid>();
                foreach (var game in games)
                {
                    GameTrackingRecord record;
                    if (!tracking.Games.TryGetValue(game.Id, out record)) continue;
                    if (stateMachine.AcknowledgeCurrent(record, ignored, DateTimeOffset.UtcNow)) changed.Add(game.Id);
                }
                repository.Save(tracking, DateTimeOffset.UtcNow);
                ApplyTags(changed);
                PlayniteApi.Dialogs.ShowMessage($"{changed.Count}件を{(ignored ? "無視済み" : "適用済み")}にしました。", "DLsite Update Monitor");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to acknowledge DLsite tracking state.");
                PlayniteApi.Dialogs.ShowErrorMessage("追跡状態を保存できませんでした。\n\n" + ex.Message, "DLsite Update Monitor");
            }
            finally
            {
                operationLock.Release();
            }
        }

        private void ResetMonitoring(List<Game> games)
        {
            if (games == null || games.Count == 0) return;
            if (!EnsurePersistenceWritable()) return;
            if (!TryEnterMutationOperation()) return;
            try
            {
                var answer = PlayniteApi.Dialogs.ShowMessage(
                    $"選択した{games.Count}件の監視Baselineをリセットします。履歴にはリセット操作を残します。次回チェックは初回監視開始扱いになります。続行しますか？",
                    "DLsite Update Monitor", MessageBoxButton.YesNo);
                if (answer != MessageBoxResult.Yes) return;

                var changed = new List<Guid>();
                foreach (var game in games)
                {
                    GameTrackingRecord record;
                    if (!tracking.Games.TryGetValue(game.Id, out record)) continue;
                    stateMachine.ResetMonitoring(record, DateTimeOffset.UtcNow);
                    changed.Add(game.Id);
                }
                repository.Save(tracking, DateTimeOffset.UtcNow);
                ApplyTags(changed);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to reset DLsite tracking state.");
                PlayniteApi.Dialogs.ShowErrorMessage("追跡状態を保存できませんでした。\n\n" + ex.Message, "DLsite Update Monitor");
            }
            finally
            {
                operationLock.Release();
            }
        }

        private static bool HasReusableRemoteObservation(ProductCheckResult result)
        {
            return result != null && result.HasReusableSnapshot;
        }

        private bool TryEnterMutationOperation()
        {
            if (operationLock.Wait(0)) return true;
            PlayniteApi.Dialogs.ShowMessage(
                "更新チェックまたは別の状態変更を実行中です。完了後にもう一度実行してください。",
                "DLsite Update Monitor");
            return false;
        }

        private bool EnsurePersistenceWritable()
        {
            if (!persistenceBlocked) return true;
            PlayniteApi.Dialogs.ShowMessage(
                "追跡データのSchemaが新しすぎるため、安全のため変更処理を無効化しています。データを上書きしません。",
                "DLsite Update Monitor");
            return false;
        }

        private GameTrackingRecord GetOrCreateRecord(Guid gameId)
        {
            GameTrackingRecord record;
            if (!tracking.Games.TryGetValue(gameId, out record))
            {
                record = new GameTrackingRecord { PlayniteGameId = gameId };
                tracking.Games[gameId] = record;
            }
            return record;
        }

        private sealed class GameRunResult
        {
            public Guid GameId { get; set; }
            public bool Skipped { get; set; }
            public CheckHealth Health { get; set; }
            public MonitoringState State { get; set; }
            public ComparisonOutcome? ComparisonOutcome { get; set; }

            public static GameRunResult CreateSkipped(Game game) => new GameRunResult { GameId = game.Id, Skipped = true };
            public static GameRunResult Failed(Game game, GameTrackingRecord record, CheckHealth health, string message) => new GameRunResult
            {
                GameId = game.Id,
                Health = health,
                State = record?.MonitoringState ?? MonitoringState.Uninitialized
            };
            public static GameRunResult From(Game game, GameTrackingRecord record, ProductCheckResult result) => new GameRunResult
            {
                GameId = game.Id,
                Health = result.Health,
                State = record.MonitoringState,
                ComparisonOutcome = result.Comparison?.Outcome
            };
        }
    }
}
