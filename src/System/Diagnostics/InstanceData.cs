namespace System.Diagnostics
{
    public class InstanceData
    {
        public InstanceData(string instanceName, CounterSample sample)
        {
            InstanceName = instanceName;
            Sample = sample;
        }
        
        public string InstanceName { get; }

        public long RawValue =>
            Sample.RawValue;

        public CounterSample Sample { get; }
    }
}