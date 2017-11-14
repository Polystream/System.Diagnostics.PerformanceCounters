﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Diagnostics.Linux
{
    internal class NetworkInterfaceCategory : CategoryBase
    {
        const string FilePath = "/proc/net/dev";
        static readonly Regex ParsingRegex = new Regex(@"(?<instanceName>[a-z0-9]+):(\s*(?<counter>[0-9]+))+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly string[] CounterNames =
        {"Receive Bytes", "Receive Packets","Receive Errors","Receive Drop","Receive Fifo","Receive Frame","Receive Compressed","Receive Multicast",
            "Transmit Bytes", "Transmit Packets","Transmit Errors","Transmit Drop","Transmit Fifo","Transmit Frame","Transmit Compressed","Transmit Multicast"};

        public override string CategoryName => "Network Interface";
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

                            instanceValues.Add(instanceName, long.Parse(counterCaptuers[i].Value));
                        }
                    }
                }
            }

            return CreateCategorySample(counterValues);
        }

        //Counter <instance, value>
        static CategorySample CreateCategorySample(Dictionary<string, Dictionary<string, long>> counterValues)
        {
            var instanceCount = 0;
            var instanceNameTable = counterValues.Values.SelectMany(x=>x.Keys).Distinct().ToDictionary(x => x, x => instanceCount++);

            instanceNameTable.Add("_Total", instanceCount++);

            var sample = new CategorySample(instanceNameTable, true, CounterNames);

            foreach (var counterValue in counterValues)
            {
                var definitionSample = new CounterDefinitionSample((int)PerformanceCounterType.NumberOfItems64, Array.IndexOf(CounterNames, counterValue.Key), sample, instanceCount);

                foreach (var instanceValue in counterValue.Value)
                    definitionSample.SetInstanceValue(instanceNameTable[instanceValue.Key], instanceValue.Value);

                definitionSample.SetInstanceValue(instanceNameTable["_Total"], counterValue.Value.Sum(x => x.Value));

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
