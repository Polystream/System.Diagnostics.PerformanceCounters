using System;
using System.Security;

namespace Microsoft.Win32
{
    [SecurityCritical]
    internal sealed class SafeFileMappingHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [SecurityCritical]
        internal SafeFileMappingHandle()
            : base(true)
        {
        }

        [SecurityCritical]
        internal SafeFileMappingHandle(IntPtr handle, bool ownsHandle)
            : base(ownsHandle)
        {
            SetHandle(handle);
        }

        [SecurityCritical]
        protected override bool ReleaseHandle() =>
            NativeMethods.CloseHandle(handle);
    }
}