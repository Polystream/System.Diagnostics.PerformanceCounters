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

namespace System.Diagnostics.Windows
{
    class PerformanceCounterLib
    {
        internal const string PerfShimName = "netfxperf.dll";
        const string PerfShimFullNameSuffix = @"\netfxperf.dll";
        internal const string OpenEntryPoint = "OpenPerformanceData";
        internal const string CollectEntryPoint = "CollectPerformanceData";
        internal const string CloseEntryPoint = "ClosePerformanceData";

        const string PerflibPath = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Perflib";
        internal const string ServicePath = "SYSTEM\\CurrentControlSet\\Services";
        const string CategorySymbolPrefix = "OBJECT_";
        const string ConterSymbolPrefix = "DEVICE_COUNTER_";
        const string HelpSufix = "_HELP";
        const string NameSufix = "_NAME";
        const string TextDefinition = "[text]";
        const string InfoDefinition = "[info]";
        const string LanguageDefinition = "[languages]";
        const string ObjectDefinition = "[objects]";
        const string DriverNameKeyword = "drivername";
        const string SymbolFileKeyword = "symbolfile";
        const string DefineKeyword = "#define";
        const string LanguageKeyword = "language";
        const string DllName = "netfxperf.dll";
        
        static volatile string _iniFilePath;
        static volatile string _symbolFilePath;
        static volatile PerformanceCounterLib _library;

        static object _sInternalSyncObject;
        readonly object _categoryTableLock = new object();
        readonly object _helpTableLock = new object();
        readonly object _nameTableLock = new object();
        Dictionary<string, CategoryEntry> _categoryTable;

        Dictionary<string, PerformanceCounterCategoryType> _customCategoryTable;
        Dictionary<int, string> _helpTable;
        Dictionary<int, string> _nameTable;

        PerformanceMonitor _performanceMonitor;

        static object InternalSyncObject
        {
            get
            {
                if (_sInternalSyncObject == null)
                {
                    var o = new object();
                    Interlocked.CompareExchange(ref _sInternalSyncObject, o, null);
                }
                return _sInternalSyncObject;
            }
        }

        unsafe Dictionary<string, CategoryEntry> CategoryTable
        {
            get
            {
                if (_categoryTable == null)
                {
                    lock (_categoryTableLock)
                    {
                        if (_categoryTable == null)
                        {
                            var perfData = GetPerformanceData("Global");

                            fixed(byte* perfDataPtr = perfData)
                            {
                                var dataRef = new IntPtr(perfDataPtr);
                                var dataBlock = new NativeMethods.PERF_DATA_BLOCK();
                                Marshal.PtrToStructure(dataRef, dataBlock);
                                dataRef = (IntPtr)((long)dataRef + dataBlock.HeaderLength);
                                var categoryNumber = dataBlock.NumObjectTypes;

                                // on some machines MSMQ claims to have 4 categories, even though it only has 2.
                                // This causes us to walk past the end of our data, potentially crashing or reading
                                // data we shouldn't.  We use endPerfData to make sure we don't go past the end
                                // of the perf data.  (ASURT 137097)
                                var endPerfData = (long)new IntPtr(perfDataPtr) + dataBlock.TotalByteLength;
                                var tempCategoryTable = new Dictionary<string, CategoryEntry>(categoryNumber, StringComparer.OrdinalIgnoreCase);
                                for (var index = 0; index < categoryNumber && (long)dataRef < endPerfData; index++)
                                {
                                    var perfObject = new NativeMethods.PERF_OBJECT_TYPE();

                                    Marshal.PtrToStructure(dataRef, perfObject);

                                    var newCategoryEntry = new CategoryEntry(perfObject);
                                    var nextRef = (IntPtr)((long)dataRef + perfObject.TotalByteLength);
                                    dataRef = (IntPtr)((long)dataRef + perfObject.HeaderLength);

                                    var index3 = 0;
                                    var previousCounterIndex = -1;
                                    //Need to filter out counters that are repeated, some providers might
                                    //return several adyacent copies of the same counter.
                                    for (var index2 = 0; index2 < newCategoryEntry.CounterIndexes.Length; ++index2)
                                    {
                                        var perfCounter = new NativeMethods.PERF_COUNTER_DEFINITION();
                                        Marshal.PtrToStructure(dataRef, perfCounter);
                                        if (perfCounter.CounterNameTitleIndex != previousCounterIndex)
                                        {
                                            newCategoryEntry.CounterIndexes[index3] = perfCounter.CounterNameTitleIndex;
                                            newCategoryEntry.HelpIndexes[index3] = perfCounter.CounterHelpTitleIndex;
                                            previousCounterIndex = perfCounter.CounterNameTitleIndex;
                                            ++index3;
                                        }
                                        dataRef = (IntPtr)((long)dataRef + perfCounter.ByteLength);
                                    }

                                    //Lets adjust the entry counter arrays in case there were repeated copies
                                    if (index3 < newCategoryEntry.CounterIndexes.Length)
                                    {
                                        var adjustedCounterIndexes = new int[index3];
                                        var adjustedHelpIndexes = new int[index3];
                                        Array.Copy(newCategoryEntry.CounterIndexes, adjustedCounterIndexes, index3);
                                        Array.Copy(newCategoryEntry.HelpIndexes, adjustedHelpIndexes, index3);
                                        newCategoryEntry.CounterIndexes = adjustedCounterIndexes;
                                        newCategoryEntry.HelpIndexes = adjustedHelpIndexes;
                                    }

                                    if (NameTable.TryGetValue(newCategoryEntry.NameIndex, out var categoryName))
                                        tempCategoryTable[categoryName] = newCategoryEntry;

                                    dataRef = nextRef;
                                }

                                _categoryTable = tempCategoryTable;
                            }
                        }
                    }
                }

                return _categoryTable;
            }
        }

        internal Dictionary<int, string> HelpTable
        {
            get
            {
                if (_helpTable == null)
                {
                    lock (_helpTableLock)
                    {
                        if (_helpTable == null)
                            _helpTable = GetStringTable(true);
                    }
                }

                return _helpTable;
            }
        }

        // Returns a temp file name
        static string IniFilePath
        {
            get
            {
                if (_iniFilePath == null)
                {
                    lock (InternalSyncObject)
                    {
                        if (_iniFilePath == null)
                            _iniFilePath = Path.GetTempFileName();
                    }
                }

                return _iniFilePath;
            }
        }

        internal Dictionary<int, string> NameTable
        {
            get
            {
                if (_nameTable == null)
                {
                    lock (_nameTableLock)
                    {
                        if (_nameTable == null)
                            _nameTable = GetStringTable(false);
                    }
                }

                return _nameTable;
            }
        }

        // Returns a temp file name
        static string SymbolFilePath
        {
            get
            {
                if (_symbolFilePath == null)
                {
                    lock (InternalSyncObject)
                    {
                        if (_symbolFilePath == null)
                            _symbolFilePath = Path.GetTempFileName();
                    }
                }

                return _symbolFilePath;
            }
        }

        internal static PerformanceCounterLib Library
        {
            get
            {
                if (_library == null)
                {
                    lock (InternalSyncObject)
                    {
                        if (_library == null)
                            _library = new PerformanceCounterLib();
                    }
                }

                return _library;
            }
        }

        internal static bool CategoryExists(string category)
        {
            return Library.CategoryExistsInternal(category);
        }

        internal bool CategoryExistsInternal(string category)
        {
            return CategoryTable.ContainsKey(category);
        }

        internal static void CloseAllLibraries()
        {
            _library?.Close();
            _library = null;
        }

        internal static void CloseAllTables()
        {
            _library?.CloseTables();
        }

        internal void CloseTables()
        {
            _nameTable = null;
            _helpTable = null;
            _categoryTable = null;
            _customCategoryTable = null;
        }

        internal void Close()
        {
            if (_performanceMonitor != null)
            {
                _performanceMonitor.Close();
                _performanceMonitor = null;
            }

            CloseTables();
        }

        internal static bool CounterExists(string category, string counter)
        {
            var categoryExists = false;
            var counterExists = Library.CounterExistsInternal(category, counter, ref categoryExists);

            if (!categoryExists)
            {
                // Consider adding diagnostic logic here, may be we can dump the nameTable...
                throw new InvalidOperationException(SR.GetString("Category does not exist."));
            }

            return counterExists;
        }

        // ReSharper disable once RedundantAssignment
        bool CounterExistsInternal(string category, string counter, ref bool categoryExists)
        {
            categoryExists = false;
            if (!CategoryTable.ContainsKey(category))
                return false;

            categoryExists = true;

            var entry = CategoryTable[category];
            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];
                
                var counterName = GetCounterName(counterIndex);

                if (string.Compare(counterName, counter, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }

            return false;
        }

        static void CreateIniFile(string categoryName, string categoryHelp, List<CounterCreationData> creationData, string[] languageIds)
        {
            using (var iniWriter = new StreamWriter(File.OpenWrite(IniFilePath)))
            {
                //NT4 won't be able to parse Unicode ini files without this
                //extra white space.
                iniWriter.WriteLine("");
                iniWriter.WriteLine(InfoDefinition);
                iniWriter.Write(DriverNameKeyword);
                iniWriter.Write("=");
                iniWriter.WriteLine(categoryName);
                iniWriter.Write(SymbolFileKeyword);
                iniWriter.Write("=");
                iniWriter.WriteLine(Path.GetFileName(SymbolFilePath));
                iniWriter.WriteLine("");

                iniWriter.WriteLine(LanguageDefinition);
                foreach (var languageId in languageIds)
                {
                    iniWriter.Write(languageId);
                    iniWriter.Write("=");
                    iniWriter.Write(LanguageKeyword);
                    iniWriter.WriteLine(languageId);
                }

                iniWriter.WriteLine("");

                iniWriter.WriteLine(ObjectDefinition);
                foreach (var languageId in languageIds)
                {
                    iniWriter.Write(CategorySymbolPrefix);
                    iniWriter.Write("1_");
                    iniWriter.Write(languageId);
                    iniWriter.Write(NameSufix);
                    iniWriter.Write("=");
                    iniWriter.WriteLine(categoryName);
                }

                iniWriter.WriteLine("");

                iniWriter.WriteLine(TextDefinition);
                foreach (var languageId in languageIds)
                {
                    iniWriter.Write(CategorySymbolPrefix);
                    iniWriter.Write("1_");
                    iniWriter.Write(languageId);
                    iniWriter.Write(NameSufix);
                    iniWriter.Write("=");
                    iniWriter.WriteLine(categoryName);
                    iniWriter.Write(CategorySymbolPrefix);
                    iniWriter.Write("1_");
                    iniWriter.Write(languageId);
                    iniWriter.Write(HelpSufix);
                    iniWriter.Write("=");
                    if (string.IsNullOrEmpty(categoryHelp))
                        iniWriter.WriteLine(SR.GetString("Help not available."));
                    else
                        iniWriter.WriteLine(categoryHelp);

                    var counterIndex = 0;
                    foreach (var counterData in creationData)
                    {
                        ++counterIndex;
                        iniWriter.WriteLine("");
                        iniWriter.Write(ConterSymbolPrefix);
                        iniWriter.Write(counterIndex.ToString(CultureInfo.InvariantCulture));
                        iniWriter.Write("_");
                        iniWriter.Write(languageId);
                        iniWriter.Write(NameSufix);
                        iniWriter.Write("=");
                        iniWriter.WriteLine(counterData.CounterName);

                        iniWriter.Write(ConterSymbolPrefix);
                        iniWriter.Write(counterIndex.ToString(CultureInfo.InvariantCulture));
                        iniWriter.Write("_");
                        iniWriter.Write(languageId);
                        iniWriter.Write(HelpSufix);
                        iniWriter.Write("=");

                        Debug.Assert(!string.IsNullOrEmpty(counterData.CounterHelp), "CounterHelp should have been fixed up by the caller");
                        iniWriter.WriteLine(counterData.CounterHelp);
                    }
                }

                iniWriter.WriteLine("");
            }
        }

        static void CreateRegistryEntry(string categoryName, PerformanceCounterCategoryType categoryType, List<CounterCreationData> creationData, ref bool iniRegistered)
        {
            RegistryKey serviceParentKey = null;
            RegistryKey serviceKey = null;
            RegistryKey linkageKey = null;

            try
            {
                serviceParentKey = Registry.LocalMachine.OpenSubKey(ServicePath, true);

                serviceKey = serviceParentKey.OpenSubKey(categoryName + "\\Performance", true) ?? serviceParentKey.CreateSubKey(categoryName + "\\Performance");

                serviceKey.SetValue("Open", "OpenPerformanceData");
                serviceKey.SetValue("Collect", "CollectPerformanceData");
                serviceKey.SetValue("Close", "ClosePerformanceData");
                serviceKey.SetValue("Library", DllName);
                serviceKey.SetValue("IsMultiInstance", (int)categoryType, RegistryValueKind.DWord);
                serviceKey.SetValue("CategoryOptions", 0x3, RegistryValueKind.DWord);

                var counters = new string[creationData.Count];
                var counterTypes = new string[creationData.Count];
                for (var i = 0; i < creationData.Count; i++)
                {
                    counters[i] = creationData[i].CounterName;
                    counterTypes[i] = ((int)creationData[i].CounterType).ToString(CultureInfo.InvariantCulture);
                }

                linkageKey = serviceParentKey.OpenSubKey(categoryName + "\\Linkage", true) ?? serviceParentKey.CreateSubKey(categoryName + "\\Linkage");

                linkageKey.SetValue("Export", new[] { categoryName });

                serviceKey.SetValue("Counter Types", counterTypes);
                serviceKey.SetValue("Counter Names", counters);

                var firstId = serviceKey.GetValue("First Counter");
                if (firstId != null)
                    iniRegistered = true;
                else
                    iniRegistered = false;
            }
            finally
            {
                serviceKey?.Dispose();
                linkageKey?.Dispose();
                serviceParentKey?.Dispose();
            }
        }

        static void CreateSymbolFile(List<CounterCreationData> creationData)
        {
            using (var symbolWriter = new StreamWriter(File.OpenWrite(SymbolFilePath)))
            {
                symbolWriter.Write(DefineKeyword);
                symbolWriter.Write(" ");
                symbolWriter.Write(CategorySymbolPrefix);
                symbolWriter.WriteLine("1 0;");

                for (var counterIndex = 1; counterIndex <= creationData.Count; ++counterIndex)
                {
                    symbolWriter.Write(DefineKeyword);
                    symbolWriter.Write(" ");
                    symbolWriter.Write(ConterSymbolPrefix);
                    symbolWriter.Write(counterIndex.ToString(CultureInfo.InvariantCulture));
                    symbolWriter.Write(" ");
                    symbolWriter.Write((counterIndex * 2).ToString(CultureInfo.InvariantCulture));
                    symbolWriter.WriteLine(";");
                }

                symbolWriter.WriteLine("");
            }
        }

        static void DeleteRegistryEntry(string categoryName)
        {
            RegistryKey serviceKey = null;

            try
            {
                serviceKey = Registry.LocalMachine.OpenSubKey(ServicePath, true);

                var deleteCategoryKey = false;
                using (var categoryKey = serviceKey.OpenSubKey(categoryName, true))
                {
                    if (categoryKey != null)
                    {
                        if (categoryKey.GetValueNames().Length == 0)
                            deleteCategoryKey = true;
                        else
                        {
                            categoryKey.DeleteSubKeyTree("Linkage");
                            categoryKey.DeleteSubKeyTree("Performance");
                        }
                    }
                }
                if (deleteCategoryKey)
                    serviceKey.DeleteSubKeyTree(categoryName);
            }
            finally
            {
                serviceKey?.Dispose();
            }
        }

        static void DeleteTemporaryFiles()
        {
            try
            {
                File.Delete(IniFilePath);
            }
            catch
            {
                // ignored
            }

            try
            {
                File.Delete(SymbolFilePath);
            }
            catch
            {
                // ignored
            }
        }

        // Ensures that the customCategoryTable is initialized and decides whether the category passed in 
        //  1) is a custom category
        //  2) is a multi instance custom category
        // The return value is whether the category is a custom category or not. 
        internal bool FindCustomCategory(string category, out PerformanceCounterCategoryType categoryType)
        {
            RegistryKey key = null;

            if (_customCategoryTable == null)
                Interlocked.CompareExchange(ref _customCategoryTable, new Dictionary<string, PerformanceCounterCategoryType>(StringComparer.OrdinalIgnoreCase), null);

            if (_customCategoryTable.TryGetValue(category, out categoryType))
            {
                categoryType = _customCategoryTable[category];
                return true;
            }

            categoryType = PerformanceCounterCategoryType.Unknown;

            try
            {
                var keyPath = ServicePath + "\\" + category + "\\Performance";
                key = Registry.LocalMachine.OpenSubKey(keyPath);

                var systemDllName = key?.GetValue("Library", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (systemDllName is string s && (string.Compare(s, PerfShimName, StringComparison.OrdinalIgnoreCase) == 0
                                || s.EndsWith(PerfShimFullNameSuffix, StringComparison.OrdinalIgnoreCase)))
                {
                    var isMultiInstanceObject = key.GetValue("IsMultiInstance");
                    if (isMultiInstanceObject != null)
                    {
                        categoryType = (PerformanceCounterCategoryType)isMultiInstanceObject;
                        if (categoryType < PerformanceCounterCategoryType.Unknown || categoryType > PerformanceCounterCategoryType.MultiInstance)
                            categoryType = PerformanceCounterCategoryType.Unknown;
                    }
                    else
                        categoryType = PerformanceCounterCategoryType.Unknown;

                    var objectId = key.GetValue("First Counter");
                    if (objectId != null)
                    {
                        _customCategoryTable[category] = categoryType;
                        return true;
                    }
                }
            }
            finally
            {
                key?.Dispose();
            }

            return false;
        }

        internal static string[] GetCategories()
        {
            return Library.GetCategoriesInternal();
        }

        internal string[] GetCategoriesInternal()
        {
            ICollection keys = CategoryTable.Keys;
            var categories = new string[keys.Count];
            keys.CopyTo(categories, 0);
            return categories;
        }

        internal static string GetCategoryHelp(string category)
        {
            var help = Library.GetCategoryHelpInternal(category);

            if (help == null)
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return help;
        }

        string GetCategoryHelpInternal(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            HelpTable.TryGetValue(entry.HelpIndex, out var help);

            return help;
        }

        internal static CategorySample GetCategorySample(string category)
        {
            var sample = Library.GetCategorySampleInternal(category);

            if (sample == null)
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return sample;
        }

        CategorySample GetCategorySampleInternal(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            var dataRef = GetPerformanceData(entry.NameIndex.ToString(CultureInfo.InvariantCulture));
            if (dataRef == null)
                throw new InvalidOperationException(SR.GetString("Cannot read Category {0}.", category));

            var sample = new CategorySample(dataRef, entry, this);
            return sample;
        }

        internal static string[] GetCounters(string category)
        {
            var categoryExists = false;
            var counters = Library.GetCountersInternal(category, ref categoryExists);

            if (!categoryExists)
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return counters;
        }

        // ReSharper disable once RedundantAssignment
        string[] GetCountersInternal(string category, ref bool categoryExists)
        {
            categoryExists = false;

            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            categoryExists = true;

            var index2 = 0;
            var counters = new string[entry.CounterIndexes.Length];
            for (var index = 0; index < counters.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];

                if (NameTable.TryGetValue(counterIndex, out var counterName) && !string.IsNullOrEmpty(counterName))
                { 
                    counters[index2] = counterName;
                    ++index2;
                }
            }

            //Lets adjust the array in case there were null entries
            if (index2 < counters.Length)
            {
                var adjustedCounters = new string[index2];
                Array.Copy(counters, adjustedCounters, index2);
                counters = adjustedCounters;
            }

            return counters;
        }

        internal static string GetCounterHelp(string category, string counter)
        {
            var categoryExists = false;

            var help = Library.GetCounterHelpInternal(category, counter, ref categoryExists);

            if (!categoryExists)
                throw new InvalidOperationException(SR.GetString("Category {0} does not exist.", category));

            return help;
        }

        // ReSharper disable once RedundantAssignment
        string GetCounterHelpInternal(string category, string counter, ref bool categoryExists)
        {
            categoryExists = false;

            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            categoryExists = true;

            var helpIndex = -1;
            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];
                
                var counterName = GetCounterName(counterIndex);

                if (string.Compare(counterName, counter, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    helpIndex = entry.HelpIndexes[index];
                    break;
                }
            }

            if (helpIndex == -1)
                throw new InvalidOperationException(SR.GetString("Counter {0} does not exist.", counter));


            HelpTable.TryGetValue(entry.HelpIndex, out var help);
            
            return help ?? string.Empty;
        }

        internal string GetCounterName(int index)
        {
            NameTable.TryGetValue(index, out var counterName);

            return counterName ?? string.Empty;
        }

        static string[] GetLanguageIds()
        {
            RegistryKey libraryParentKey = null;
            var ids = new string[0];
            try
            {
                libraryParentKey = Registry.LocalMachine.OpenSubKey(PerflibPath);

                if (libraryParentKey != null)
                    ids = libraryParentKey.GetSubKeyNames();
            }
            finally
            {
                libraryParentKey?.Dispose();
            }

            return ids;
        }

        internal byte[] GetPerformanceData(string item)
        {
            if (_performanceMonitor == null)
            {
                lock (InternalSyncObject)
                {
                    if (_performanceMonitor == null)
                        _performanceMonitor = new PerformanceMonitor();
                }
            }

            return _performanceMonitor.GetData(item);
        }

        Dictionary<int, string> GetStringTable(bool isHelp)
        {
            Dictionary<int, string> stringTable;

            using (var libraryKey = Registry.PerformanceData)

            {
                string[] names = null;
                var waitRetries = 14; //((2^13)-1)*10ms == approximately 1.4mins
                var waitSleep = 0;

                // In some stress situations, querying counter values from 
                // HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib\009 
                // often returns null/empty data back. We should build fault-tolerance logic to 
                // make it more reliable because getting null back once doesn't necessarily mean 
                // that the data is corrupted, most of the time we would get the data just fine 
                // in subsequent tries.
                while (waitRetries > 0)
                {
                    try
                    {
                        if (!isHelp)
                            names = (string[])libraryKey.GetValue("Counter " + CultureInfo.CurrentCulture.Name);
                        else
                            names = (string[])libraryKey.GetValue("Explain " + CultureInfo.CurrentCulture.Name);

                        if (names == null || names.Length == 0)
                        {
                            --waitRetries;
                            if (waitSleep == 0)
                                waitSleep = 10;
                            else
                            {
                                Thread.Sleep(waitSleep);
                                waitSleep *= 2;
                            }
                        }
                        else
                            break;
                    }
                    catch (IOException)
                    {
                        // RegistryKey throws if it can't find the value.  We want to return an empty table
                        // and throw a different exception higher up the stack. 
                        names = null;
                        break;
                    }
                    catch (InvalidCastException)
                    {
                        // Unable to cast object of type 'System.Byte[]' to type 'System.String[]'.
                        // this happens when the registry data store is corrupt and the type is not even REG_MULTI_SZ
                        names = null;
                        break;
                    }
                }

                if (names == null)
                    stringTable = new Dictionary<int, string>();
                else
                {
                    stringTable = new Dictionary<int, string>(names.Length / 2);

                    for (var index = 0; index < names.Length / 2; ++index)
                    {
                        var nameString = names[index * 2 + 1] ?? string.Empty;

                        if (!int.TryParse(names[index * 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
                        {
                            if (isHelp)
                            {
                                // Category Help Table
                                throw new InvalidOperationException(SR.GetString("Cannot load Category Help data because an invalid index '{0}' was read from the registry.", names[index * 2]));
                            }
                            // Counter Name Table 
                            throw new InvalidOperationException(SR.GetString("Cannot load Counter Name data because an invalid index '{0}' was read from the registry.", names[index * 2]));
                        }

                        stringTable[key] = nameString;
                    }
                }
            }

            return stringTable;
        }

        internal static bool IsCustomCategory(string category)
        {
            return Library.IsCustomCategoryInternal(category);
        }

        internal static bool IsBaseCounter(int type)
        {
            return type == NativeMethods.PERF_AVERAGE_BASE ||
                   type == NativeMethods.PERF_COUNTER_MULTI_BASE ||
                   type == NativeMethods.PERF_RAW_BASE ||
                   type == NativeMethods.PERF_LARGE_RAW_BASE ||
                   type == NativeMethods.PERF_SAMPLE_BASE;
        }

        bool IsCustomCategoryInternal(string category)
        {
            return FindCustomCategory(category, out PerformanceCounterCategoryType _);
        }

        internal static PerformanceCounterCategoryType GetCategoryType(string category)
        {
            Library.FindCustomCategory(category, out PerformanceCounterCategoryType categoryType);

            return categoryType;
        }

        internal static void RegisterCategory(string categoryName, PerformanceCounterCategoryType categoryType, string categoryHelp, List<CounterCreationData> creationData)
        {
            try
            {
                var iniRegistered = false;
                CreateRegistryEntry(categoryName, categoryType, creationData, ref iniRegistered);
                if (!iniRegistered)
                {
                    var languageIds = GetLanguageIds();
                    CreateIniFile(categoryName, categoryHelp, creationData, languageIds);
                    CreateSymbolFile(creationData);
                    RegisterFiles(IniFilePath, false);
                }
                CloseAllTables();
                CloseAllLibraries();
            }
            finally
            {
                DeleteTemporaryFiles();
            }
        }

        static void RegisterFiles(string arg0, bool unregister)
        {
            var sb = new StringBuilder();

            NativeMethods.GetSystemDirectory(sb, 260);

            var systemDirectory = sb.ToString();

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = systemDirectory
            };

            if (unregister)
                processStartInfo.FileName = systemDirectory + "\\unlodctr.exe";
            else
                processStartInfo.FileName = systemDirectory + "\\lodctr.exe";

            int res;

            processStartInfo.Arguments = "\"" + arg0 + "\"";
            var p = Process.Start(processStartInfo);
            p.WaitForExit();

            res = p.ExitCode;

            if (res == NativeMethods.ERROR_ACCESS_DENIED)
                throw new UnauthorizedAccessException(SR.GetString("Cannot create or delete the Performance Category '{0}' because access is denied.", arg0));

            // Look at Q269225, unlodctr might return 2 when WMI is not installed.
            if (unregister && res == 2)
                res = 0;

            if (res != 0)
                throw SharedUtils.CreateSafeWin32Exception(res);
        }

        internal static void UnregisterCategory(string categoryName)
        {
            RegisterFiles(categoryName, true);
            DeleteRegistryEntry(categoryName);
            CloseAllTables();
            CloseAllLibraries();
        }
        internal static CustomPerformanceCounter CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName, PerformanceCounterInstanceLifetime lifetime)
        {
            return new SharedPerformanceCounter(categoryName, counterName, instanceName, lifetime);
        }

        internal static void RemoveAllCustomInstances(string categoryName)
        {
            SharedPerformanceCounter.RemoveAllInstances(categoryName);
        }
    }

    internal class PerformanceMonitor
    {
        RegistryKey _perfDataKey;

        internal PerformanceMonitor()
        {
            Init();
        }

        void Init()
        {
            try
            {
                _perfDataKey = Registry.PerformanceData;
            }
            catch (UnauthorizedAccessException)
            {
                // we need to do this for compatibility with v1.1 and v1.0.
                throw new Win32Exception(NativeMethods.ERROR_ACCESS_DENIED);
            }
            catch (IOException e)
            {
                // we need to do this for compatibility with v1.1 and v1.0.
                throw new Win32Exception(Marshal.GetHRForException(e));
            }
        }

        internal void Close()
        {
            _perfDataKey?.Dispose();

            _perfDataKey = null;
        }

        // Win32 RegQueryValueEx for perf data could deadlock (for a Mutex) up to 2mins in some 
        // scenarios before they detect it and exit gracefully. In the mean time, ERROR_BUSY, 
        // ERROR_NOT_READY etc can be seen by other concurrent calls (which is the reason for the 
        // wait loop and switch case below). We want to wait most certainly more than a 2min window. 
        // The curent wait time of up to 10mins takes care of the known stress deadlock issues. In most 
        // cases we wouldn't wait for more than 2mins anyways but in worst cases how much ever time 
        // we wait may not be sufficient if the Win32 code keeps running into this deadlock again 
        // and again. A condition very rare but possible in theory. We would get back to the user 
        // in this case with InvalidOperationException after the wait time expires.
        internal byte[] GetData(string item)
        {
            var waitRetries = 17; //2^16*10ms == approximately 10mins
            var waitSleep = 0;
            var error = 0;

            // no need to revert here since we'll fall off the end of the method
            while (waitRetries > 0)
            {
                try
                {
                    var data = (byte[])_perfDataKey.GetValue(item);
                    return data;
                }
                catch (IOException e)
                {
                    error = Marshal.GetHRForException(e);
                    switch (error)
                    {
                        case NativeMethods.RPC_S_CALL_FAILED:
                        case NativeMethods.ERROR_INVALID_HANDLE:
                        case NativeMethods.RPC_S_SERVER_UNAVAILABLE:
                            Init();
                            goto case NativeMethods.WAIT_TIMEOUT;

                        case NativeMethods.WAIT_TIMEOUT:
                        case NativeMethods.ERROR_NOT_READY:
                        case NativeMethods.ERROR_LOCK_FAILED:
                        case NativeMethods.ERROR_BUSY:
                            --waitRetries;
                            if (waitSleep == 0)
                                waitSleep = 10;
                            else
                            {
                                Thread.Sleep(waitSleep);
                                waitSleep *= 2;
                            }
                            break;

                        default:
                            throw SharedUtils.CreateSafeWin32Exception(error);
                    }
                }
                catch (InvalidCastException e)
                {
                    throw new InvalidOperationException(SR.GetString("Cannot load Performance Counter data because an unexpected registry key value type was read from '{0}'.", _perfDataKey.ToString()), e);
                }
            }

            throw SharedUtils.CreateSafeWin32Exception(error);
        }
    }

    internal class CategoryEntry
    {
        internal int[] CounterIndexes;
        internal int HelpIndex;
        internal int[] HelpIndexes;
        internal int NameIndex;

        internal CategoryEntry(NativeMethods.PERF_OBJECT_TYPE perfObject)
        {
            NameIndex = perfObject.ObjectNameTitleIndex;
            HelpIndex = perfObject.ObjectHelpTitleIndex;
            CounterIndexes = new int[perfObject.NumCounters];
            HelpIndexes = new int[perfObject.NumCounters];
        }
    }

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

        readonly CategoryEntry _entry;
        readonly PerformanceCounterLib _library;

        unsafe internal CategorySample(byte[] data, CategoryEntry entry, PerformanceCounterLib library)
        {
            _entry = entry;
            _library = library;
            var categoryIndex = entry.NameIndex;
            var dataBlock = new NativeMethods.PERF_DATA_BLOCK();
            fixed(byte* dataPtr = data)
            {
                var dataRef = new IntPtr(dataPtr);

                Marshal.PtrToStructure(dataRef, dataBlock);
                SystemFrequency = dataBlock.PerfFreq;
                TimeStamp = dataBlock.PerfTime;
                TimeStamp100NSec = dataBlock.PerfTime100nSec;
                dataRef = (IntPtr)((long)dataRef + dataBlock.HeaderLength);
                var numPerfObjects = dataBlock.NumObjectTypes;
                if (numPerfObjects == 0)
                {
                    CounterTable = new Dictionary<int, Diagnostics.CounterDefinitionSample>();
                    InstanceNameTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                //Need to find the right category, GetPerformanceData might return
                //several of them.
                NativeMethods.PERF_OBJECT_TYPE perfObject = null;
                var foundCategory = false;
                for (var index = 0; index < numPerfObjects; index++)
                {
                    perfObject = new NativeMethods.PERF_OBJECT_TYPE();
                    Marshal.PtrToStructure(dataRef, perfObject);

                    if (perfObject.ObjectNameTitleIndex == categoryIndex)
                    {
                        foundCategory = true;
                        break;
                    }

                    dataRef = (IntPtr)((long)dataRef + perfObject.TotalByteLength);
                }

                if (!foundCategory)
                    throw new InvalidOperationException(SR.GetString("Could not Read Category Index: {0}.", categoryIndex.ToString(CultureInfo.CurrentCulture)));

                CounterFrequency = perfObject.PerfFreq;
                CounterTimeStamp = perfObject.PerfTime;
                var counterNumber = perfObject.NumCounters;
                var instanceNumber = perfObject.NumInstances;

                if (instanceNumber == -1)
                    IsMultiInstance = false;
                else
                    IsMultiInstance = true;

                // Move pointer forward to end of PERF_OBJECT_TYPE
                dataRef = (IntPtr)((long)dataRef + perfObject.HeaderLength);

                var samples = new CounterDefinitionSample[counterNumber];
                CounterTable = new Dictionary<int, Diagnostics.CounterDefinitionSample>(counterNumber);
                for (var index = 0; index < samples.Length; ++index)
                {
                    var perfCounter = new NativeMethods.PERF_COUNTER_DEFINITION();
                    Marshal.PtrToStructure(dataRef, perfCounter);
                    samples[index] = new CounterDefinitionSample(perfCounter, this, instanceNumber);
                    dataRef = (IntPtr)((long)dataRef + perfCounter.ByteLength);

                    var currentSampleType = samples[index].CounterType;
                    if (!PerformanceCounterLib.IsBaseCounter(currentSampleType))
                    {
                        // We'll put only non-base counters in the table. 
                        if (currentSampleType != NativeMethods.PERF_COUNTER_NODATA)
                            CounterTable[samples[index].NameIndex] = samples[index];
                    }
                    else
                    {
                        // it's a base counter, try to hook it up to the main counter. 
                        Debug.Assert(index > 0, "Index > 0 because base counters should never be at index 0");
                        if (index > 0)
                            samples[index - 1].BaseCounterDefinitionSample = samples[index];
                    }
                }

                // now set up the InstanceNameTable.  
                if (!IsMultiInstance)
                {
                    InstanceNameTable = new Dictionary<string, int>(1, StringComparer.OrdinalIgnoreCase)
                    {
                        [Diagnostics.PerformanceCounterLib.SingleInstanceName] = 0
                    };

                    for (var index = 0; index < samples.Length; ++index)
                        samples[index].SetInstanceValue(0, dataRef);
                }
                else
                {
                    string[] parentInstanceNames = null;
                    InstanceNameTable = new Dictionary<string, int>(instanceNumber, StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < instanceNumber; i++)
                    {
                        var perfInstance = new NativeMethods.PERF_INSTANCE_DEFINITION();
                        Marshal.PtrToStructure(dataRef, perfInstance);
                        if (perfInstance.ParentObjectTitleIndex > 0 && parentInstanceNames == null)
                            parentInstanceNames = GetInstanceNamesFromIndex(perfInstance.ParentObjectTitleIndex);

                        string instanceName;
                        if (parentInstanceNames != null && perfInstance.ParentObjectInstance >= 0 && perfInstance.ParentObjectInstance < parentInstanceNames.Length - 1)
                            instanceName = parentInstanceNames[perfInstance.ParentObjectInstance] + "/" + Marshal.PtrToStringUni((IntPtr)((long)dataRef + perfInstance.NameOffset));
                        else
                            instanceName = Marshal.PtrToStringUni((IntPtr)((long)dataRef + perfInstance.NameOffset));

                        //In some cases instance names are not unique (Process), same as perfmon
                        //generate a unique name.
                        var newInstanceName = instanceName;
                        var newInstanceNumber = 1;
                        while (true)
                        {
                            if (!InstanceNameTable.ContainsKey(newInstanceName))
                            {
                                InstanceNameTable[newInstanceName] = i;
                                break;
                            }

                            newInstanceName = instanceName + "#" + newInstanceNumber.ToString(CultureInfo.InvariantCulture);
                            ++newInstanceNumber;
                        }

                        dataRef = (IntPtr)((long)dataRef + perfInstance.ByteLength);
                        for (var index = 0; index < samples.Length; ++index)
                            samples[index].SetInstanceValue(i, dataRef);

                        dataRef = (IntPtr)((long)dataRef + Marshal.ReadInt32(dataRef));
                    }
                }
            }
        }
        
        unsafe internal override string[] GetInstanceNamesFromIndex(int categoryIndex)
        {
            var data = _library.GetPerformanceData(categoryIndex.ToString(CultureInfo.InvariantCulture));
            fixed(byte* dataPtr = data)
            {
                var dataRef = new IntPtr(dataPtr);

                var dataBlock = new NativeMethods.PERF_DATA_BLOCK();
                Marshal.PtrToStructure(dataRef, dataBlock);
                dataRef = (IntPtr)((long)dataRef + dataBlock.HeaderLength);
                var numPerfObjects = dataBlock.NumObjectTypes;

                NativeMethods.PERF_OBJECT_TYPE perfObject = null;
                var foundCategory = false;
                for (var index = 0; index < numPerfObjects; index++)
                {
                    perfObject = new NativeMethods.PERF_OBJECT_TYPE();
                    Marshal.PtrToStructure(dataRef, perfObject);

                    if (perfObject.ObjectNameTitleIndex == categoryIndex)
                    {
                        foundCategory = true;
                        break;
                    }

                    dataRef = (IntPtr)((long)dataRef + perfObject.TotalByteLength);
                }

                if (!foundCategory)
                    return new string[0];

                var counterNumber = perfObject.NumCounters;
                var instanceNumber = perfObject.NumInstances;
                dataRef = (IntPtr)((long)dataRef + perfObject.HeaderLength);

                if (instanceNumber == -1)
                    return new string[0];

                var samples = new CounterDefinitionSample[counterNumber];
                for (var index = 0; index < samples.Length; ++index)
                {
                    var perfCounter = new NativeMethods.PERF_COUNTER_DEFINITION();
                    Marshal.PtrToStructure(dataRef, perfCounter);
                    dataRef = (IntPtr)((long)dataRef + perfCounter.ByteLength);
                }

                var instanceNames = new string[instanceNumber];
                for (var i = 0; i < instanceNumber; i++)
                {
                    var perfInstance = new NativeMethods.PERF_INSTANCE_DEFINITION();
                    Marshal.PtrToStructure(dataRef, perfInstance);
                    instanceNames[i] = Marshal.PtrToStringUni((IntPtr)((long)dataRef + perfInstance.NameOffset));
                    dataRef = (IntPtr)((long)dataRef + perfInstance.ByteLength);
                    dataRef = (IntPtr)((long)dataRef + Marshal.ReadInt32(dataRef));
                }

                return instanceNames;
            }
        }

        internal override Diagnostics.CounterDefinitionSample GetCounterDefinitionSample(string counter)
        {
            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];

                if (_library.NameTable.TryGetValue(counterIndex, out var counterName) && counterName != null)
                {
                    if (string.Compare(counterName, counter, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        var sample = CounterTable[counterIndex];
                        if (sample == null)
                        {
                            //This is a base counter and has not been added to the table
                            foreach (var multiSample in CounterTable.Values)
                            {
                                if (multiSample.BaseCounterDefinitionSample != null &&
                                    multiSample.BaseCounterDefinitionSample.NameIndex == counterIndex)
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
#pragma warning disable 618
            var data = new Dictionary<string, Dictionary<string, InstanceData>>();
#pragma warning restore 618
            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];

                var name = _library.GetCounterName(counterIndex);
                if (!string.IsNullOrEmpty(name))
                {
                    var sample = CounterTable[counterIndex];
                    if (sample != null)
                        //If the current index refers to a counter base,
                        //the sample will be null
                        data.Add(name, sample.ReadInstanceData(name));
                }
            }

            return data;
        }
    }

    internal class CounterDefinitionSample : Diagnostics.CounterDefinitionSample
    {
        protected internal override int CounterType { get; set; }
        protected internal override int NameIndex { get; set; }
        protected internal override Diagnostics.CounterDefinitionSample BaseCounterDefinitionSample { get; set; }
        readonly int _offset;

        readonly int _size;
        readonly CategorySample _categorySample;
        readonly long[] _instanceValues;

        internal CounterDefinitionSample(NativeMethods.PERF_COUNTER_DEFINITION perfCounter, CategorySample categorySample, int instanceNumber)
        {
            NameIndex = perfCounter.CounterNameTitleIndex;
            CounterType = perfCounter.CounterType;
            _offset = perfCounter.CounterOffset;
            _size = perfCounter.CounterSize;
            _instanceValues = instanceNumber == -1 ? new long[1] : new long[instanceNumber];

            _categorySample = categorySample;
        }

        long ReadValue(IntPtr pointer)
        {
            if (_size == 4)
                return (uint)Marshal.ReadInt32((IntPtr)((long)pointer + _offset));

            if (_size == 8)
                return Marshal.ReadInt64((IntPtr)((long)pointer + _offset));

            return -1;
        }

        internal override CounterSample GetInstanceValue(string instanceName)
        {
            if (!_categorySample.InstanceNameTable.ContainsKey(instanceName))
            {
                // Our native dll truncates instance names to 128 characters.  If we can't find the instance
                // with the full name, try truncating to 128 characters. 
                if (instanceName.Length > SharedPerformanceCounter.InstanceNameMaxLength)
                    instanceName = instanceName.Substring(0, SharedPerformanceCounter.InstanceNameMaxLength);

                if (!_categorySample.InstanceNameTable.ContainsKey(instanceName))
                    throw new InvalidOperationException(SR.GetString("Instance '{0}' does not exist in the specified Category.", instanceName));
            }

            var index = _categorySample.InstanceNameTable[instanceName];
            var rawValue = _instanceValues[index];
            long baseValue = 0;
            if (BaseCounterDefinitionSample != null)
            {
                var baseCategorySample = ((CounterDefinitionSample)BaseCounterDefinitionSample)._categorySample;
                var baseIndex = baseCategorySample.InstanceNameTable[instanceName];
                baseValue = ((CounterDefinitionSample)BaseCounterDefinitionSample)._instanceValues[baseIndex];
            }

            return new CounterSample(rawValue,
                                     baseValue,
                                     _categorySample.CounterFrequency,
                                     _categorySample.SystemFrequency,
                                     _categorySample.TimeStamp,
                                     _categorySample.TimeStamp100NSec,
                                     (PerformanceCounterType)CounterType,
                                     _categorySample.CounterTimeStamp);
        }

        internal override Dictionary<string, InstanceData> ReadInstanceData(string counterName)
        {
#pragma warning disable 618
            var data = new Dictionary<string, InstanceData>();
#pragma warning restore 618

            var keys = new string[_categorySample.InstanceNameTable.Count];
            _categorySample.InstanceNameTable.Keys.CopyTo(keys, 0);
            var indexes = new int[_categorySample.InstanceNameTable.Count];
            _categorySample.InstanceNameTable.Values.CopyTo(indexes, 0);
            for (var index = 0; index < keys.Length; ++index)
            {
                long baseValue = 0;
                if (BaseCounterDefinitionSample != null)
                {
                    var baseCategorySample = ((CounterDefinitionSample)BaseCounterDefinitionSample)._categorySample;
                    var baseIndex = baseCategorySample.InstanceNameTable[keys[index]];
                    baseValue = ((CounterDefinitionSample)BaseCounterDefinitionSample)._instanceValues[baseIndex];
                }

                var sample = new CounterSample(_instanceValues[indexes[index]],
                                               baseValue,
                                               _categorySample.CounterFrequency,
                                               _categorySample.SystemFrequency,
                                               _categorySample.TimeStamp,
                                               _categorySample.TimeStamp100NSec,
                                               (PerformanceCounterType)CounterType,
                                               _categorySample.CounterTimeStamp);

                data.Add(keys[index], new InstanceData(keys[index], sample));
            }

            return data;
        }

        internal override CounterSample GetSingleValue()
        {
            var rawValue = _instanceValues[0];
            long baseValue = 0;
            if (BaseCounterDefinitionSample != null)
                baseValue = ((CounterDefinitionSample)BaseCounterDefinitionSample)._instanceValues[0];

            return new CounterSample(rawValue,
                                     baseValue,
                                     _categorySample.CounterFrequency,
                                     _categorySample.SystemFrequency,
                                     _categorySample.TimeStamp,
                                     _categorySample.TimeStamp100NSec,
                                     (PerformanceCounterType)CounterType,
                                     _categorySample.CounterTimeStamp);
        }

        internal void SetInstanceValue(int index, IntPtr dataRef)
        {
            var rawValue = ReadValue(dataRef);
            _instanceValues[index] = rawValue;
        }
    }
}