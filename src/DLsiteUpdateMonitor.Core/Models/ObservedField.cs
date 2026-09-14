namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class ObservedField<T>
    {
        public ObservationState State { get; set; }
        public string Raw { get; set; }
        public string Normalized { get; set; }
        public T Value { get; set; }

        public static ObservedField<T> Missing()
        {
            return new ObservedField<T> { State = ObservationState.Missing };
        }

        public static ObservedField<T> Unparsed(string raw)
        {
            return new ObservedField<T>
            {
                State = ObservationState.Unparsed,
                Raw = raw
            };
        }

        public static ObservedField<T> Parsed(string raw, string normalized, T value)
        {
            return new ObservedField<T>
            {
                State = ObservationState.Parsed,
                Raw = raw,
                Normalized = normalized,
                Value = value
            };
        }
    }
}
