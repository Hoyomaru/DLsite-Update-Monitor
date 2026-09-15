using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace DLsiteUpdateMonitor
{
    public sealed class PluginSettings : ObservableObject, ISettings
    {
        private const int DefaultRequestIntervalSeconds = 2;
        private const int DefaultTimeoutSeconds = 20;
        private const int DefaultRetryCount = 2;
        private const int DefaultCacheHours = 24;
        private const int DefaultHistoryLimit = 50;

        private readonly DLsiteUpdateMonitorPlugin plugin;
        private PluginSettings previous;

        public int RequestIntervalSeconds { get; set; } = DefaultRequestIntervalSeconds;
        public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
        public int RetryCount { get; set; } = DefaultRetryCount;
        public int CacheHours { get; set; } = DefaultCacheHours;
        public int HistoryLimit { get; set; } = DefaultHistoryLimit;
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
                    RequestIntervalSeconds = IsInRange(saved.RequestIntervalSeconds, 1, 60)
                        ? saved.RequestIntervalSeconds : DefaultRequestIntervalSeconds;
                    TimeoutSeconds = IsInRange(saved.TimeoutSeconds, 5, 120)
                        ? saved.TimeoutSeconds : DefaultTimeoutSeconds;
                    RetryCount = IsInRange(saved.RetryCount, 0, 5)
                        ? saved.RetryCount : DefaultRetryCount;
                    CacheHours = IsInRange(saved.CacheHours, 1, 168)
                        ? saved.CacheHours : DefaultCacheHours;
                    HistoryLimit = IsInRange(saved.HistoryLimit, 10, 500)
                        ? saved.HistoryLimit : DefaultHistoryLimit;
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
            if (!IsInRange(RequestIntervalSeconds, 1, 60))
                errors.Add("リクエスト間隔は1～60秒にしてください。");
            if (!IsInRange(TimeoutSeconds, 5, 120))
                errors.Add("タイムアウトは5～120秒にしてください。");
            if (!IsInRange(RetryCount, 0, 5))
                errors.Add("リトライ回数は0～5回にしてください。");
            if (!IsInRange(CacheHours, 1, 168))
                errors.Add("キャッシュ有効期間は1～168時間にしてください。");
            if (!IsInRange(HistoryLimit, 10, 500))
                errors.Add("履歴上限は10～500件にしてください。");
            return errors.Count == 0;
        }

        private static bool IsInRange(int value, int min, int max)
        {
            return value >= min && value <= max;
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
