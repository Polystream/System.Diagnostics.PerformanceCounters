using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PerformanceCounters;

namespace System.Diagnostics
{
    public static class CounterSampleCalculator
    {
        static volatile bool _perfCounterDllLoaded;

        static float GetElapsedTime(CounterSample oldSample, CounterSample newSample)
        {
            float eSeconds;
            float eDifference;

            if (newSample.RawValue == 0)
            {
                // no data [start time = 0] so return 0
                return 0.0f;
            }

            float eFreq;
            eFreq = (ulong)oldSample.CounterFrequency;

            if (oldSample.UnsignedRawValue >= (ulong)newSample.CounterTimeStamp || eFreq <= 0.0f)
                return 0.0f;

            // otherwise compute difference between current time and start time
            eDifference = (ulong)newSample.CounterTimeStamp - oldSample.UnsignedRawValue;

            // convert to fractional seconds using object counter
            eSeconds = eDifference / eFreq;

            return eSeconds;
        }
        
        public static float ComputeCounterValue(CounterSample newSample)
        {
            return ComputeCounterValue(CounterSample.Empty, newSample);
        }

        public static float ComputeCounterValue(CounterSample oldSample, CounterSample newSample)
        {
            var newCounterType = (int)newSample.CounterType;
            if (oldSample.SystemFrequency == 0)
            {
                if (newCounterType != NativeMethods.PERF_RAW_FRACTION &&
                    newCounterType != NativeMethods.PERF_COUNTER_RAWCOUNT &&
                    newCounterType != NativeMethods.PERF_COUNTER_RAWCOUNT_HEX &&
                    newCounterType != NativeMethods.PERF_COUNTER_LARGE_RAWCOUNT &&
                    newCounterType != NativeMethods.PERF_COUNTER_LARGE_RAWCOUNT_HEX &&
                    newCounterType != NativeMethods.PERF_COUNTER_MULTI_BASE)
                {
                    // Since oldSample has a system frequency of 0, this means the newSample is the first sample
                    // on a two sample calculation.  Since we can't do anything with it, return 0.
                    return 0.0f;
                }
            }
            else if (oldSample.CounterType != newSample.CounterType)
                throw new InvalidOperationException(SR.GetString("Mismatched counter types."));

            if (newCounterType == NativeMethods.PERF_ELAPSED_TIME)
                return GetElapsedTime(oldSample, newSample);

            var newPdhValue = new NativeMethods.PDH_RAW_COUNTER();
            var oldPdhValue = new NativeMethods.PDH_RAW_COUNTER();

            FillInValues(oldSample, newSample, oldPdhValue, newPdhValue);

            LoadPerfCounterDll();

            var pdhFormattedValue = new NativeMethods.PDH_FMT_COUNTERVALUE();
            var timeBase = newSample.SystemFrequency;
            var result = SafeNativeMethods.FormatFromRawValue((uint)newCounterType, NativeMethods.PDH_FMT_DOUBLE | NativeMethods.PDH_FMT_NOSCALE | NativeMethods.PDH_FMT_NOCAP100,
                                                              ref timeBase, newPdhValue, oldPdhValue, pdhFormattedValue);

            if (result != NativeMethods.ERROR_SUCCESS)
            {
                // If the numbers go negative, just return 0.  This better matches the old behavior. 
                if (result == NativeMethods.PDH_CALC_NEGATIVE_VALUE || result == NativeMethods.PDH_CALC_NEGATIVE_DENOMINATOR || result == NativeMethods.PDH_NO_DATA)
                    return 0;

                throw new Win32Exception(result, SR.GetString("There was an error calculating the PerformanceCounter value (0x{0}).", result.ToString("x", CultureInfo.InvariantCulture)));
            }

            return (float)pdhFormattedValue.data;
        }

        static void FillInValues(CounterSample oldSample, CounterSample newSample, NativeMethods.PDH_RAW_COUNTER oldPdhValue, NativeMethods.PDH_RAW_COUNTER newPdhValue)
        {
            var newCounterType = (int)newSample.CounterType;

            switch (newCounterType)
            {
                case NativeMethods.PERF_COUNTER_COUNTER:
                case NativeMethods.PERF_COUNTER_QUEUELEN_TYPE:
                case NativeMethods.PERF_SAMPLE_COUNTER:
                case NativeMethods.PERF_OBJ_TIME_TIMER:
                case NativeMethods.PERF_COUNTER_OBJ_TIME_QUEUELEN_TYPE:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = newSample.TimeStamp;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = oldSample.TimeStamp;
                    break;

                case NativeMethods.PERF_COUNTER_100NS_QUEUELEN_TYPE:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = newSample.TimeStamp100NSec;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = oldSample.TimeStamp100NSec;
                    break;

                case NativeMethods.PERF_COUNTER_TIMER:
                case NativeMethods.PERF_COUNTER_TIMER_INV:
                case NativeMethods.PERF_COUNTER_BULK_COUNT:
                case NativeMethods.PERF_COUNTER_LARGE_QUEUELEN_TYPE:
                case NativeMethods.PERF_COUNTER_MULTI_TIMER:
                case NativeMethods.PERF_COUNTER_MULTI_TIMER_INV:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = newSample.TimeStamp;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = oldSample.TimeStamp;
                    if (newCounterType == NativeMethods.PERF_COUNTER_MULTI_TIMER || newCounterType == NativeMethods.PERF_COUNTER_MULTI_TIMER_INV)
                    {
                        //  this is to make PDH work like PERFMON for
                        //  this counter type
                        newPdhValue.FirstValue *= (uint)newSample.CounterFrequency;
                        if (oldSample.CounterFrequency != 0)
                            oldPdhValue.FirstValue *= (uint)oldSample.CounterFrequency;
                    }

                    if ((newCounterType & NativeMethods.PERF_MULTI_COUNTER) == NativeMethods.PERF_MULTI_COUNTER)
                    {
                        newPdhValue.MultiCount = (int)newSample.BaseValue;
                        oldPdhValue.MultiCount = (int)oldSample.BaseValue;
                    }

                    break;
                //
                //  These counters do not use any time reference
                //
                case NativeMethods.PERF_COUNTER_RAWCOUNT:
                case NativeMethods.PERF_COUNTER_RAWCOUNT_HEX:
                case NativeMethods.PERF_COUNTER_DELTA:
                case NativeMethods.PERF_COUNTER_LARGE_RAWCOUNT:
                case NativeMethods.PERF_COUNTER_LARGE_RAWCOUNT_HEX:
                case NativeMethods.PERF_COUNTER_LARGE_DELTA:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = 0;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = 0;
                    break;
                //
                //  These counters use the 100 Ns time base in thier calculation
                //
                case NativeMethods.PERF_100NSEC_TIMER:
                case NativeMethods.PERF_100NSEC_TIMER_INV:
                case NativeMethods.PERF_100NSEC_MULTI_TIMER:
                case NativeMethods.PERF_100NSEC_MULTI_TIMER_INV:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = newSample.TimeStamp100NSec;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = oldSample.TimeStamp100NSec;
                    if ((newCounterType & NativeMethods.PERF_MULTI_COUNTER) == NativeMethods.PERF_MULTI_COUNTER)
                    {
                        newPdhValue.MultiCount = (int)newSample.BaseValue;
                        oldPdhValue.MultiCount = (int)oldSample.BaseValue;
                    }
                    break;
                //
                //  These counters use two data points
                //
                case NativeMethods.PERF_SAMPLE_FRACTION:
                case NativeMethods.PERF_RAW_FRACTION:
                case NativeMethods.PERF_LARGE_RAW_FRACTION:
                case NativeMethods.PERF_PRECISION_SYSTEM_TIMER:
                case NativeMethods.PERF_PRECISION_100NS_TIMER:
                case NativeMethods.PERF_PRECISION_OBJECT_TIMER:
                case NativeMethods.PERF_AVERAGE_TIMER:
                case NativeMethods.PERF_AVERAGE_BULK:
                    newPdhValue.FirstValue = newSample.RawValue;
                    newPdhValue.SecondValue = newSample.BaseValue;

                    oldPdhValue.FirstValue = oldSample.RawValue;
                    oldPdhValue.SecondValue = oldSample.BaseValue;
                    break;

                default:
                    // an unidentified counter was returned so
                    newPdhValue.FirstValue = 0;
                    newPdhValue.SecondValue = 0;

                    oldPdhValue.FirstValue = 0;
                    oldPdhValue.SecondValue = 0;
                    break;
            }
        }

        static void LoadPerfCounterDll()
        {
            if (_perfCounterDllLoaded)
                return;

            if (SafeNativeMethods.LoadLibrary("perfcounter.dll") == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _perfCounterDllLoaded = true;
        }
    }
}