using System.Threading;
using PerformanceCounters;
// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor

namespace System.Diagnostics
{
    public sealed class PerformanceCounter : IDisposable
    {
        string _categoryName;
        string _counterName;
        int _counterType = -1;
        string _helpMsg;
        bool _initialized;
        PerformanceCounterInstanceLifetime _instanceLifetime = PerformanceCounterInstanceLifetime.Global;
        string _instanceName;

        bool _isReadOnly;

        object _mInstanceLockObject;

        // Cached old sample
        CounterSample _oldSample = CounterSample.Empty;

        // Cached IP Shared Performanco counter
        SharedPerformanceCounter _sharedCounter;

        public PerformanceCounter()
        {
            _categoryName = string.Empty;
            _counterName = string.Empty;
            _instanceName = string.Empty;
            _isReadOnly = true;
            GC.SuppressFinalize(this);
        }

        public PerformanceCounter(string categoryName, string counterName, string instanceName)
        {
            CategoryName = categoryName;
            CounterName = counterName;
            InstanceName = instanceName;
            _isReadOnly = true;
            Initialize();
            GC.SuppressFinalize(this);
        }

        public PerformanceCounter(string categoryName, string counterName, string instanceName, bool readOnly)
        {
            CategoryName = categoryName;
            CounterName = counterName;
            InstanceName = instanceName;
            _isReadOnly = readOnly;
            Initialize();
            GC.SuppressFinalize(this);
        }

        public PerformanceCounter(string categoryName, string counterName)
            :
            this(categoryName, counterName, true)
        {
        }

        public PerformanceCounter(string categoryName, string counterName, bool readOnly)
            :
            this(categoryName, counterName, "", readOnly)
        {
        }

        object InstanceLockObject
        {
            get
            {
                if (_mInstanceLockObject == null)
                {
                    var o = new object();
                    Interlocked.CompareExchange(ref _mInstanceLockObject, o, null);
                }
                return _mInstanceLockObject;
            }
        }

        public string CategoryName
        {
            get => _categoryName;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (_categoryName == null || string.Compare(_categoryName, value, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    _categoryName = value;
                    Close();
                }
            }
        }

        public string CounterHelp
        {
            get
            {
                var currentCategoryName = _categoryName;

                Initialize();

                return _helpMsg ?? (_helpMsg = PerformanceCounterLib.GetCounterHelp(currentCategoryName, _counterName));
            }
        }

        public string CounterName
        {
            get => _counterName;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (_counterName == null || string.Compare(_counterName, value, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    _counterName = value;
                    Close();
                }
            }
        }

        public PerformanceCounterType CounterType
        {
            get
            {
                if (_counterType == -1)
                {
                    var currentCategoryName = _categoryName;

                    // This is the same thing that NextSample does, except that it doesn't try to get the actual counter
                    // value.  If we wanted the counter value, we would need to have an instance name. 

                    Initialize();
                    var categorySample = PerformanceCounterLib.GetCategorySample(currentCategoryName);
                    var counterSample = categorySample.GetCounterDefinitionSample(_counterName);
                    _counterType = counterSample.CounterType;
                }

                return (PerformanceCounterType)_counterType;
            }
        }

        public PerformanceCounterInstanceLifetime InstanceLifetime
        {
            get => _instanceLifetime;
            set
            {
                if (value > PerformanceCounterInstanceLifetime.Process || value < PerformanceCounterInstanceLifetime.Global)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (_initialized)
                    throw new InvalidOperationException(SR.GetString("The InstanceLifetime cannot be set after the instance has been initialized.  You must use the default constructor and set the CategoryName, InstanceName, CounterName, InstanceLifetime and ReadOnly properties manually before setting the RawValue."));

                _instanceLifetime = value;
            }
        }

        public string InstanceName
        {
            get => _instanceName;
            set
            {
                if (value == null && _instanceName == null)
                    return;

                if (value == null && _instanceName != null ||
                    value != null && _instanceName == null ||
                    string.Compare(_instanceName, value, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    _instanceName = value;
                    Close();
                }
            }
        }

        public bool ReadOnly
        {
            get => _isReadOnly;

            set
            {
                if (value != _isReadOnly)
                {
                    _isReadOnly = value;
                    Close();
                }
            }
        }

        public long RawValue
        {
            get
            {
                if (ReadOnly)
                {
                    //No need to initialize or Demand, since NextSample already does.
                    return NextSample().RawValue;
                }

                Initialize();

                return _sharedCounter.Value;
            }
            set
            {
                if (ReadOnly)
                    ThrowReadOnly();

                Initialize();

                _sharedCounter.Value = value;
            }
        }

        public void Dispose()
        {
            // safe to call while finalizing or disposing
            //
            Close();
        }

        public void BeginInit()
        {
            Close();
        }

        public void Close()
        {
            _helpMsg = null;
            _oldSample = CounterSample.Empty;
            _sharedCounter = null;
            _initialized = false;
            _counterType = -1;
        }

        public static void CloseSharedResources()
        {
            PerformanceCounterLib.CloseAllLibraries();
        }

        public long Decrement()
        {
            if (ReadOnly)
                ThrowReadOnly();

            Initialize();

            return _sharedCounter.Decrement();
        }

        public void EndInit()
        {
            Initialize();
        }

        public long IncrementBy(long value)
        {
            if (_isReadOnly)
                ThrowReadOnly();

            Initialize();

            return _sharedCounter.IncrementBy(value);
        }

        public long Increment()
        {
            if (_isReadOnly)
                ThrowReadOnly();

            Initialize();

            return _sharedCounter.Increment();
        }

        void ThrowReadOnly()
        {
            throw new InvalidOperationException(SR.GetString("Cannot update Performance Counter, this object has been initialized as ReadOnly."));
        }

        void Initialize()
        {
            // Keep this method small so the JIT will inline it.
            if (!_initialized)
                InitializeImpl();
        }

        void InitializeImpl()
        {
            var tookLock = false;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                Monitor.Enter(InstanceLockObject, ref tookLock);

                if (!_initialized)
                {
                    var currentCategoryName = _categoryName;

                    if (currentCategoryName == string.Empty)
                        throw new InvalidOperationException(SR.GetString("Failed to initialize because CategoryName is missing."));
                    if (_counterName == string.Empty)
                        throw new InvalidOperationException(SR.GetString("Failed to initialize because CounterName is missing."));

                    if (ReadOnly)
                    {
                        if (!PerformanceCounterLib.CounterExists(currentCategoryName, _counterName))
                            throw new InvalidOperationException(SR.GetString("Could not locate Performance Counter with specified category name '{0}', counter name '{1}'.", currentCategoryName, _counterName));

                        var categoryType = PerformanceCounterLib.GetCategoryType(currentCategoryName);
                        if (categoryType == PerformanceCounterCategoryType.MultiInstance)
                        {
                            if (string.IsNullOrEmpty(_instanceName))
                                throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as multi-instance.  Performance counters in this category can only be created with instance names.", currentCategoryName));
                        }
                        else if (categoryType == PerformanceCounterCategoryType.SingleInstance)
                        {
                            if (!string.IsNullOrEmpty(_instanceName))
                                throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as single-instance.  Performance counters in this category can only be created without instance names.", currentCategoryName));
                        }

                        if (_instanceLifetime != PerformanceCounterInstanceLifetime.Global)
                            throw new InvalidOperationException(SR.GetString("InstanceLifetime is unused by ReadOnly counters."));

                        _initialized = true;
                    }
                    else
                    {
                        if (!PerformanceCounterLib.IsCustomCategory(currentCategoryName))
                            throw new InvalidOperationException(SR.GetString("The requested Performance Counter is not a custom counter, it has to be initialized as ReadOnly."));

                        // check category type
                        var categoryType = PerformanceCounterLib.GetCategoryType(currentCategoryName);
                        if (categoryType == PerformanceCounterCategoryType.MultiInstance)
                        {
                            if (string.IsNullOrEmpty(_instanceName))
                                throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as multi-instance.  Performance counters in this category can only be created with instance names.", currentCategoryName));
                        }
                        else if (categoryType == PerformanceCounterCategoryType.SingleInstance)
                        {
                            if (!string.IsNullOrEmpty(_instanceName))
                                throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as single-instance.  Performance counters in this category can only be created without instance names.", currentCategoryName));
                        }

                        if (string.IsNullOrEmpty(_instanceName) && InstanceLifetime == PerformanceCounterInstanceLifetime.Process)
                            throw new InvalidOperationException(SR.GetString("Single instance categories are only valid with the Global lifetime."));

                        _sharedCounter = new SharedPerformanceCounter(currentCategoryName.ToLower(), _counterName.ToLower(), _instanceName.ToLower(), _instanceLifetime);
                        _initialized = true;
                    }
                }
            }
            finally
            {
                if (tookLock)
                    Monitor.Exit(InstanceLockObject);
            }
        }

        public CounterSample NextSample()
        {
            var currentCategoryName = _categoryName;

            Initialize();
            var categorySample = PerformanceCounterLib.GetCategorySample(currentCategoryName);
            var counterSample = categorySample.GetCounterDefinitionSample(_counterName);
            _counterType = counterSample.CounterType;
            if (!categorySample.IsMultiInstance)
            {
                if (!string.IsNullOrEmpty(_instanceName))
                    throw new InvalidOperationException(SR.GetString("Counter is single instance, instance name '{0}' is not valid for this counter category.", _instanceName));

                return counterSample.GetSingleValue();
            }

            if (string.IsNullOrEmpty(_instanceName))
                throw new InvalidOperationException(SR.GetString("Counter is not single instance, an instance name needs to be specified."));

            return counterSample.GetInstanceValue(_instanceName);
        }

        public float NextValue()
        {
            //No need to initialize or Demand, since NextSample already does.
            var newSample = NextSample();
            float retVal;

            retVal = CounterSample.Calculate(_oldSample, newSample);
            _oldSample = newSample;

            return retVal;
        }

        public void RemoveInstance()
        {
            if (_isReadOnly)
                throw new InvalidOperationException(SR.GetString("Cannot remove Performance Counter Instance, this object as been initialized as ReadOnly."));

            Initialize();
            _sharedCounter.RemoveInstance(_instanceName.ToLower(), _instanceLifetime);
        }
    }
}