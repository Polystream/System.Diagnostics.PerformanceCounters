using System.Security;

namespace Microsoft.Win32
{
    [SecurityCritical]
    internal sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeLibraryHandle()
            : base(true)
        {
        }

        [SecurityCritical]
        protected override bool ReleaseHandle() =>
            UnsafeNativeMethods.FreeLibrary(handle);
    }
}