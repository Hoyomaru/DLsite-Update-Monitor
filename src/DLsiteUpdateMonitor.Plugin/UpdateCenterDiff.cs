namespace DLsiteUpdateMonitor
{
    internal sealed class UpdateCenterDiff
    {
        public bool Available { get; set; }
        public string Summary { get; set; }
        public string BeforeUpdateInfo { get; set; }
        public string AfterUpdateInfo { get; set; }
        public string BeforeFileSize { get; set; }
        public string AfterFileSize { get; set; }
        public bool UpdateInfoChanged { get; set; }
        public bool FileSizeChanged { get; set; }
    }
}
