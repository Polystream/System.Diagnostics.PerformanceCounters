using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Microsoft.Win32
{
    [SecurityCritical]
    public abstract class SafeHandleZeroOrMinusOneIsInvalid : SafeHandle
    {
        protected SafeHandleZeroOrMinusOneIsInvalid(bool ownsHandle)
            : base(IntPtr.Zero, ownsHandle)
        {
        }
        
        unsafe public override bool IsInvalid
        {
            [SecurityCritical] get
            {
                if (handle.ToPointer() != null)
                    return handle == new IntPtr(-1);
                
                return true;
            }
        }
    }
}