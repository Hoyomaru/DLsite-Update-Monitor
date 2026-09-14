namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class ComparisonResult
    {
        public ComparisonOutcome Outcome { get; set; }
        public ChangeFlags Changes { get; set; }
        public MonitoringState? TargetState { get; set; }
        public string Reason { get; set; }

        public static ComparisonResult Baseline()
        {
            return new ComparisonResult
            {
                Outcome = ComparisonOutcome.BaselineCreated,
                Changes = ChangeFlags.None,
                TargetState = MonitoringState.Clean,
                Reason = "No acknowledged snapshot exists; create initial baseline."
            };
        }
    }
}
