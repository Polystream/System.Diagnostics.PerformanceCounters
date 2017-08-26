using System.Collections.Generic;

namespace System.Diagnostics.Linux
{
    internal class CounterDefinitionSample : Diagnostics.CounterDefinitionSample
    {
        protected internal override int CounterType { get; set; }
        protected internal override int NameIndex { get; set; }
        protected internal override Diagnostics.CounterDefinitionSample BaseCounterDefinitionSample { get; set; }

        CategorySample _categorySample;
        long[] _instanceValues;

        internal CounterDefinitionSample(int counterType, int nameIndex, CategorySample categorySample, int instanceCount)
        {
            NameIndex = nameIndex;
            CounterType = counterType;
            _instanceValues = instanceCount == -1 ? new long[1] : new long[instanceCount];

            _categorySample = categorySample;
        }
        internal override CounterSample GetInstanceValue(string instanceName)
        {
            if (!_categorySample.InstanceNameTable.ContainsKey(instanceName))
            {
                if (!_categorySample.InstanceNameTable.ContainsKey(instanceName))
                    throw new InvalidOperationException(string.Format("Instance '{0}' does not exist in the specified Category.", instanceName));
            }

            var index = _categorySample.InstanceNameTable[instanceName];
            var rawValue = _instanceValues[index];

            return new CounterSample(rawValue,
                                     0,
                                     _categorySample.CounterFrequency,
                                     _categorySample.SystemFrequency,
                                     _categorySample.TimeStamp,
                                     _categorySample.TimeStamp100NSec,
                                     (PerformanceCounterType)CounterType,
                                     _categorySample.CounterTimeStamp);
        }

        internal override Dictionary<string, InstanceData> ReadInstanceData(string counterName)
        {
            var data = new Dictionary<string, InstanceData>();

            var keys = new string[_categorySample.InstanceNameTable.Count];
            _categorySample.InstanceNameTable.Keys.CopyTo(keys, 0);
            var indexes = new int[_categorySample.InstanceNameTable.Count];
            _categorySample.InstanceNameTable.Values.CopyTo(indexes, 0);
            for (var index = 0; index < keys.Length; ++index)
            {
                var sample = new CounterSample(_instanceValues[indexes[index]],
                                               0,
                                               _categorySample.CounterFrequency,
                                               _categorySample.SystemFrequency,
                                               _categorySample.TimeStamp,
                                               _categorySample.TimeStamp100NSec,
                                               (PerformanceCounterType)CounterType,
                                               _categorySample.CounterTimeStamp);

                data.Add(keys[index], new InstanceData(keys[index], sample));
            }

            return data;
        }

        internal override CounterSample GetSingleValue()
        {
            var rawValue = _instanceValues[0];

            return new CounterSample(rawValue,
                                     0,
                                     _categorySample.CounterFrequency,
                                     _categorySample.SystemFrequency,
                                     _categorySample.TimeStamp,
                                     _categorySample.TimeStamp100NSec,
                                     (PerformanceCounterType)CounterType,
                                     _categorySample.CounterTimeStamp);
        }

        internal void SetInstanceValue(int index, long value)
        {
            _instanceValues[index] = value;
        }
    }
}