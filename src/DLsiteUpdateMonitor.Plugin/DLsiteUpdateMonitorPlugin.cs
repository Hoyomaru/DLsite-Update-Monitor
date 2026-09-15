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
                Description = "エラー/要確認のゲームを再確認",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => RecheckFailedGames()
            };
            yield return new MainMenuItem
            {
                Description = "DLsiteリンク診断",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => DiagnoseLinks()
            };
            yield return new MainMenuItem
            {
                Description = "孤立した追跡データを整理",
                MenuSection = "@DLsite Update Monitor",
                Action = _ => CleanupOrphanedTracking()
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
                Description = "監視詳細を表示",
                MenuSection = "DLsite Update Monitor",
                Action = a => ShowTrackingDetails(a.Games)
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
            var working = TrackingDatabaseCloner.Clone(tracking);
            try
            {
                var progressResult = PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.ProgressMaxValue = games.Count;
                    progress.CurrentProgressValue = 0;
                    Task.Run(async () =>
                    {
                        // Keep only product-level reuse metadata here. Full HTTP/HTML payloads belong to
                        // per-game results and the SnapshotCache, and should not remain rooted for the batch.
                        var productResults = new Dictionary<string, BatchProductResult>(StringComparer.OrdinalIgnoreCase);
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

                            var record = GetOrCreateRecord(working, game.Id);
                            if (resolution.Status != LinkResolutionStatus.Resolved)
                            {
                                var message = resolution.Reason ?? "DLsiteリンクを一意に解決できません。";
                                stateMachine.RecordCheckFailure(record, CheckHealth.LinkError, new CheckError
                                {
                                    Type = CheckHealth.LinkError,
                                    OccurredAtUtc = DateTimeOffset.UtcNow,
                                    Message = message
                                }, DateTimeOffset.UtcNow);
                                results.Add(GameRunResult.Failed(game, record, CheckHealth.LinkError));
                                continue;
                            }

                            var target = resolution.Target;
                            ProductCheckResult checkedResult;
                            BatchProductResult priorProductResult;
                            if (productResults.TryGetValue(target.ProductId, out priorProductResult))
                            {
                                if (priorProductResult.HasReusableSnapshot)
                                {
                                    // Reuse only the validated remote observation. Each game's local
                                    // comparison state still runs independently against that observation.
                                    checkedResult = await checkService.CheckAsync(record, target, false, progress.CancelToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    // Only remote/product failures are stored in productResults. A local
                                    // record failure such as a changed registered product ID is never shared.
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
                                        FailureScope = ProductCheckFailureScope.RemoteProduct,
                                        Health = priorProductResult.Health,
                                        Message = priorProductResult.Message
                                    };
                                }
                            }
                            else
                            {
                                checkedResult = await CheckOneAsync(game, record, target, forceRefresh, progress.CancelToken).ConfigureAwait(false);
                                var batchResult = BatchProductResult.From(checkedResult);
                                if (batchResult != null)
                                {
                                    productResults[target.ProductId] = batchResult;
                                }
                            }

                            results.Add(GameRunResult.From(game, record, checkedResult));
                            if (processed % 10 == 0)
                            {
                                repository.Save(working, DateTimeOffset.UtcNow);
                                tracking = TrackingDatabaseCloner.Clone(working);
                            }
                        }

                        repository.Save(working, DateTimeOffset.UtcNow);
                        tracking = working;
                    }).GetAwaiter().GetResult();
                }, new GlobalProgressOptions("DLsite更新情報を確認中...", true) { IsIndeterminate = false });

                if (progressResult.Error != null)
                {
                    Logger.Error(progressResult.Error, "DLsite update check failed.");
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "更新チェック中にエラーが発生しました。最後に正常保存できた追跡状態を維持し、タグ反映を中止しました。\n\n"
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

        private async Task<ProductCheckResult> CheckOneAsync(
            Game game,
            GameTrackingRecord record,
            DlsiteTarget target,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            try
            {
                return await checkService.CheckAsync(record, target, forceRefresh, cancellationToken).ConfigureAwait(false);
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
                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    Health = CheckHealth.NetworkError,
                    Message = ex.Message
                };
            }
        }

        private void RecheckFailedGames()
        {
            var games = PlayniteApi.Database.Games
                .Where(game =>
                {
                    GameTrackingRecord record;
                    return tracking.Games.TryGetValue(game.Id, out record)
                        && record != null
                        && record.LastCheckHealth != CheckHealth.Healthy
                        && record.LastCheckHealth != CheckHealth.NeverChecked;
                })
                .ToList();

            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("再確認が必要なゲームはありません。", "DLsite Update Monitor");
                return;
            }

            CheckGames(games, true);
        }

        private void CleanupOrphanedTracking()
        {
            if (!EnsurePersistenceWritable()) return;
            if (!TryEnterMutationOperation()) return;

            try
            {
                var liveGameIds = new HashSet<Guid>(PlayniteApi.Database.Games.Select(game => game.Id));
                var orphanIds = tracking.Games.Keys
                    .Where(id => !liveGameIds.Contains(id))
                    .OrderBy(id => id)
                    .ToList();

                if (orphanIds.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("孤立した追跡データはありません。", "DLsite Update Monitor");
                    return;
                }

                const int previewLimit = 20;
                var preview = orphanIds.Take(previewLimit).Select(id =>
                {
                    GameTrackingRecord record;
                    tracking.Games.TryGetValue(id, out record);
                    var productId = record?.RequestedProductId ?? record?.ResolvedProductId ?? "作品ID不明";
                    return productId + " / GameId: " + id;
                }).ToList();

                var previewText = string.Join("\n", preview);
                if (orphanIds.Count > previewLimit)
                {
                    previewText += "\n...ほか " + (orphanIds.Count - previewLimit) + " 件";
                }

                var answer = PlayniteApi.Dialogs.ShowMessage(
                    "Playnite上に存在しない追跡データが " + orphanIds.Count + " 件あります。\n"
                    + "tracking.jsonからのみ削除します。現在存在するゲームやタグは変更しません。\n\n"
                    + previewText
                    + "\n\n削除しますか？",
                    "DLsite Update Monitor",
                    MessageBoxButton.YesNo);
                if (answer != MessageBoxResult.Yes) return;

                var working = TrackingDatabaseCloner.Clone(tracking);
                foreach (var id in orphanIds)
                {
                    working.Games.Remove(id);
                }

                repository.Save(working, DateTimeOffset.UtcNow);
                tracking = working;
                PlayniteApi.Dialogs.ShowMessage(orphanIds.Count + "件の孤立した追跡データを削除しました。", "DLsite Update Monitor");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clean orphaned DLsite tracking records.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "孤立した追跡データを保存できませんでした。変更は確定していません。\n\n" + ex.Message,
                    "DLsite Update Monitor");
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
                        if (!tracking.Games.TryGetValue(id, out record) || record == null) continue;
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
            var file = results.Count(r => r.State == MonitoringState.PendingFileChange || r.State == MonitoringState.PendingUpdateAndFileChange);
            var both = results.Count(r => r.State == MonitoringState.PendingUpdateAndFileChange);
            var errors = results.Count(r => r.Health != CheckHealth.Healthy && !r.Skipped);
            var skipped = results.Count(r => r.Skipped);

            var text = (canceled ? "※ ユーザー操作により途中でキャンセルされました。処理済み分のみ反映しています。\n\n" : "")
                + $"対象: {monitored} 件\n"
                + $"監視開始: {baseline} 件\n"
                + $"更新あり: {update} 件\n"
                + $"配布物変更: {file} 件\n"
                + $"両方変更: {both} 件\n"
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
            var details = new List<string>();

            foreach (var game in PlayniteApi.Database.Games)
            {
                var resolution = linkResolver.Resolve(game.Links?.Select(l => l.Url));
                switch (resolution.Status)
                {
                    case LinkResolutionStatus.Resolved:
                        resolved++;
                        break;
                    case LinkResolutionStatus.NoDlsiteLink:
                        missing++;
                        details.Add("リンクなし: " + game.Name);
                        break;
                    case LinkResolutionStatus.Invalid:
                        invalid++;
                        details.Add("不正: " + game.Name + FormatReason(resolution.Reason));
                        break;
                    case LinkResolutionStatus.Ambiguous:
                        ambiguous++;
                        var ids = resolution.Candidates == null
                            ? string.Empty
                            : string.Join(", ", resolution.Candidates.Select(c => c.ProductId));
                        details.Add("複数作品: " + game.Name + (string.IsNullOrWhiteSpace(ids) ? string.Empty : " [" + ids + "]"));
                        break;
                }
            }

            const int detailLimit = 40;
            var shown = details.Take(detailLimit).ToList();
            var detailText = shown.Count == 0
                ? string.Empty
                : "\n\n要確認:\n" + string.Join("\n", shown);
            if (details.Count > detailLimit)
            {
                detailText += "\n...ほか " + (details.Count - detailLimit) + " 件";
            }

            PlayniteApi.Dialogs.ShowMessage(
                $"正常: {resolved}\nリンクなし: {missing}\n不正なDLsiteリンク: {invalid}\n複数作品リンク: {ambiguous}" + detailText,
                "DLsiteリンク診断");
        }

        private static string FormatReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ? string.Empty : " — " + reason;
        }

        private void ShowTrackingDetails(List<Game> games)
        {
            if (games == null || games.Count == 0) return;
            if (games.Count != 1)
            {
                PlayniteApi.Dialogs.ShowMessage("監視詳細は1ゲームだけ選択して表示してください。", "DLsite Update Monitor");
                return;
            }

            var game = games[0];
            GameTrackingRecord record;
            if (!tracking.Games.TryGetValue(game.Id, out record) || record == null)
            {
                PlayniteApi.Dialogs.ShowMessage("このゲームにはまだ監視データがありません。", "DLsite Update Monitor");
                return;
            }

            var lines = new List<string>
            {
                game.Name,
                "状態: " + FormatMonitoringState(record.MonitoringState),
                "最終チェック: " + FormatCheckHealth(record.LastCheckHealth),
                "作品ID: " + (record.RequestedProductId ?? record.ResolvedProductId ?? "未確定"),
                "初回チェック: " + FormatTimestamp(record.FirstCheckedAtUtc),
                "最終試行: " + FormatTimestamp(record.LastAttemptAtUtc),
                "最終成功: " + FormatTimestamp(record.LastSuccessfulCheckAtUtc),
                string.Empty
            };

            AddSnapshotDetails(lines, "確認済み基準", record.AcknowledgedSnapshot);
            AddSnapshotDetails(lines, "現在の比較可能状態", record.CurrentSnapshot);
            AddSnapshotDetails(lines, "直近の観測", record.LastObservation);

            if (record.LastError != null)
            {
                lines.Add("最終エラー:");
                lines.Add("  種別: " + FormatCheckHealth(record.LastError.Type));
                lines.Add("  時刻: " + FormatTimestamp(record.LastError.OccurredAtUtc));
                if (record.LastError.HttpStatusCode.HasValue)
                {
                    lines.Add("  HTTP: " + record.LastError.HttpStatusCode.Value);
                }
                if (!string.IsNullOrWhiteSpace(record.LastError.Message))
                {
                    lines.Add("  内容: " + record.LastError.Message);
                }
                lines.Add(string.Empty);
            }

            var history = (record.History ?? new List<TrackingHistoryEntry>())
                .Where(entry => entry != null)
                .OrderByDescending(entry => entry.TimestampUtc)
                .Take(10)
                .ToList();
            if (history.Count > 0)
            {
                lines.Add("最近の履歴:");
                foreach (var entry in history)
                {
                    var changes = entry.Changes == ChangeFlags.None ? string.Empty : " / " + entry.Changes;
                    lines.Add("  " + FormatTimestamp(entry.TimestampUtc) + "  " + entry.EventType + changes);
                }
            }

            PlayniteApi.Dialogs.ShowMessage(string.Join("\n", lines), "DLsite監視詳細");
        }

        private static void AddSnapshotDetails(List<string> lines, string label, RemoteSnapshot snapshot)
        {
            lines.Add(label + ":");
            if (snapshot == null)
            {
                lines.Add("  なし");
                lines.Add(string.Empty);
                return;
            }

            lines.Add("  作品: " + (snapshot.WorkName ?? "不明"));
            lines.Add("  ProductId: " + (snapshot.ProductId ?? "不明"));
            lines.Add("  更新情報: " + FormatUpdateInfo(snapshot.UpdateInfo));
            lines.Add("  ファイル容量: " + FormatFileSize(snapshot.FileSize));
            lines.Add("  取得時刻: " + FormatTimestamp(snapshot.FetchedAtUtc));
            lines.Add(string.Empty);
        }

        private static string FormatUpdateInfo(ObservedField<string> field)
        {
            if (field == null) return "不明";
            switch (field.State)
            {
                case ObservationState.Missing: return "なし";
                case ObservationState.Unparsed: return "解析不能" + FormatRaw(field.Raw);
                case ObservationState.Parsed: return field.Normalized ?? field.Value ?? "(空)";
                default: return field.State.ToString();
            }
        }

        private static string FormatFileSize(ObservedField<long> field)
        {
            if (field == null) return "不明";
            switch (field.State)
            {
                case ObservationState.Missing: return "なし";
                case ObservationState.Unparsed: return "解析不能" + FormatRaw(field.Raw);
                case ObservationState.Parsed: return field.Normalized ?? field.Value + " B";
                default: return field.State.ToString();
            }
        }

        private static string FormatRaw(string raw)
        {
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : " (" + raw + ")";
        }

        private static string FormatMonitoringState(MonitoringState state)
        {
            switch (state)
            {
                case MonitoringState.Uninitialized: return "未初期化";
                case MonitoringState.Clean: return "変更なし";
                case MonitoringState.PendingUpdateInfo: return "更新情報に変更あり";
                case MonitoringState.PendingFileChange: return "配布物に変更あり";
                case MonitoringState.PendingUpdateAndFileChange: return "更新情報・配布物の両方に変更あり";
                default: return state.ToString();
            }
        }

        private static string FormatCheckHealth(CheckHealth health)
        {
            switch (health)
            {
                case CheckHealth.NeverChecked: return "未チェック";
                case CheckHealth.Healthy: return "正常";
                case CheckHealth.NetworkError: return "ネットワークエラー";
                case CheckHealth.RateLimited: return "レート制限";
                case CheckHealth.AccessDenied: return "アクセス拒否";
                case CheckHealth.Timeout: return "タイムアウト";
                case CheckHealth.ProductUnavailable: return "作品利用不可";
                case CheckHealth.RedirectedToDifferentProduct: return "別作品へリダイレクト";
                case CheckHealth.ParseError: return "解析エラー";
                case CheckHealth.ParseDegraded: return "解析要確認";
                case CheckHealth.LinkError: return "リンクエラー";
                case CheckHealth.Cancelled: return "キャンセル";
                default: return health.ToString();
            }
        }

        private static string FormatTimestamp(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "なし";
        }

        private static string FormatTimestamp(DateTimeOffset value)
        {
            return value == default(DateTimeOffset)
                ? "なし"
                : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
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
                var working = TrackingDatabaseCloner.Clone(tracking);
                var changed = new List<Guid>();
                foreach (var game in games)
                {
                    GameTrackingRecord record;
                    if (!working.Games.TryGetValue(game.Id, out record) || record == null) continue;
                    if (stateMachine.AcknowledgeCurrent(record, ignored, DateTimeOffset.UtcNow)) changed.Add(game.Id);
                }
                repository.Save(working, DateTimeOffset.UtcNow);
                tracking = working;
                ApplyTags(changed);
                PlayniteApi.Dialogs.ShowMessage($"{changed.Count}件を{(ignored ? "無視済み" : "適用済み")}にしました。", "DLsite Update Monitor");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to acknowledge DLsite tracking state.");
                PlayniteApi.Dialogs.ShowErrorMessage("追跡状態を保存できませんでした。変更は確定していません。\n\n" + ex.Message, "DLsite Update Monitor");
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

                var working = TrackingDatabaseCloner.Clone(tracking);
                var changed = new List<Guid>();
                foreach (var game in games)
                {
                    GameTrackingRecord record;
                    if (!working.Games.TryGetValue(game.Id, out record) || record == null) continue;
                    stateMachine.ResetMonitoring(record, DateTimeOffset.UtcNow);
                    changed.Add(game.Id);
                }
                repository.Save(working, DateTimeOffset.UtcNow);
                tracking = working;
                ApplyTags(changed);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to reset DLsite tracking state.");
                PlayniteApi.Dialogs.ShowErrorMessage("追跡状態を保存できませんでした。変更は確定していません。\n\n" + ex.Message, "DLsite Update Monitor");
            }
            finally
            {
                operationLock.Release();
            }
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

        private static GameTrackingRecord GetOrCreateRecord(TrackingDatabase database, Guid gameId)
        {
            GameTrackingRecord record;
            if (!database.Games.TryGetValue(gameId, out record) || record == null)
            {
                record = new GameTrackingRecord { PlayniteGameId = gameId };
                database.Games[gameId] = record;
            }
            return record;
        }

        private sealed class BatchProductResult
        {
            public bool HasReusableSnapshot { get; set; }
            public CheckHealth Health { get; set; }
            public string Message { get; set; }

            public static BatchProductResult From(ProductCheckResult result)
            {
                if (result == null
                    || (!result.HasReusableSnapshot && result.FailureScope != ProductCheckFailureScope.RemoteProduct))
                {
                    return null;
                }

                return new BatchProductResult
                {
                    HasReusableSnapshot = result.HasReusableSnapshot,
                    Health = result.Health,
                    Message = result.Message
                };
            }
        }

        private sealed class GameRunResult
        {
            public Guid GameId { get; set; }
            public bool Skipped { get; set; }
            public CheckHealth Health { get; set; }
            public MonitoringState State { get; set; }
            public ComparisonOutcome? ComparisonOutcome { get; set; }

            public static GameRunResult CreateSkipped(Game game) => new GameRunResult { GameId = game.Id, Skipped = true };
            public static GameRunResult Failed(Game game, GameTrackingRecord record, CheckHealth health) => new GameRunResult
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
