using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Diagnostics.Linux
{
    internal class MemoryCategory : CategoryBase
    {
        const string FilePath = "/proc/meminfo";
        static readonly Regex ParsingRegex = new Regex(@"(?<counterName>.+):\s*(?<counterValue>[0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Dictionary<string, string> CounterMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MemTotal", "Total KBytes" },
            { "MemFree", "Free KBytes" },
            { "MemAvailable", "Available KBytes" },
            { "Buffers", "Buffers KBytes" },
            { "Cached", "Cached KBytes" },
            { "SwapCached", "Swap Cached KBytes" },
            { "Active", "Active KBytes" },
            { "Inactive", "Inactive KBytes" },
            { "Active(anon)", "Active (anon) KBytes" },
            { "Inactive(anon)", "Inactive (anon) KBytes" },
            { "Active(file)", "Active (file) KBytes" },
            { "Inactive(file)", "Inactive (file) KBytes" },
            { "Unevictable", "Unevictable KBytes" },
            { "Mlocked", "Mlocked KBytes" },
            { "HighTotal", "High Total KBytes" },
            { "HighFree", "High Free KBytes" },
            { "LowTotal", "Low Total KBytes" },
            { "LowFree", "Low Free KBytes" },
            { "MmapCopy", "Mmap Copy KBytes" },
            { "SwapTotal", "Swap Total KBytes" },
            { "SwapFree", "Swap Free KBytes" },
            { "Dirty", "Dirty KBytes" },
            { "Writeback", "Writeback KBytes" },
            { "AnonPages", "AnonPages KBytes" },
            { "Mapped", "Mapped KBytes" },
            { "Shmem", "Shmem KBytes" },
            { "Slab", "Slab KBytes" },
            { "SReclaimable", "SReclaimable KBytes" },
            { "SUnreclaim", "SUnreclaim KBytes" },
            { "KernelStack", "Kernel Stack KBytes" },
            { "PageTables", "Page Tables KBytes" },
            { "Quicklists", "Quicklists KBytes" },
            { "NFS_Unstable", "NFS Unstable KBytes" },
            { "Bounce", "Bounce KBytes" },
            { "WritebackTmp", "Writeback Tmp KBytes" },
            { "CommitLimit", "Commit Limit KBytes" },
            { "Committed_AS", "Committed AS KBytes" },
            { "VmallocTotal", "Vmalloc Total KBytes" },
            { "VmallocUsed", "Vmalloc Used KBytes" },
            { "VmallocChunk", "Vmalloc Chunk KBytes" },
            { "HardwareCorrupted", "Hardware Corrupted KBytes" },
            { "AnonHugePages", "Anon Huge Pages KBytes" },
            { "ShmemHugePages", "Shmem Huge Pages KBytes" },
            { "ShmemPmdMapped", "Shmem Pmd Mapped KBytes" },
            { "CmaTotal", "Cma Total KBytes" },
            { "CmaFree", "Cma Free KBytes" },
            { "HugePages_Total", "Huge Pages Total" },
            { "HugePages_Free", "Huge Pages Free" },
            { "HugePages_Surp", "Huge Pages Surp" },
            { "Hugepagesize", "Huge Page Size KBytes" },
            { "DirectMap4k", "Direct Map 4k KBytes" },
            { "DirectMap4M", "Direct Map 4M KBytes" },
            { "DirectMap2M", "Direct Map 2M KBytes" },
            { "DirectMap1G", "Direct Map 1G KBytes" },
        };

        public override string CategoryName => "Memory";
        public override PerformanceCounterCategoryType Type => PerformanceCounterCategoryType.SingleInstance;

        public override CategorySample GetSample()
        {
            var counterValues = GetCounterValues();

            return CreateCategorySample(counterValues);
        }
        static Dictionary<string, long> GetCounterValues()
        {
            var counterValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            using (var fileStream = File.OpenRead(FilePath))
            using (var reader = new StreamReader(fileStream))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var match = ParsingRegex.Match(line);
                    if (match.Success)
                    {
                        var counterName = match.Groups["counterName"].Value;
                        if (CounterMapping.TryGetValue(counterName, out var name))
                        {
                            var value = long.Parse(match.Groups["counterValue"].Value);
                            counterValues.Add(name, value);
                        }
                    }
                }
            }

            return counterValues;
        }

        static CategorySample CreateCategorySample(Dictionary<string, long> counterValues)
        {
            var instanceCount = 1;
            var counterNames = counterValues.Keys.ToArray();

            var sample = new CategorySample(new Dictionary<string, int>{{Diagnostics.PerformanceCounterLib.SingleInstanceName, 1}}, false, counterNames);

            foreach (var counterValue in counterValues)
            {
                var definitionSample = new CounterDefinitionSample((int)PerformanceCounterType.NumberOfItems64, Array.IndexOf(counterNames, counterValue.Key), sample, instanceCount);

                definitionSample.SetInstanceValue(0, counterValue.Value);

                sample.CounterTable.Add(definitionSample.NameIndex, definitionSample);
            }

            return sample;
        }

        public override string[] GetCounters()
        {
            return GetCounterValues().Keys.ToArray();
        }

        public override bool CounterExists(string counter)
        {
            return GetCounterValues().ContainsKey(counter);
        }
    }
}
