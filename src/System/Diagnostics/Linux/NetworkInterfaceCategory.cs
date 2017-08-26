using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace System.Diagnostics.Linux
{
    internal class NetworkInterfaceCategory : CategoryBase
    {
        private const string FilePath = "/proc/net/dev";
        private static readonly Regex ParsingRegex = new Regex(@"(?<instanceName>[a-z0-9]+):\s*(?<c1>[0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly string[] CounterNames =
        {"Receive Bytes", "Receive Packets","Receive Errors","Receive Drop","Receive Fifo","Receive Frame","Receive Compressed","Receive Multicast",
            "Transmit Bytes", "Transmit Packets","Transmit Errors","Transmit Drop","Transmit Fifo","Transmit Frame","Transmit Compressed","Transmit Multicast"};

        public override string CategoryName => "Network Interface";
        public override PerformanceCounterCategoryType Type => PerformanceCounterCategoryType.MultiInstance;

        public override CategorySample GetSample()
        {
            var instanceNameTable = new Dictionary<string, int>();
            var instanceValues = new Dictionary<string, List<long>>();

            using (var fileStream = File.OpenRead(FilePath))
            using (var reader = new StreamReader(fileStream))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var match = ParsingRegex.Match(line);
                    if (match.Success)
                    {
                        var instanceName = match.Groups["instanceName"].Value;
                        instanceNameTable.Add(instanceName, instanceNameTable.Count);

                        if (!instanceValues.TryGetValue(CounterNames[0], out var list))
                        {
                            list = new List<long>();
                            instanceValues.Add(CounterNames[0], list);
                        }
                        list.Add(long.Parse(match.Groups["c1"].Value));
                    }
                }
            }

            var sample = new CategorySample(instanceNameTable, true, CounterNames);

            for (var i = 0; i < CounterNames.Length; i++)
            {
                var definitionSample = new CounterDefinitionSample(0, i, sample, instanceNameTable.Count);
                if (instanceValues.TryGetValue(CounterNames[i], out var list))
                {
                    for (var j = 0; j < list.Count; j++)
                        definitionSample.SetInstanceValue(j, list[j]);
                }

                sample.CounterTable.Add(i, definitionSample);
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
