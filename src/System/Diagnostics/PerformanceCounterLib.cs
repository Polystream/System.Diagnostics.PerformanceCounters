using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using PerformanceCounters;

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

    internal abstract class CustomPerformanceCounter
    {
        internal abstract long Value { get; set; }
        internal abstract long Decrement();
        internal abstract long IncrementBy(long value);
        internal abstract long Increment();
        internal abstract void RemoveInstance(string instanceName, PerformanceCounterInstanceLifetime instanceLifetime);
    }

    internal class PerformanceCounterLib
    {
        internal const string SingleInstanceName = "systemdiagnosticsperfcounterlibsingleinstance";
        internal const string DefaultFileMappingName = "netfxcustomperfcounters.1.0";

        internal static bool CategoryExists(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CategoryExists(category);

            throw new NotSupportedException();
        }

        internal static void CloseAllLibraries()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.CloseAllLibraries();
            else
                throw new NotSupportedException();
        }

        internal static bool CounterExists(string category, string counter)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CounterExists(category, counter);

            throw new NotSupportedException();
        }

        internal static string[] GetCategories()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategories();

            throw new NotSupportedException();
        }

        internal static string GetCategoryHelp(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategoryHelp(category);

            throw new NotSupportedException();
        }

        internal static CategorySample GetCategorySample(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategorySample(category);

            throw new NotSupportedException();
        }

        internal static string[] GetCounters(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCounters(category);

            throw new NotSupportedException();
        }

        internal static string GetCounterHelp(string category, string counter)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCounterHelp(category, counter);

            throw new NotSupportedException();
        }

        internal static bool IsCustomCategory(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.IsCustomCategory(category);

            throw new NotSupportedException();
        }

        internal static bool IsBaseCounter(int type)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.IsBaseCounter(type);

            throw new NotSupportedException();
        }

        internal static PerformanceCounterCategoryType GetCategoryType(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategoryType(category);

            throw new NotSupportedException();
        }

        internal static void RegisterCategory(string categoryName, PerformanceCounterCategoryType categoryType, string categoryHelp, List<CounterCreationData> creationData)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.RegisterCategory(categoryName, categoryType, categoryHelp, creationData);
            else
                throw new NotSupportedException();
        }

        internal static void UnregisterCategory(string categoryName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.UnregisterCategory(categoryName);
            else
                throw new NotSupportedException();
        }

        internal static void CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName)
            => CreateCustomPerformanceCounter(categoryName, counterName, instanceName, PerformanceCounterInstanceLifetime.Global);

        internal static CustomPerformanceCounter CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName, PerformanceCounterInstanceLifetime lifetime)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CreateCustomPerformanceCounter(categoryName, counterName, instanceName, lifetime);

            throw new NotSupportedException();
        }
        internal static void RemoveAllCustomInstances(string categoryName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.RemoveAllCustomInstances(categoryName);
            else
                throw new NotSupportedException();
        }
    }
}