using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace System.Diagnostics
{
    internal static class SharedUtils
    {
        internal const int NonNtEnvironment = 3;
        internal const int NtEnvironment = 2;
        internal const int UnknownEnvironment = 0;

        internal const int W2KEnvironment = 1;

        internal static Win32Exception CreateSafeWin32Exception() =>
            CreateSafeWin32Exception(0);

        internal static Win32Exception CreateSafeWin32Exception(int error)
        {
            if (error == 0)
                return new Win32Exception();

            var exception = new Win32Exception(error);

            return exception;
        }

        internal static void EnterMutex(string name, ref Mutex mutex)
        {
            var mutexName = @"Global\" + name;
            EnterMutexWithoutGlobal(mutexName, ref mutex);
        }

        internal static void EnterMutexWithoutGlobal(string mutexName, ref Mutex mutex)
        {
            var mutexIn = new Mutex(false, mutexName, out bool _);
            SafeWaitForMutex(mutexIn, ref mutex);
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        static bool SafeWaitForMutex(Mutex mutexIn, ref Mutex mutexOut)
        {
            while (SafeWaitForMutexOnce(mutexIn, ref mutexOut))
            {
                if (mutexOut != null)
                    return true;

                Thread.Sleep(0);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool SafeWaitForMutexOnce(Mutex mutexIn, ref Mutex mutexOut)
        {
            bool flag;

            try
            {
            }
            finally
            {
                //Thread.BeginCriticalRegion();
                //Thread.BeginThreadAffinity();
                switch (WaitForSingleObjectDontCallThis(mutexIn.GetSafeWaitHandle(), 500))
                {
                    case 0:
                    case 0x80:
                        mutexOut = mutexIn;
                        flag = true;
                        break;

                    case 0x102:
                        flag = true;
                        break;

                    default:
                        flag = false;
                        break;
                }

                if (mutexOut == null)
                {
                    //Thread.EndThreadAffinity();
                    //Thread.EndCriticalRegion();
                }
            }

            return flag;
        }

        [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true, ExactSpelling = true)]
        static extern int WaitForSingleObjectDontCallThis(SafeWaitHandle handle, int timeout);
    }
}