using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
// ReSharper disable InconsistentNaming

namespace Microsoft.Win32
{
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MEMORY_BASIC_INFORMATION
        {
            internal IntPtr BaseAddress;
            internal IntPtr AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr RegionSize;
            internal uint State;
            internal uint Protect;
            internal uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class PDH_FMT_COUNTERVALUE
        {
            public int CStatus = 0;
            public double data = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class PDH_RAW_COUNTER
        {
            public int CStatus = 0;
            public long TimeStamp = 0;
            public long FirstValue = 0;
            public long SecondValue = 0;
            public int MultiCount = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class PERF_COUNTER_DEFINITION
        {
            public int ByteLength = 0;
            public int CounterNameTitleIndex = 0;

            // this one is kind of weird. It is defined as in SDK:
            // #ifdef _WIN64
            //  DWORD           CounterNameTitle;
            // #else
            //  LPWSTR          CounterNameTitle;
            // #endif
            // so we can't use IntPtr here.

            public int CounterNameTitlePtr = 0;
            public int CounterHelpTitleIndex = 0;
            public int CounterHelpTitlePtr = 0;
            public int DefaultScale = 0;
            public int DetailLevel = 0;
            public int CounterType = 0;
            public int CounterSize = 0;
            public int CounterOffset = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class PERF_DATA_BLOCK
        {
            public int Signature1 = 0;
            public int Signature2 = 0;
            public int LittleEndian = 0;
            public int Version = 0;
            public int Revision = 0;
            public int TotalByteLength = 0;
            public int HeaderLength = 0;
            public int NumObjectTypes = 0;
            public int DefaultObject = 0;
            public SYSTEMTIME SystemTime = null;
            public int pad1 = 0;  // Need to pad the struct to get quadword alignment for the 'long' after SystemTime
            public long PerfTime = 0;
            public long PerfFreq = 0;
            public long PerfTime100nSec = 0;
            public int SystemNameLength = 0;
            public int SystemNameOffset = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class PERF_INSTANCE_DEFINITION
        {
            public int ByteLength = 0;
            public int ParentObjectTitleIndex = 0;
            public int ParentObjectInstance = 0;
            public int UniqueID = 0;
            public int NameOffset = 0;
            public int NameLength = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class PERF_OBJECT_TYPE
        {
            public int TotalByteLength = 0;
            public int DefinitionLength = 0;
            public int HeaderLength = 0;
            public int ObjectNameTitleIndex = 0;
            public int ObjectNameTitlePtr = 0;
            public int ObjectHelpTitleIndex = 0;
            public int ObjectHelpTitlePtr = 0;
            public int DetailLevel = 0;
            public int NumCounters = 0;
            public int DefaultCounter = 0;
            public int NumInstances = 0;
            public int CodePage = 0;
            public long PerfTime = 0;
            public long PerfFreq = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class SECURITY_ATTRIBUTES
        {
            public int nLength = 12;
            public SafeLocalMemHandle lpSecurityDescriptor = new SafeLocalMemHandle(IntPtr.Zero, false);
            public bool bInheritHandle = false;
        }

        internal sealed class SafeLocalMemHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal SafeLocalMemHandle()
                : base(true)
            {
            }

            internal SafeLocalMemHandle(IntPtr existingHandle, bool ownsHandle)
                : base(ownsHandle)
            {
                SetHandle(existingHandle);
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string StringSecurityDescriptor, int StringSDRevision, out SafeLocalMemHandle pSecurityDescriptor, IntPtr SecurityDescriptorSize);
            [DllImport("kernel32.dll")]
            static extern IntPtr LocalFree(IntPtr hMem);
            protected override bool ReleaseHandle() =>
                LocalFree(handle) == IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class SYSTEMTIME
        {
            public short wYear;
            public short wMonth;
            public short wDayOfWeek;
            public short wDay;
            public short wHour;
            public short wMinute;
            public short wSecond;
            public short wMilliseconds;

            public override string ToString()
            {
                return "[SYSTEMTIME: "
                       + wDay.ToString(CultureInfo.CurrentCulture) + "/" + wMonth.ToString(CultureInfo.CurrentCulture) + "/" + wYear.ToString(CultureInfo.CurrentCulture)
                       + " " + wHour.ToString(CultureInfo.CurrentCulture) + ":" + wMinute.ToString(CultureInfo.CurrentCulture) + ":" + wSecond.ToString(CultureInfo.CurrentCulture)
                       + "]";
            }
        }

        public const int ERROR_ACCESS_DENIED = 5;

        public const int ERROR_BUSY = 170;

        public const int ERROR_FILE_NOT_FOUND = 2;

        public const int ERROR_INVALID_HANDLE = 6;

        public const int ERROR_INVALID_PARAMETER = 0x57;

        public const int ERROR_LOCK_FAILED = 0xa7;

        public const int ERROR_NOT_READY = 0x15;

        public const int ERROR_SUCCESS = 0;

        public const int FILE_MAP_READ = 4;

        public const int FILE_MAP_WRITE = 2;

        public const int PAGE_READWRITE = 4;

        public const int PDH_CALC_NEGATIVE_DENOMINATOR = -2147481642;

        public const int PDH_CALC_NEGATIVE_VALUE = -2147481640;
        public const uint PDH_FMT_DOUBLE = 0x200;
        public const uint PDH_FMT_NOCAP100 = 0x8000;
        public const uint PDH_FMT_NOSCALE = 0x1000;
        public const int PDH_NO_DATA = -2147481643;
        public const int PERF_100NSEC_MULTI_TIMER = 0x22510500;
        public const int PERF_100NSEC_MULTI_TIMER_INV = 0x23510500;
        public const int PERF_100NSEC_TIMER = 0x20510500;
        public const int PERF_100NSEC_TIMER_INV = 0x21510500;
        public const int PERF_AVERAGE_BASE = 0x40030402;
        public const int PERF_AVERAGE_BULK = 0x40020500;
        public const int PERF_AVERAGE_TIMER = 0x30020400;
        public const int PERF_COUNTER_100NS_QUEUELEN_TYPE = 0x550500;
        public const int PERF_COUNTER_BASE = 0x30000;
        public const int PERF_COUNTER_BULK_COUNT = 0x10410500;
        public const int PERF_COUNTER_COUNTER = 0x10410400;
        public const int PERF_COUNTER_DELTA = 0x400400;
        public const int PERF_COUNTER_ELAPSED = 0x40000;
        public const int PERF_COUNTER_FRACTION = 0x20000;
        public const int PERF_COUNTER_HISTOGRAM = 0x60000;
        public const int PERF_COUNTER_LARGE_DELTA = 0x400500;
        public const int PERF_COUNTER_LARGE_QUEUELEN_TYPE = 0x450500;
        public const int PERF_COUNTER_LARGE_RAWCOUNT = 0x10100;
        public const int PERF_COUNTER_LARGE_RAWCOUNT_HEX = 0x100;
        public const int PERF_COUNTER_MULTI_BASE = 0x42030500;
        public const int PERF_COUNTER_MULTI_TIMER = 0x22410500;
        public const int PERF_COUNTER_MULTI_TIMER_INV = 0x23410500;
        public const int PERF_COUNTER_NODATA = 0x40000200;
        public const int PERF_COUNTER_OBJ_TIME_QUEUELEN_TYPE = 0x650500;
        public const int PERF_COUNTER_PRECISION = 0x70000;
        public const int PERF_COUNTER_QUEUELEN = 0x50000;
        public const int PERF_COUNTER_QUEUELEN_TYPE = 0x450400;
        public const int PERF_COUNTER_RATE = 0x10000;
        public const int PERF_COUNTER_RAWCOUNT = 0x10000;
        public const int PERF_COUNTER_RAWCOUNT_HEX = 0;
        public const int PERF_COUNTER_TEXT = 0xb00;
        public const int PERF_COUNTER_TIMER = 0x20410500;
        public const int PERF_COUNTER_TIMER_INV = 0x21410500;
        public const int PERF_COUNTER_VALUE = 0;
        public const int PERF_DELTA_BASE = 0x800000;
        public const int PERF_DELTA_COUNTER = 0x400000;
        public const int PERF_DETAIL_ADVANCED = 200;
        public const int PERF_DETAIL_EXPERT = 300;
        public const int PERF_DETAIL_NOVICE = 100;
        public const int PERF_DETAIL_WIZARD = 400;
        public const int PERF_DISPLAY_NO_SUFFIX = 0;
        public const int PERF_DISPLAY_NOSHOW = 0x40000000;
        public const int PERF_DISPLAY_PER_SEC = 0x10000000;
        public const int PERF_DISPLAY_PERCENT = 0x20000000;
        public const int PERF_DISPLAY_SECONDS = 0x30000000;
        public const int PERF_ELAPSED_TIME = 0x30240500;
        public const int PERF_INVERSE_COUNTER = 0x1000000;
        public const int PERF_LARGE_RAW_BASE = 0x40030500;
        public const int PERF_LARGE_RAW_FRACTION = 0x20020500;
        public const int PERF_MULTI_COUNTER = 0x2000000;
        public const int PERF_NO_INSTANCES = -1;
        public const int PERF_NO_UNIQUE_ID = -1;
        public const int PERF_NUMBER_DEC_1000 = 0x20000;
        public const int PERF_NUMBER_DECIMAL = 0x10000;
        public const int PERF_NUMBER_HEX = 0;
        public const int PERF_OBJ_TIME_TIME = 0x20610500;
        public const int PERF_OBJ_TIME_TIMER = 0x20610500;
        public const int PERF_OBJECT_TIMER = 0x200000;
        public const int PERF_PRECISION_100NS_TIMER = 0x20570500;
        public const int PERF_PRECISION_OBJECT_TIMER = 0x20670500;
        public const int PERF_PRECISION_SYSTEM_TIMER = 0x20470500;
        public const int PERF_RAW_BASE = 0x40030403;
        public const int PERF_RAW_FRACTION = 0x20020400;
        public const int PERF_SAMPLE_BASE = 0x40030401;
        public const int PERF_SAMPLE_COUNTER = 0x410400;
        public const int PERF_SAMPLE_FRACTION = 0x20c20400;
        public const int PERF_SIZE_DWORD = 0;
        public const int PERF_SIZE_LARGE = 0x100;
        public const int PERF_SIZE_VARIABLE_LEN = 0x300;
        public const int PERF_SIZE_ZERO = 0x200;
        public const int PERF_TEXT_ASCII = 0x10000;
        public const int PERF_TEXT_UNICODE = 0;
        public const int PERF_TIMER_100NS = 0x100000;
        public const int PERF_TIMER_TICK = 0;
        public const int PERF_TYPE_COUNTER = 0x400;
        public const int PERF_TYPE_NUMBER = 0;
        public const int PERF_TYPE_TEXT = 0x800;

        public const int PERF_TYPE_ZERO = 0xc00;
        public const int PROCESS_QUERY_INFORMATION = 0x400;
        public const int RPC_S_CALL_FAILED = 0x6be;
        public const int RPC_S_SERVER_UNAVAILABLE = 0x6ba;

        internal const int SDDL_REVISION_1 = 1;

        public const int SYNCHRONIZE = 0x100000;

        public const int WAIT_TIMEOUT = 0x102;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern SafeFileMappingHandle CreateFileMapping(IntPtr hFile, SECURITY_ATTRIBUTES lpFileMappingAttributes, int flProtect, int dwMaximumSizeHigh, int dwMaximumSizeLow, string lpName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool DuplicateHandle(HandleRef hSourceProcessHandle, SafeHandle hSourceHandle, HandleRef hTargetProcess, out SafeFileHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool DuplicateHandle(HandleRef hSourceProcessHandle, SafeHandle hSourceHandle, HandleRef hTargetProcess, out SafeWaitHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern int GetSystemDirectory([Out] StringBuilder sb, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        public static extern int GetCurrentProcessId();

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern SafeFileMappingHandle OpenFileMapping(int dwDesiredAccess, bool bInheritHandle, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualQuery(SafeFileMapViewHandle address, ref MEMORY_BASIC_INFORMATION buffer, IntPtr sizeOfBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}