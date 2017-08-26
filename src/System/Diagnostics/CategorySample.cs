using System.Collections.Generic;

namespace System.Diagnostics
{
    internal abstract class CategorySample
    {
        protected internal abstract long CounterFrequency { get; set; }
        protected internal abstract long CounterTimeStamp { get; set; }
        protected internal abstract long SystemFrequency { get; set; }
        protected internal abstract long TimeStamp { get; set; }
        protected internal abstract long TimeStamp100NSec { get; set; }
        protected internal abstract Dictionary<int, CounterDefinitionSample> CounterTable { get; set; }
        protected internal abstract Dictionary<string, int> InstanceNameTable { get; set; }
        protected internal abstract bool IsMultiInstance { get; set; }

        internal abstract string[] GetInstanceNamesFromIndex(int categoryIndex);
        internal abstract CounterDefinitionSample GetCounterDefinitionSample(string counter);
        internal abstract Dictionary<string, Dictionary<string, InstanceData>> ReadCategory();
    }
}