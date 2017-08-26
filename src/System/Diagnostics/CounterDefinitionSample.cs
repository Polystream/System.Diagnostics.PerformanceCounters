using System.Collections.Generic;

namespace System.Diagnostics
{
    internal abstract class CounterDefinitionSample
    {
        protected internal abstract int CounterType { get; set; }
        protected internal abstract int NameIndex { get; set; }
        protected internal abstract CounterDefinitionSample BaseCounterDefinitionSample { get; set; }

        internal abstract CounterSample GetInstanceValue(string instanceName);
        internal abstract Dictionary<string, InstanceData> ReadInstanceData(string counterName);
        internal abstract CounterSample GetSingleValue();
    }
}