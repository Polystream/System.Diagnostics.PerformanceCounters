namespace System.Diagnostics
{
    public class CounterCreationData
    {
        string _counterHelp = string.Empty;
        string _counterName = string.Empty;
        PerformanceCounterType _counterType = PerformanceCounterType.NumberOfItems32;

        public CounterCreationData()
        { }

        public CounterCreationData(string counterName, string counterHelp, PerformanceCounterType counterType)
        {
            CounterType = counterType;
            CounterName = counterName;
            CounterHelp = counterHelp;
        }

        public PerformanceCounterType CounterType
        {
            get => _counterType;
            set
            {
                if (!Enum.IsDefined(typeof(PerformanceCounterType), value))
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(PerformanceCounterType));

                _counterType = value;
            }
        }

        public string CounterName
        {
            get => _counterName;
            set
            {
                PerformanceCounterCategory.CheckValidCounter(value);
                _counterName = value;
            }
        }

        public string CounterHelp
        {
            get => _counterHelp;
            set
            {
                PerformanceCounterCategory.CheckValidHelp(value);
                _counterHelp = value;
            }
        }
    }
}