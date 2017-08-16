using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;
using PerformanceCounters;

namespace System.Diagnostics
{
    public sealed class PerformanceCounterCategory
    {
        internal const int MaxCategoryNameLength = 80;
        internal const int MaxCounterNameLength = 32767;
        internal const int MaxHelpLength = 32767;
        const string PerfMutexName = "netfxperf.1.0";
        string _categoryHelp;
        string _categoryName;

        public PerformanceCounterCategory()
        {
        }

        public PerformanceCounterCategory(string categoryName)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.GetString("Invalid value '{1}' for parameter '{0}'.", "categoryName", categoryName));

            _categoryName = categoryName;
        }

        public string CategoryName
        {
            get => _categoryName;

            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value.Length == 0)
                    throw new ArgumentException(SR.GetString("Invalid value {1} for property {0}.", "CategoryName", value));

                // there lock prevents a ---- between setting CategoryName and MachineName, since this permission 
                // checks depend on both pieces of info. 
                lock (this)
                    _categoryName = value;
            }
        }

        public string CategoryHelp
        {
            get
            {
                if (_categoryName == null)
                    throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

                return _categoryHelp ?? (_categoryHelp = PerformanceCounterLib.GetCategoryHelp(_categoryName));
            }
        }

        public PerformanceCounterCategoryType CategoryType
        {
            get
            {
                var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName);

                // If we get MultiInstance, we can be confident it is correct.  If it is single instance, though
                // we need to check if is a custom category and if the IsMultiInstance value is set in the registry.
                // If not we return Unknown
                if (categorySample.IsMultiInstance)
                    return PerformanceCounterCategoryType.MultiInstance;

                return PerformanceCounterLib.IsCustomCategory(_categoryName) ? PerformanceCounterLib.GetCategoryType(_categoryName) : PerformanceCounterCategoryType.SingleInstance;
            }
        }

        public bool CounterExists(string counterName)
        {
            if (counterName == null)
                throw new ArgumentNullException(nameof(counterName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

            return PerformanceCounterLib.CounterExists(_categoryName, counterName);
        }

        public static bool CounterExists(string counterName, string categoryName)
        {
            return CounterExists(counterName, categoryName, ".");
        }

        public static bool CounterExists(string counterName, string categoryName, string machineName)
        {
            if (counterName == null)
                throw new ArgumentNullException(nameof(counterName));

            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.GetString("Invalid value '{1}' for parameter '{0}'.", "categoryName", categoryName));

            return PerformanceCounterLib.CounterExists(categoryName, counterName);
        }

        public static PerformanceCounterCategory Create(string categoryName, string categoryHelp, PerformanceCounterCategoryType categoryType, string counterName, string counterHelp)
        {
            var customData = new CounterCreationData(counterName, counterHelp, PerformanceCounterType.NumberOfItems32);
            return Create(categoryName, categoryHelp, categoryType, new List<CounterCreationData>(new[] { customData }));
        }

        public static PerformanceCounterCategory Create(string categoryName, string categoryHelp, PerformanceCounterCategoryType categoryType, List<CounterCreationData> counterData)
        {
            if (categoryType < PerformanceCounterCategoryType.Unknown || categoryType > PerformanceCounterCategoryType.MultiInstance)
                throw new ArgumentOutOfRangeException(nameof(categoryType));
            if (counterData == null)
                throw new ArgumentNullException(nameof(counterData));

            CheckValidCategory(categoryName);
            if (categoryHelp != null)
            {
                // null categoryHelp is a valid option - it gets set to "Help Not Available" later on.
                CheckValidHelp(categoryHelp);
            }

            Mutex mutex = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutex(PerfMutexName, ref mutex);
                if (PerformanceCounterLib.IsCustomCategory(categoryName) || PerformanceCounterLib.CategoryExists(categoryName))
                    throw new InvalidOperationException(SR.GetString("Cannot create Performance Category '{0}' because it already exists.", categoryName));

                CheckValidCounterLayout(counterData);
                PerformanceCounterLib.RegisterCategory(categoryName, categoryType, categoryHelp, counterData);
                return new PerformanceCounterCategory(categoryName);
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        // there is an idential copy of CheckValidCategory in PerformnaceCounterInstaller
        internal static void CheckValidCategory(string categoryName)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (!CheckValidId(categoryName, MaxCategoryNameLength))
                throw new ArgumentException(SR.GetString("Invalid category name. Its length must be in the range between '{0}' and '{1}'. Double quotes, control characters and leading or trailing spaces are not allowed.", 1, MaxCategoryNameLength));

            // 1026 chars is the size of the buffer used in perfcounter.dll to get this name.  
            // If the categoryname plus prefix is too long, we won't be able to read the category properly. 
            if (categoryName.Length > 1024 - SharedPerformanceCounter.DefaultFileMappingName.Length)
                throw new ArgumentException(SR.GetString("Category names must be 1024 characters or less."));
        }

        internal static void CheckValidCounter(string counterName)
        {
            if (counterName == null)
                throw new ArgumentNullException(nameof(counterName));

            if (!CheckValidId(counterName, MaxCounterNameLength))
                throw new ArgumentException(SR.GetString("Invalid counter name. Its length must be in the range between '{0}' and '{1}'. Double quotes, control characters and leading or trailing spaces are not allowed.", 1, MaxCounterNameLength));
        }

        // there is an idential copy of CheckValidId in PerformnaceCounterInstaller
        internal static bool CheckValidId(string id, int maxLength)
        {
            if (id.Length == 0 || id.Length > maxLength)
                return false;

            for (var index = 0; index < id.Length; ++index)
            {
                var current = id[index];

                if ((index == 0 || index == id.Length - 1) && current == ' ')
                    return false;

                if (current == '\"')
                    return false;

                if (char.IsControl(current))
                    return false;
            }

            return true;
        }

        internal static void CheckValidHelp(string help)
        {
            if (help == null)
                throw new ArgumentNullException(nameof(help));
            if (help.Length > MaxHelpLength)
                throw new ArgumentException(SR.GetString("Invalid help string. Its length must be in the range between '{0}' and '{1}'.", 0, MaxHelpLength));
        }

        internal static void CheckValidCounterLayout(List<CounterCreationData> counterData)
        {
            // Ensure that there are no duplicate counter names being created
            var h = new HashSet<string>();
            for (var i = 0; i < counterData.Count; i++)
            {
                if (counterData[i].CounterName == null || counterData[i].CounterName.Length == 0)
                    throw new ArgumentException(SR.GetString("Invalid empty or null string for counter name."));

                var currentSampleType = (int)counterData[i].CounterType;
                if (currentSampleType == NativeMethods.PERF_AVERAGE_BULK ||
                    currentSampleType == NativeMethods.PERF_100NSEC_MULTI_TIMER ||
                    currentSampleType == NativeMethods.PERF_100NSEC_MULTI_TIMER_INV ||
                    currentSampleType == NativeMethods.PERF_COUNTER_MULTI_TIMER ||
                    currentSampleType == NativeMethods.PERF_COUNTER_MULTI_TIMER_INV ||
                    currentSampleType == NativeMethods.PERF_RAW_FRACTION ||
                    currentSampleType == NativeMethods.PERF_SAMPLE_FRACTION ||
                    currentSampleType == NativeMethods.PERF_AVERAGE_TIMER)
                {
                    if (counterData.Count <= i + 1)
                        throw new InvalidOperationException(SR.GetString("The Counter layout for the Category specified is invalid, a counter of the type:  AverageCount64, AverageTimer32, CounterMultiTimer, CounterMultiTimerInverse, CounterMultiTimer100Ns, CounterMultiTimer100NsInverse, RawFraction, or SampleFraction has to be immediately followed by any of the base counter types: AverageBase, CounterMultiBase, RawBase or SampleBase."));

                    currentSampleType = (int)counterData[i + 1].CounterType;

                    if (!PerformanceCounterLib.IsBaseCounter(currentSampleType))
                        throw new InvalidOperationException(SR.GetString("The Counter layout for the Category specified is invalid, a counter of the type:  AverageCount64, AverageTimer32, CounterMultiTimer, CounterMultiTimerInverse, CounterMultiTimer100Ns, CounterMultiTimer100NsInverse, RawFraction, or SampleFraction has to be immediately followed by any of the base counter types: AverageBase, CounterMultiBase, RawBase or SampleBase."));
                }
                else if (PerformanceCounterLib.IsBaseCounter(currentSampleType))
                {
                    if (i == 0)
                        throw new InvalidOperationException(SR.GetString("The Counter layout for the Category specified is invalid, a counter of the type:  AverageCount64, AverageTimer32, CounterMultiTimer, CounterMultiTimerInverse, CounterMultiTimer100Ns, CounterMultiTimer100NsInverse, RawFraction, or SampleFraction has to be immediately followed by any of the base counter types: AverageBase, CounterMultiBase, RawBase or SampleBase."));

                    currentSampleType = (int)counterData[i - 1].CounterType;

                    if (
                        currentSampleType != NativeMethods.PERF_AVERAGE_BULK &&
                        currentSampleType != NativeMethods.PERF_100NSEC_MULTI_TIMER &&
                        currentSampleType != NativeMethods.PERF_100NSEC_MULTI_TIMER_INV &&
                        currentSampleType != NativeMethods.PERF_COUNTER_MULTI_TIMER &&
                        currentSampleType != NativeMethods.PERF_COUNTER_MULTI_TIMER_INV &&
                        currentSampleType != NativeMethods.PERF_RAW_FRACTION &&
                        currentSampleType != NativeMethods.PERF_SAMPLE_FRACTION &&
                        currentSampleType != NativeMethods.PERF_AVERAGE_TIMER)
                        throw new InvalidOperationException(SR.GetString("The Counter layout for the Category specified is invalid, a counter of the type:  AverageCount64, AverageTimer32, CounterMultiTimer, CounterMultiTimerInverse, CounterMultiTimer100Ns, CounterMultiTimer100NsInverse, RawFraction, or SampleFraction has to be immediately followed by any of the base counter types: AverageBase, CounterMultiBase, RawBase or SampleBase."));
                }

                if (h.Contains(counterData[i].CounterName))
                    throw new ArgumentException(SR.GetString("Cannot create Performance Category with counter name {0} because the name is a duplicate.", counterData[i].CounterName));

                h.Add(counterData[i].CounterName);

                // Ensure that all counter help strings aren't null or empty
                if (counterData[i].CounterHelp == null || counterData[i].CounterHelp.Length == 0)
                    counterData[i].CounterHelp = counterData[i].CounterName;
            }
        }

        public static void Delete(string categoryName)
        {
            CheckValidCategory(categoryName);

            categoryName = categoryName.ToLower();

            Mutex mutex = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutex(PerfMutexName, ref mutex);
                if (!PerformanceCounterLib.IsCustomCategory(categoryName))
                    throw new InvalidOperationException(SR.GetString("Cannot delete Performance Category because this category is not registered or is a system category."));

                SharedPerformanceCounter.RemoveAllInstances(categoryName);

                PerformanceCounterLib.UnregisterCategory(categoryName);
                PerformanceCounterLib.CloseAllLibraries();
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        public static bool Exists(string categoryName)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.GetString("Invalid value '{1}' for parameter '{0}'.", "categoryName", categoryName));

            if (PerformanceCounterLib.IsCustomCategory(categoryName))
                return true;

            return PerformanceCounterLib.CategoryExists(categoryName);
        }

        internal static string[] GetCounterInstances(string categoryName)
        {
            var categorySample = PerformanceCounterLib.GetCategorySample(categoryName);
            if (categorySample.InstanceNameTable.Count == 0)
                return new string[0];

            var instanceNames = new string[categorySample.InstanceNameTable.Count];
            categorySample.InstanceNameTable.Keys.CopyTo(instanceNames, 0);
            if (instanceNames.Length == 1 && instanceNames[0].CompareTo(PerformanceCounterLib.SingleInstanceName) == 0)
                return new string[0];

            return instanceNames;
        }

        public PerformanceCounter[] GetCounters()
        {
            if (GetInstanceNames().Length != 0)
                throw new ArgumentException(SR.GetString("Counter is not single instance, an instance name needs to be specified."));

            return GetCounters("");
        }

        public PerformanceCounter[] GetCounters(string instanceName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

            if (instanceName.Length != 0 && !InstanceExists(instanceName))
                throw new InvalidOperationException(SR.GetString("Instance {0} does not exist in category {1}.", instanceName, _categoryName));

            var counterNames = PerformanceCounterLib.GetCounters(_categoryName);
            var counters = new PerformanceCounter[counterNames.Length];
            for (var index = 0; index < counters.Length; index++)
                counters[index] = new PerformanceCounter(_categoryName, counterNames[index], instanceName, true);

            return counters;
        }

        public static PerformanceCounterCategory[] GetCategories()
        {
            var categoryNames = PerformanceCounterLib.GetCategories();
            var categories = new PerformanceCounterCategory[categoryNames.Length];
            for (var index = 0; index < categories.Length; index++)
                categories[index] = new PerformanceCounterCategory(categoryNames[index]);

            return categories;
        }

        public string[] GetInstanceNames()
        {
            if (_categoryName == null)
                throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

            return GetCounterInstances(_categoryName);
        }

        public bool InstanceExists(string instanceName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

            var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName);
            return categorySample.InstanceNameTable.ContainsKey(instanceName);
        }

        public static bool InstanceExists(string instanceName, string categoryName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.GetString("Invalid value '{1}' for parameter '{0}'.", "categoryName", categoryName));

            var category = new PerformanceCounterCategory(categoryName);
            return category.InstanceExists(instanceName);
        }

        public Dictionary<string, Dictionary<string, InstanceData>> ReadCategory()
        {
            if (_categoryName == null)
                throw new InvalidOperationException(SR.GetString("Category name property has not been set."));

            var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName);
            return categorySample.ReadCategory();
        }
    }

    [Flags]
    internal enum PerformanceCounterCategoryOptions
    {
        EnableReuse = 0x1,
        UseUniqueSharedMemory = 0x2
    }
}