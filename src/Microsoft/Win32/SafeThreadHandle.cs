using System;
using System.Security;

namespace Microsoft.Win32
{
    [SecurityCritical]
    internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeThreadHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        [SecurityCritical]
        protected override bool ReleaseHandle() =>
            NativeMethods.CloseHandle(handle);
    }
}