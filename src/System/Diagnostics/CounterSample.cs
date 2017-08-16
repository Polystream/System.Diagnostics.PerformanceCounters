using System.Runtime.InteropServices;

namespace System.Diagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CounterSample
    {
        public static CounterSample Empty;

        public CounterSample(long rawValue, long baseValue, long counterFrequency, long systemFrequency, long timeStamp, long timeStamp100NSec, PerformanceCounterType counterType)
        {
            RawValue = rawValue;
            BaseValue = baseValue;
            TimeStamp = timeStamp;
            CounterFrequency = counterFrequency;
            CounterType = counterType;
            TimeStamp100NSec = timeStamp100NSec;
            SystemFrequency = systemFrequency;
            CounterTimeStamp = 0L;
        }

        public CounterSample(long rawValue, long baseValue, long counterFrequency, long systemFrequency, long timeStamp, long timeStamp100NSec, PerformanceCounterType counterType, long counterTimeStamp)
        {
            RawValue = rawValue;
            BaseValue = baseValue;
            TimeStamp = timeStamp;
            CounterFrequency = counterFrequency;
            CounterType = counterType;
            TimeStamp100NSec = timeStamp100NSec;
            SystemFrequency = systemFrequency;
            CounterTimeStamp = counterTimeStamp;
        }

        public long RawValue { get; }

        internal ulong UnsignedRawValue =>
            (ulong)RawValue;

        public long BaseValue { get; }

        public long SystemFrequency { get; }

        public long CounterFrequency { get; }

        public long CounterTimeStamp { get; }

        public long TimeStamp { get; }

        public long TimeStamp100NSec { get; }

        public PerformanceCounterType CounterType { get; }

        public static float Calculate(CounterSample counterSample) =>
            CounterSampleCalculator.ComputeCounterValue(counterSample);

        public static float Calculate(CounterSample counterSample, CounterSample nextCounterSample) =>
            CounterSampleCalculator.ComputeCounterValue(counterSample, nextCounterSample);

        public override bool Equals(object o) =>
            o is CounterSample && Equals((CounterSample)o);

        public bool Equals(CounterSample sample) =>
            RawValue == sample.RawValue && BaseValue == sample.BaseValue && TimeStamp == sample.TimeStamp && CounterFrequency == sample.CounterFrequency && CounterType == sample.CounterType && TimeStamp100NSec == sample.TimeStamp100NSec && SystemFrequency == sample.SystemFrequency && CounterTimeStamp == sample.CounterTimeStamp;

        public override int GetHashCode() =>
            RawValue.GetHashCode();

        public static bool operator ==(CounterSample a, CounterSample b) =>
            a.Equals(b);

        public static bool operator !=(CounterSample a, CounterSample b) =>
            !a.Equals(b);

        static CounterSample()
        {
            Empty = new CounterSample(0L, 0L, 0L, 0L, 0L, 0L, PerformanceCounterType.NumberOfItems32);
        }
    }
}