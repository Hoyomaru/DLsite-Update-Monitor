using System;

namespace DLsiteUpdateMonitor.Core.Models
{
    public enum MonitoringState
    {
        Uninitialized = 0,
        Clean = 10,
        PendingUpdateInfo = 20,
        PendingFileChange = 30,
        PendingUpdateAndFileChange = 40
    }

    public enum CheckHealth
    {
        NeverChecked = 0,
        Healthy = 10,
        NetworkError = 20,
        RateLimited = 21,
        AccessDenied = 22,
        Timeout = 23,
        ProductUnavailable = 30,
        RedirectedToDifferentProduct = 31,
        ParseError = 40,
        ParseDegraded = 41,
        LinkError = 50,
        Cancelled = 60
    }

    public enum ObservationState
    {
        Missing = 0,
        Parsed = 10,
        Unparsed = 20
    }

    [Flags]
    public enum ChangeFlags
    {
        None = 0,
        UpdateInfo = 1,
        FileSize = 2
    }

    public enum ComparisonOutcome
    {
        BaselineCreated = 0,
        NoChange = 10,
        Changed = 20,
        Indeterminate = 30,
        IdentityMismatch = 40
    }

    public enum LinkResolutionStatus
    {
        NoDlsiteLink = 0,
        Resolved = 10,
        Ambiguous = 20,
        Invalid = 30
    }

    public enum HistoryEventType
    {
        BaselineCreated = 0,
        ChangeDetected = 10,
        ChangeUpdated = 11,
        ChangeReturnedToBaseline = 12,
        Applied = 20,
        Ignored = 21,
        MonitoringReset = 30,
        ProductRedirectDetected = 40
    }
}
