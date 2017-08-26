using System.Collections.Generic;
using PerformanceCounters;

namespace System.Diagnostics.Linux
{
    internal class CategorySample : Diagnostics.CategorySample
    {
        protected internal override long CounterFrequency { get; set; }
        protected internal override long CounterTimeStamp { get; set; }
        protected internal override long SystemFrequency { get; set; }
        protected internal override long TimeStamp { get; set; }
        protected internal override long TimeStamp100NSec { get; set; }
        protected internal override Dictionary<int, Diagnostics.CounterDefinitionSample> CounterTable { get; set; }
        protected internal override Dictionary<string, int> InstanceNameTable { get; set; }
        protected internal override bool IsMultiInstance { get; set; }

        readonly string[] _counterNames;

        internal CategorySample(Dictionary<string, int> instanceNameTable, bool multiInstance, string[] counterNames)
        {
            InstanceNameTable = instanceNameTable;
            IsMultiInstance = multiInstance;
            _counterNames = counterNames;

            CounterTable = new Dictionary<int, Diagnostics.CounterDefinitionSample>();
        }

        internal override string[] GetInstanceNamesFromIndex(int categoryIndex)
        {
            throw new NotImplementedException();
        }

        internal override Diagnostics.CounterDefinitionSample GetCounterDefinitionSample(string counter)
        {
            for (var index = 0; index < _counterNames.Length; ++index)
            {
                var counterName = _counterNames[index];
                if (counterName != null)
                {
                    if (string.Compare(counterName, counter, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        var sample = CounterTable[index];
                        if (sample == null)
                        {
                            //This is a base counter and has not been added to the table
                            foreach (var multiSample in CounterTable.Values)
                            {
                                if (multiSample.BaseCounterDefinitionSample != null &&
                                    multiSample.BaseCounterDefinitionSample.NameIndex == index)
                                    return multiSample.BaseCounterDefinitionSample;
                            }

                            throw new InvalidOperationException(SR.GetString("The Counter layout for the Category specified is invalid, a counter of the type:  AverageCount64, AverageTimer32, CounterMultiTimer, CounterMultiTimerInverse, CounterMultiTimer100Ns, CounterMultiTimer100NsInverse, RawFraction, or SampleFraction has to be immediately followed by any of the base counter types: AverageBase, CounterMultiBase, RawBase or SampleBase."));
                        }

                        return sample;
                    }
                }
            }
            throw new InvalidOperationException(SR.GetString("Counter '{0}' does not exist in the specified Category.", counter));
        }

        internal override Dictionary<string, Dictionary<string, InstanceData>> ReadCategory()
        {
            var data = new Dictionary<string, Dictionary<string, InstanceData>>();

            for (var i = 0; i < _counterNames.Length; i++)
            {
                var name = _counterNames[i];
                if (!string.IsNullOrEmpty(name))
                {
                    var sample = CounterTable[i];
                    if (sample != null)
                        data.Add(name, sample.ReadInstanceData(name));
                }
            }

            return data;
        }
    }
}