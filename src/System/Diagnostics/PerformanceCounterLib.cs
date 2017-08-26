using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace System.Diagnostics
{
    internal class PerformanceCounterLib
    {
        internal const string SingleInstanceName = "systemdiagnosticsperfcounterlibsingleinstance";
        internal const string DefaultFileMappingName = "netfxcustomperfcounters.1.0";

        internal static bool CategoryExists(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CategoryExists(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.CategoryExists(category);

            throw new NotSupportedException();
        }

        internal static void CloseAllLibraries()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.CloseAllLibraries();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Linux.PerformanceCounterLib.CloseAllLibraries();
            else
                throw new NotSupportedException();
        }

        internal static bool CounterExists(string category, string counter)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CounterExists(category, counter);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.CounterExists(category, counter);

            throw new NotSupportedException();
        }

        internal static string[] GetCategories()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategories();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCategories();

            throw new NotSupportedException();
        }

        internal static string GetCategoryHelp(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategoryHelp(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCategoryHelp(category);

            throw new NotSupportedException();
        }

        internal static CategorySample GetCategorySample(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategorySample(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCategorySample(category);

            throw new NotSupportedException();
        }

        internal static string[] GetCounters(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCounters(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCounters(category);

            throw new NotSupportedException();
        }

        internal static string GetCounterHelp(string category, string counter)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCounterHelp(category, counter);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCounterHelp(category, counter);

            throw new NotSupportedException();
        }

        internal static bool IsCustomCategory(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.IsCustomCategory(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.IsCustomCategory(category);

            throw new NotSupportedException();
        }

        internal static bool IsBaseCounter(int type)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.IsBaseCounter(type);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.IsBaseCounter(type);

            throw new NotSupportedException();
        }

        internal static PerformanceCounterCategoryType GetCategoryType(string category)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.GetCategoryType(category);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.GetCategoryType(category);

            throw new NotSupportedException();
        }

        internal static void RegisterCategory(string categoryName, PerformanceCounterCategoryType categoryType, string categoryHelp, List<CounterCreationData> creationData)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.RegisterCategory(categoryName, categoryType, categoryHelp, creationData);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Linux.PerformanceCounterLib.RegisterCategory(categoryName, categoryType, categoryHelp, creationData);
            else
                throw new NotSupportedException();
        }

        internal static void UnregisterCategory(string categoryName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.UnregisterCategory(categoryName);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Linux.PerformanceCounterLib.UnregisterCategory(categoryName);
            else
                throw new NotSupportedException();
        }

        internal static void CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName)
            => CreateCustomPerformanceCounter(categoryName, counterName, instanceName, PerformanceCounterInstanceLifetime.Global);

        internal static CustomPerformanceCounter CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName, PerformanceCounterInstanceLifetime lifetime)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows.PerformanceCounterLib.CreateCustomPerformanceCounter(categoryName, counterName, instanceName, lifetime);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux.PerformanceCounterLib.CreateCustomPerformanceCounter(categoryName, counterName, instanceName, lifetime);

            throw new NotSupportedException();
        }
        internal static void RemoveAllCustomInstances(string categoryName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Windows.PerformanceCounterLib.RemoveAllCustomInstances(categoryName);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Linux.PerformanceCounterLib.RemoveAllCustomInstances(categoryName);
            else
                throw new NotSupportedException();
        }
    }
}