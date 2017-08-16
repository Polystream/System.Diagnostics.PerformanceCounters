using System;
using System.Runtime.InteropServices;

namespace Microsoft.Win32
{
    internal static class UnsafeNativeMethods
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool FreeLibrary(IntPtr hModule);
    }
}
