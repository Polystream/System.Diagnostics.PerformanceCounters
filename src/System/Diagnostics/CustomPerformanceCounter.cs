namespace System.Diagnostics
{
    internal abstract class CustomPerformanceCounter
    {
        internal abstract long Value { get; set; }
        internal abstract long Decrement();
        internal abstract long IncrementBy(long value);
        internal abstract long Increment();
        internal abstract void RemoveInstance(string instanceName, PerformanceCounterInstanceLifetime instanceLifetime);
    }
}