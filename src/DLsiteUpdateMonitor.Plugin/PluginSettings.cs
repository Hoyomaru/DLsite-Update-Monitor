using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace DLsiteUpdateMonitor
{
    public sealed class PluginSettings : ObservableObject, ISettings
    {
        private readonly DLsiteUpdateMonitorPlugin plugin;
        private PluginSettings previous;

        public int RequestIntervalSeconds { get; set; } = 2;
        public int TimeoutSeconds { get; set; } = 20;
        public int RetryCount { get; set; } = 2;
        public int CacheHours { get; set; } = 24;
        public int HistoryLimit { get; set; } = 50;
        public bool EnableTags { get; set; } = true;

        public PluginSettings() { }

        public PluginSettings(DLsiteUpdateMonitorPlugin plugin)
        {
            this.plugin = plugin;
            try
            {
                var saved = plugin.LoadPluginSettings<PluginSettings>();
                if (saved != null)
                {
                    RequestIntervalSeconds = saved.RequestIntervalSeconds;
                    TimeoutSeconds = saved.TimeoutSeconds;
                    RetryCount = saved.RetryCount;
                    CacheHours = saved.CacheHours;
                    HistoryLimit = saved.HistoryLimit;
                    EnableTags = saved.EnableTags;
                }
            }
            catch (Exception)
            {
                // Defaults are safer than failing plugin startup because settings.json is malformed.
            }
        }

        public void BeginEdit()
        {
            previous = Copy();
        }

        public void CancelEdit()
        {
            if (previous == null) return;
            RequestIntervalSeconds = previous.RequestIntervalSeconds;
            TimeoutSeconds = previous.TimeoutSeconds;
            RetryCount = previous.RetryCount;
            CacheHours = previous.CacheHours;
            HistoryLimit = previous.HistoryLimit;
            EnableTags = previous.EnableTags;
        }

        public void EndEdit()
        {
            plugin?.SavePluginSettings(this);
            plugin?.ReloadRuntimeSettings();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (RequestIntervalSeconds < 1 || RequestIntervalSeconds > 60)
                errors.Add("リクエスト間隔は1～60秒にしてください。");
            if (TimeoutSeconds < 5 || TimeoutSeconds > 120)
                errors.Add("タイムアウトは5～120秒にしてください。");
            if (RetryCount < 0 || RetryCount > 5)
                errors.Add("リトライ回数は0～5回にしてください。");
            if (CacheHours < 1 || CacheHours > 168)
                errors.Add("キャッシュ有効期間は1～168時間にしてください。");
            if (HistoryLimit < 10 || HistoryLimit > 500)
                errors.Add("履歴上限は10～500件にしてください。");
            return errors.Count == 0;
        }

        private PluginSettings Copy()
        {
            return new PluginSettings
            {
                RequestIntervalSeconds = RequestIntervalSeconds,
                TimeoutSeconds = TimeoutSeconds,
                RetryCount = RetryCount,
                CacheHours = CacheHours,
                HistoryLimit = HistoryLimit,
                EnableTags = EnableTags
            };
        }
    }
}
