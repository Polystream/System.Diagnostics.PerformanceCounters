using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Diagnostics.Linux
{
    internal class ProcessorCategory : CategoryBase
    {
        const string FilePath = "/proc/stat";
        static readonly Regex ParsingRegex = new Regex(@"cpu(?<instanceName>[0-9]*)(\s*(?<counter>[0-9]+))+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly string[] CounterNames = {"User", "User Nice", "System", "Idle", "IO Wait", "IRQ", "Soft IRQ", "Steal", "Guest", "Guest Nice"};

        public override string CategoryName => "Processor";
        public override PerformanceCounterCategoryType Type => PerformanceCounterCategoryType.MultiInstance;

        public override CategorySample GetSample()
        {
            var counterValues = new Dictionary<string, Dictionary<string, long>>();

            using (var fileStream = File.OpenRead(FilePath))
            using (var reader = new StreamReader(fileStream))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var match = ParsingRegex.Match(line);
                    if (match.Success)
                    {
                        var counterCaptuers = match.Groups["counter"].Captures;
                        for (var i = 0; i < CounterNames.Length; i++)
                        {

                            if (!counterValues.TryGetValue(CounterNames[i], out var instanceValues))
                            {
                                instanceValues = new Dictionary<string, long>();
                                counterValues.Add(CounterNames[i], instanceValues);
                            }

                            var instanceName = match.Groups["instanceName"].Value;
                            if (string.IsNullOrEmpty(instanceName))
                                instanceName = "_Total";

                            instanceValues.Add(instanceName, long.Parse(counterCaptuers[i].Value));
                        }
                    }
                }
            }

            return CreateCategorySample(counterValues);
        }

        static CategorySample CreateCategorySample(Dictionary<string, Dictionary<string, long>> counterValues)
        {
            var instanceCount = 0;
            var instanceNameTable = counterValues.Values.SelectMany(x => x.Keys).Distinct().ToDictionary(x => x, x => instanceCount++);

            var sample = new CategorySample(instanceNameTable, true, CounterNames);

            foreach (var counterValue in counterValues)
            {
                var definitionSample = new CounterDefinitionSample((int)PerformanceCounterType.NumberOfItems64, Array.IndexOf(CounterNames, counterValue.Key), sample, instanceCount);

                foreach (var instanceValue in counterValue.Value)
                    definitionSample.SetInstanceValue(instanceNameTable[instanceValue.Key], instanceValue.Value);

                sample.CounterTable.Add(definitionSample.NameIndex, definitionSample);
            }

            return sample;
        }

        public override string[] GetCounters()
        {
            var counters = new string[CounterNames.Length];
            CounterNames.CopyTo(counters, 0);
            return counters;
        }

        public override bool CounterExists(string counter)
        {
            return CounterNames.Contains(counter, StringComparer.OrdinalIgnoreCase);
        }
    }
}
