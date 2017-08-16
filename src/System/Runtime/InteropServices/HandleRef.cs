namespace System.Runtime.InteropServices
{
    [StructLayout(LayoutKind.Sequential)]
    [ComVisible(true)]
    public struct HandleRef
    {
        internal object _wrapper;
        internal IntPtr _handle;
        public HandleRef(object wrapper, IntPtr handle)
        {
            _wrapper = wrapper;
            _handle = handle;
        }

        public object Wrapper =>
            _wrapper;

        public IntPtr Handle =>
            _handle;

        public static explicit operator IntPtr(HandleRef value) =>
            value._handle;

        public static IntPtr ToIntPtr(HandleRef value) =>
            value._handle;
    }
}