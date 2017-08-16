using System;
using System.Runtime.InteropServices;

namespace Microsoft.Win32
{
    internal sealed class SafeFileMapViewHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeFileMapViewHandle()
            : base(true)
        {
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        internal static extern SafeFileMapViewHandle MapViewOfFile(SafeFileMappingHandle hFileMappingObject, int dwDesiredAccess, int dwFileOffsetHigh, int dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);
        protected override bool ReleaseHandle() =>
            UnmapViewOfFile(handle);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool UnmapViewOfFile(IntPtr handle);
    }
}