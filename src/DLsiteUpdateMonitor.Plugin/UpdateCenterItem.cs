using System;

namespace DLsiteUpdateMonitor
{
    internal sealed class UpdateCenterItem
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string ProductId { get; set; }
        public string StateText { get; set; }
        public string HealthText { get; set; }
        public string LastCheckedText { get; set; }
        public string ChangeSummary { get; set; }
        public string FilterBucket { get; set; }
        public bool HasPendingChange { get; set; }
        public bool NeedsAttention { get; set; }
    }
}
