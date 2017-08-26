using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PerformanceCounters;
using SafeProcessHandle = Microsoft.Win32.SafeProcessHandle;

namespace System.Diagnostics.Windows
{
    internal sealed class SharedPerformanceCounter : CustomPerformanceCounter
    {
        internal class ProcessWaitHandle : WaitHandle
        {
            internal ProcessWaitHandle(SafeProcessHandle processHandle)
            {
                if (!NativeMethods.DuplicateHandle(new HandleRef(this, NativeMethods.GetCurrentProcess()), processHandle, new HandleRef(this, NativeMethods.GetCurrentProcess()), out SafeWaitHandle targetHandle, 0, false, 2))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                this.SetSafeWaitHandle(targetHandle);
            }
        }

        class FileMapping
        {
            SafeFileMappingHandle _fileMappingHandle;
            internal int FileMappingSize;

            SafeFileMapViewHandle _fileViewAddress;
            //The version of the file mapping name is independent from the
            //assembly version.

            public FileMapping(string fileMappingName, int fileMappingSize, int initialOffset)
            {
                Initialize(fileMappingName, fileMappingSize, initialOffset);
            }

            internal IntPtr FileViewAddress
            {
                get
                {
                    if (_fileViewAddress.IsInvalid)
                        throw new InvalidOperationException(SR.GetString("Cannot access shared memory."));

                    return _fileViewAddress.DangerousGetHandle();
                }
            }

            unsafe void Initialize(string fileMappingName, int fileMappingSize, int initialOffset)
            {
                var mappingName = fileMappingName;

                NativeMethods.SafeLocalMemHandle securityDescriptorPointer = null;
                try
                {
                    // The sddl string consists of these parts:
                    // D:           it's a DACL
                    // (A;          this is an allow ACE
                    // OICI;        object inherit and container inherit
                    // FRFWGRGW;;;  allow file read, file write, generic read and generic write
                    // AU)          granted to Authenticated Users
                    // ;S-1-5-33)   the same permission granted to AU is also granted to restricted services
                    var sddlString = "D:(A;OICI;FRFWGRGW;;;AU)(A;OICI;FRFWGRGW;;;S-1-5-33)";

                    if (!NativeMethods.SafeLocalMemHandle.ConvertStringSecurityDescriptorToSecurityDescriptor(sddlString, NativeMethods.SDDL_REVISION_1,
                                                                                                              out securityDescriptorPointer, IntPtr.Zero))
                        throw new InvalidOperationException(SR.GetString("Cannot initialize security descriptor initialized."));

                    var securityAttributes = new NativeMethods.SECURITY_ATTRIBUTES()
                    {
                        lpSecurityDescriptor = securityDescriptorPointer,
                        bInheritHandle = false
                    };

                    //
                    //
                    // Here we call CreateFileMapping to create the memory mapped file.  When CreateFileMapping fails
                    // with ERROR_ACCESS_DENIED, we know the file mapping has been created and we then open it with OpenFileMapping.
                    //
                    // There is chance of a race condition between CreateFileMapping and OpenFileMapping; The memory mapped file
                    // may actually be closed in between these two calls.  When this happens, OpenFileMapping returns ERROR_FILE_NOT_FOUND.
                    // In this case, we need to loop back and retry creating the memory mapped file.
                    //
                    // This loop will timeout in approximately 1.4 minutes.  An InvalidOperationException is thrown in the timeout case. 
                    //
                    //
                    var waitRetries = 14; //((2^13)-1)*10ms == approximately 1.4mins
                    var waitSleep = 0;
                    var created = false;
                    while (!created && waitRetries > 0)
                    {
                        _fileMappingHandle = NativeMethods.CreateFileMapping((IntPtr)(-1), securityAttributes,
                                                                            NativeMethods.PAGE_READWRITE, 0, fileMappingSize, mappingName);

                        if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_ACCESS_DENIED || !_fileMappingHandle.IsInvalid)
                            created = true;
                        else
                        {
                            // Invalidate the old safehandle before we get rid of it.  This prevents it from trying to finalize
                            _fileMappingHandle.SetHandleAsInvalid();
                            _fileMappingHandle = NativeMethods.OpenFileMapping(NativeMethods.FILE_MAP_WRITE, false, mappingName);

                            if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_FILE_NOT_FOUND || !_fileMappingHandle.IsInvalid)
                                created = true;
                            else
                            {
                                --waitRetries;
                                if (waitSleep == 0)
                                    waitSleep = 10;
                                else
                                {
                                    Thread.Sleep(waitSleep);
                                    waitSleep *= 2;
                                }
                            }
                        }
                    }

                    if (_fileMappingHandle.IsInvalid)
                        throw new InvalidOperationException(SR.GetString("Cannot create file mapping."));

                    _fileViewAddress = SafeFileMapViewHandle.MapViewOfFile(_fileMappingHandle, NativeMethods.FILE_MAP_WRITE, 0, 0, UIntPtr.Zero);
                    if (_fileViewAddress.IsInvalid)
                        throw new InvalidOperationException(SR.GetString("Cannot map view of file."));

                    // figure out what size the share memory really is.
                    var meminfo = new NativeMethods.MEMORY_BASIC_INFORMATION();
                    if (NativeMethods.VirtualQuery(_fileViewAddress, ref meminfo, (IntPtr)sizeof(NativeMethods.MEMORY_BASIC_INFORMATION)) == IntPtr.Zero)
                        throw new InvalidOperationException(SR.GetString("Cannot calculate the size of the file view."));

                    FileMappingSize = (int)meminfo.RegionSize;
                }
                finally
                {
                    securityDescriptorPointer?.Dispose();
                }

                SafeNativeMethods.InterlockedCompareExchange(_fileViewAddress.DangerousGetHandle(), initialOffset, 0);
            }
        }

        // <WARNING>
        // The final tmpPadding field is needed to make the size of this structure 8-byte aligned.  This is
        // necessary on IA64.
        // </WARNING>
        // Note that in V1.0 and v1.1 there was no explicit padding defined on any of these structs.  That means that 
        // sizeof(CategoryEntry) or Marshal.SizeOf(typeof(CategoryEntry)) returned 4 bytes less before Whidbey, 
        // and the int we use as IsConsistent could actually overlap the InstanceEntry SpinLock.  

        [StructLayout(LayoutKind.Sequential)]
        struct CategoryEntry
        {
            public int SpinLock;
            public int CategoryNameHashCode;
            public int CategoryNameOffset;
            public int FirstInstanceOffset;
            public int NextCategoryOffset;
            public int IsConsistent; // this was 4 bytes of padding in v1.0/v1.1
        }

        [StructLayout(LayoutKind.Sequential)]
        struct InstanceEntry
        {
            // ReSharper disable once FieldCanBeMadeReadOnly.Local
            public int SpinLock;
            public int InstanceNameHashCode;
            public int InstanceNameOffset;
            public int RefCount;
            public int FirstCounterOffset;
            public int NextInstanceOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CounterEntry
        {
            // ReSharper disable once FieldCanBeMadeReadOnly.Local
            public int SpinLock;
            public int CounterNameHashCode;
            public int CounterNameOffset;
            public int LifetimeOffset; // this was 4 bytes of padding in v1.0/v1.1
            public long Value;
            public int NextCounterOffset;
            readonly int padding2;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CounterEntryMisaligned
        {
            // ReSharper disable once FieldCanBeMadeReadOnly.Local
            int SpinLock;

            readonly int CounterNameHashCode;
            readonly int CounterNameOffset;
            readonly int LifetimeOffset; // this was 4 bytes of padding in v1.0/v1.1
            public int Value_lo;
            public int Value_hi;
            readonly int NextCounterOffset;

            readonly int padding2; // The compiler adds this only if there is an int64 in the struct - 
            // ie only for CounterEntry.  It really needs to be here.  
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessLifetimeEntry
        {
            public int LifetimeType;
            public int ProcessId;
            public long StartupTime;
        }

        class CategoryData
        {
            public List<string> CounterNames;
            public bool EnableReuse;
            public FileMapping FileMapping;
            public string FileMappingName;
            public string MutexName;
            public bool UseUniqueSharedMemory;
        }

        const int MaxSpinCount = 5000;
        internal const int DefaultCountersFileMappingSize = 524288;
        internal const int MaxCountersFileMappingSize = 33554432;
        internal const int MinCountersFileMappingSize = 32768;
        internal const int InstanceNameMaxLength = 127;
        internal const int InstanceNameSlotSize = 256;
        internal const string SingleInstanceName = "systemdiagnosticssharedsingleinstance";
        internal const string DefaultFileMappingName = "netfxcustomperfcounters.1.0";
        const long InstanceLifetimeSweepWindow = 30 * 10000000; //ticks
        internal static readonly int SingleInstanceHashCode = GetWstrHashCode(SingleInstanceName);
        static readonly Dictionary<string, CategoryData> CategoryDataTable = new Dictionary<string, CategoryData>(StringComparer.Ordinal);
        static readonly int CategoryEntrySize = Marshal.SizeOf<CategoryEntry>();
        static readonly int InstanceEntrySize = Marshal.SizeOf<InstanceEntry>();
        static readonly int CounterEntrySize = Marshal.SizeOf<CounterEntry>();
        static readonly int ProcessLifetimeEntrySize = Marshal.SizeOf<ProcessLifetimeEntry>();

        static long _lastInstanceLifetimeSweepTick;
        static volatile ProcessData _procData;
        long _baseAddress;

        readonly CategoryData _categoryData;
        readonly string _categoryName;
        readonly int _categoryNameHashCode;
        unsafe readonly CounterEntry* _counterEntryPointer;

        // InitialOffset is the offset in our global shared memory where we put the first CategoryEntry.  It needs to be 4 because in 
        // v1.0 and v1.1 we used IntPtr.Size.  That creates potential side-by-side issues on 64 bit machines using WOW64.
        // A v1.0 app running on WOW64 will assume the InitialOffset is 4.  A true 64 bit app on the same machine will assume
        // the initial offset is 8. 
        // However, using an offset of 4 means that our CounterEntry.Value is potentially misaligned.  This is why we have SetValue 
        // and other methods which split CounterEntry.Value into two ints.  With separate shared memory blocks per
        // category, we can fix this and always use an inital offset of 8. 
        internal int InitialOffset = 4;

        int _thisInstanceOffset = -1;

        internal SharedPerformanceCounter(string catName, string counterName, string instanceName)
            :
            this(catName, counterName, instanceName, PerformanceCounterInstanceLifetime.Global)
        {
        }

        unsafe internal SharedPerformanceCounter(string catName, string counterName, string instanceName, PerformanceCounterInstanceLifetime lifetime)
        {
            _categoryName = catName;
            _categoryNameHashCode = GetWstrHashCode(_categoryName);

            _categoryData = GetCategoryData();

            // Check that the instance name isn't too long if we're using the new shared memory.  
            // We allocate InstanceNameSlotSize bytes in the shared memory
            if (_categoryData.UseUniqueSharedMemory)
            {
                if (instanceName != null && instanceName.Length > InstanceNameMaxLength)
                    throw new InvalidOperationException(SR.GetString("Instance names used for writing to custom counters must be 127 characters or less."));
            }
            else
            {
                if (lifetime != PerformanceCounterInstanceLifetime.Global)
                    throw new InvalidOperationException(SR.GetString("PerformanceCounterInstanceLifetime.Process is not valid in the global shared memory.  If your performance counter category was created with an older version of the Framework, it uses the global shared memory.  Either use PerformanceCounterInstanceLifetime.Global, or if applications running on older versions of the Framework do not need to write to your category, delete and recreate it."));
            }

            if (counterName != null && instanceName != null)
            {
                if (!_categoryData.CounterNames.Contains(counterName))
                    Debug.Assert(false, "Counter " + counterName + " does not exist in category " + catName);
                else
                    _counterEntryPointer = GetCounter(counterName, instanceName, _categoryData.EnableReuse, lifetime);
            }
        }

        static ProcessData ProcessData
        {
            get
            {
                if (_procData == null)
                {
                    var pid = NativeMethods.GetCurrentProcessId();
                    long startTime = -1;

                    // Though we have asserted the required CAS permissions above, we may
                    // still fail to query the process information if the user does not 
                    // have the necessary process access rights or privileges.
                    // This might be the case if the current process was started by a 
                    // different user (primary token) than the current user
                    // (impersonation token) that has less privilege/ACL rights.
                    using (var procHandle = SafeProcessHandle.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, pid))
                    {
                        if (!procHandle.IsInvalid)
                            NativeMethods.GetProcessTimes(procHandle, out startTime, out var _, out var _, out var _);
                    }
                    _procData = new ProcessData(pid, startTime);
                }
                return _procData;
            }
        }

        FileMapping FileView => _categoryData.FileMapping;

        unsafe internal override long Value
        {
            get
            {
                if (_counterEntryPointer == null)
                    return 0;

                return GetValue(_counterEntryPointer);
            }

            set
            {
                if (_counterEntryPointer == null)
                    return;

                SetValue(_counterEntryPointer, value);
            }
        }

        unsafe int CalculateAndAllocateMemory(int totalSize, out int alignmentAdjustment)
        {
            int newOffset;
            int oldOffset;

            Debug.Assert(!_categoryData.UseUniqueSharedMemory, "We should never be calling CalculateAndAllocateMemory in the unique shared memory");

            do
            {
                oldOffset = *((int*)_baseAddress);
                // we need to verify the oldOffset before we start using it.  Otherwise someone could change
                // it to something bogus and we would write outside of the shared memory. 
                ResolveOffset(oldOffset, 0);

                newOffset = CalculateMemory(oldOffset, totalSize, out alignmentAdjustment);

                // In the default shared mem we need to make sure that the end address is also aligned.  This is because 
                // in v1.1/v1.0 we just assumed that the next free offset was always properly aligned. 
                var endAddressMod8 = (int)(_baseAddress + newOffset) & 0x7;
                var endAlignmentAdjustment = (8 - endAddressMod8) & 0x7;
                newOffset += endAlignmentAdjustment;
            } while (SafeNativeMethods.InterlockedCompareExchange((IntPtr)_baseAddress, newOffset, oldOffset) != oldOffset);

            return oldOffset;
        }

        int CalculateMemory(int oldOffset, int totalSize, out int alignmentAdjustment)
        {
            var newOffset = CalculateMemoryNoBoundsCheck(oldOffset, totalSize, out alignmentAdjustment);

            if (newOffset > FileView.FileMappingSize || newOffset < 0)
                throw new InvalidOperationException(SR.GetString("Custom counters file view is out of memory."));

            return newOffset;
        }

        int CalculateMemoryNoBoundsCheck(int oldOffset, int totalSize, out int alignmentAdjustment)
        {
            var currentTotalSize = totalSize;

            //Thread.MemoryBarrier();

            // make sure the start address is 8 byte aligned
            var startAddressMod8 = (int)(_baseAddress + oldOffset) & 0x7;
            alignmentAdjustment = (8 - startAddressMod8) & 0x7;
            currentTotalSize = currentTotalSize + alignmentAdjustment;

            var newOffset = oldOffset + currentTotalSize;

            return newOffset;
        }

        unsafe int CreateCategory(CategoryEntry* lastCategoryPointer,
                                  int instanceNameHashCode, string instanceName,
                                  PerformanceCounterInstanceLifetime lifetime)
        {
            int categoryNameLength;
            int instanceNameLength;
            int alignmentAdjustment;
            int freeMemoryOffset;
            var newOffset = 0;
            int totalSize;

            categoryNameLength = (_categoryName.Length + 1) * 2;
            totalSize = CategoryEntrySize + InstanceEntrySize + CounterEntrySize * _categoryData.CounterNames.Count + categoryNameLength;
            for (var i = 0; i < _categoryData.CounterNames.Count; i++)
                totalSize += (_categoryData.CounterNames[i].Length + 1) * 2;

            if (_categoryData.UseUniqueSharedMemory)
            {
                instanceNameLength = InstanceNameSlotSize;
                totalSize += ProcessLifetimeEntrySize + instanceNameLength;

                // If we're in a separate shared memory, we need to do a two stage update of the free memory pointer.
                // First we calculate our alignment adjustment and where the new free offset is.  Then we 
                // write the new structs and data.  The last two operations are to link the new structs into the 
                // existing ones and update the next free offset.  Our process could get killed in between those two,
                // leaving the memory in an inconsistent state.  We use the "IsConsistent" flag to help determine 
                // when that has happened. 
                freeMemoryOffset = *((int*)_baseAddress);
                newOffset = CalculateMemory(freeMemoryOffset, totalSize, out alignmentAdjustment);

                if (freeMemoryOffset == InitialOffset)
                    lastCategoryPointer->IsConsistent = 0;
            }
            else
            {
                instanceNameLength = (instanceName.Length + 1) * 2;
                totalSize += instanceNameLength;
                freeMemoryOffset = CalculateAndAllocateMemory(totalSize, out alignmentAdjustment);
            }

            var nextPtr = ResolveOffset(freeMemoryOffset, totalSize + alignmentAdjustment);

            CategoryEntry* newCategoryEntryPointer;
            InstanceEntry* newInstanceEntryPointer;
            // We need to decide where to put the padding returned in alignmentAdjustment.  There are several things that
            // need to be aligned.  First, we need to align each struct on a 4 byte boundary so we can use interlocked 
            // operations on the int Spinlock field.  Second, we need to align the CounterEntry on an 8 byte boundary so that
            // on 64 bit platforms we can use interlocked operations on the Value field.  alignmentAdjustment guarantees 8 byte
            // alignemnt, so we use that for both.  If we're creating the very first category, however, we can't move that 
            // CategoryEntry.  In this case we put the alignmentAdjustment before the InstanceEntry. 
            if (freeMemoryOffset == InitialOffset)
            {
                newCategoryEntryPointer = (CategoryEntry*)nextPtr;
                nextPtr += CategoryEntrySize + alignmentAdjustment;
                newInstanceEntryPointer = (InstanceEntry*)nextPtr;
            }
            else
            {
                nextPtr += alignmentAdjustment;
                newCategoryEntryPointer = (CategoryEntry*)nextPtr;
                nextPtr += CategoryEntrySize;
                newInstanceEntryPointer = (InstanceEntry*)nextPtr;
            }
            nextPtr += InstanceEntrySize;

            // create the first CounterEntry and reserve space for all of the rest.  We won't 
            // finish creating them until the end
            var newCounterEntryPointer = (CounterEntry*)nextPtr;
            nextPtr += CounterEntrySize * _categoryData.CounterNames.Count;

            if (_categoryData.UseUniqueSharedMemory)
            {
                var newLifetimeEntry = (ProcessLifetimeEntry*)nextPtr;
                nextPtr += ProcessLifetimeEntrySize;

                newCounterEntryPointer->LifetimeOffset = (int)((long)newLifetimeEntry - _baseAddress);
                PopulateLifetimeEntry(newLifetimeEntry, lifetime);
            }

            newCategoryEntryPointer->CategoryNameHashCode = _categoryNameHashCode;
            newCategoryEntryPointer->NextCategoryOffset = 0;
            newCategoryEntryPointer->FirstInstanceOffset = (int)((long)newInstanceEntryPointer - _baseAddress);
            newCategoryEntryPointer->CategoryNameOffset = (int)(nextPtr - _baseAddress);
            SafeMarshalCopy(_categoryName, (IntPtr)nextPtr);
            nextPtr += categoryNameLength;

            newInstanceEntryPointer->InstanceNameHashCode = instanceNameHashCode;
            newInstanceEntryPointer->NextInstanceOffset = 0;
            newInstanceEntryPointer->FirstCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);
            newInstanceEntryPointer->RefCount = 1;
            newInstanceEntryPointer->InstanceNameOffset = (int)(nextPtr - _baseAddress);
            SafeMarshalCopy(instanceName, (IntPtr)nextPtr);
            nextPtr += instanceNameLength;

            var counterName = _categoryData.CounterNames[0];
            newCounterEntryPointer->CounterNameHashCode = GetWstrHashCode(counterName);
            SetValue(newCounterEntryPointer, 0);
            newCounterEntryPointer->CounterNameOffset = (int)(nextPtr - _baseAddress);
            SafeMarshalCopy(counterName, (IntPtr)nextPtr);
            nextPtr += (counterName.Length + 1) * 2;

            for (var i = 1; i < _categoryData.CounterNames.Count; i++)
            {
                var previousCounterEntryPointer = newCounterEntryPointer;
                counterName = _categoryData.CounterNames[i];

                newCounterEntryPointer++;
                newCounterEntryPointer->CounterNameHashCode = GetWstrHashCode(counterName);
                SetValue(newCounterEntryPointer, 0);
                newCounterEntryPointer->CounterNameOffset = (int)(nextPtr - _baseAddress);
                SafeMarshalCopy(counterName, (IntPtr)nextPtr);

                nextPtr += (counterName.Length + 1) * 2;
                previousCounterEntryPointer->NextCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);
            }

            Debug.Assert(nextPtr - _baseAddress == freeMemoryOffset + totalSize + alignmentAdjustment, "We should have used all of the space we requested at this point");

            var offset = (int)((long)newCategoryEntryPointer - _baseAddress);
            lastCategoryPointer->IsConsistent = 0;
            // If not the first category node, link it.
            if (offset != InitialOffset)
                lastCategoryPointer->NextCategoryOffset = offset;

            if (_categoryData.UseUniqueSharedMemory)
            {
                *((int*)_baseAddress) = newOffset;
                lastCategoryPointer->IsConsistent = 1;
            }
            return offset;
        }

        unsafe int CreateInstance(CategoryEntry* categoryPointer,
                                  int instanceNameHashCode, string instanceName,
                                  PerformanceCounterInstanceLifetime lifetime)
        {
            int instanceNameLength;
            var totalSize = InstanceEntrySize + CounterEntrySize * _categoryData.CounterNames.Count;
            int alignmentAdjustment;
            int freeMemoryOffset;
            var newOffset = 0;

            if (_categoryData.UseUniqueSharedMemory)
            {
                instanceNameLength = InstanceNameSlotSize;
                totalSize += ProcessLifetimeEntrySize + instanceNameLength;

                // If we're in a separate shared memory, we need to do a two stage update of the free memory pointer.
                // First we calculate our alignment adjustment and where the new free offset is.  Then we 
                // write the new structs and data.  The last two operations are to link the new structs into the 
                // existing ones and update the next free offset.  Our process could get killed in between those two,
                // leaving the memory in an inconsistent state.  We use the "IsConsistent" flag to help determine 
                // when that has happened. 
                freeMemoryOffset = *((int*)_baseAddress);
                newOffset = CalculateMemory(freeMemoryOffset, totalSize, out alignmentAdjustment);
            }
            else
            {
                instanceNameLength = (instanceName.Length + 1) * 2;
                totalSize += instanceNameLength;

                // add in the counter names for the global shared mem.
                for (var i = 0; i < _categoryData.CounterNames.Count; i++)
                    totalSize += (_categoryData.CounterNames[i].Length + 1) * 2;

                freeMemoryOffset = CalculateAndAllocateMemory(totalSize, out alignmentAdjustment);
            }

            freeMemoryOffset += alignmentAdjustment;
            var nextPtr = ResolveOffset(freeMemoryOffset, totalSize); // don't add alignmentAdjustment since it's already
            // been added to freeMemoryOffset 

            var newInstanceEntryPointer = (InstanceEntry*)nextPtr;
            nextPtr += InstanceEntrySize;

            // create the first CounterEntry and reserve space for all of the rest.  We won't 
            // finish creating them until the end
            var newCounterEntryPointer = (CounterEntry*)nextPtr;
            nextPtr += CounterEntrySize * _categoryData.CounterNames.Count;

            if (_categoryData.UseUniqueSharedMemory)
            {
                var newLifetimeEntry = (ProcessLifetimeEntry*)nextPtr;
                nextPtr += ProcessLifetimeEntrySize;

                newCounterEntryPointer->LifetimeOffset = (int)((long)newLifetimeEntry - _baseAddress);
                PopulateLifetimeEntry(newLifetimeEntry, lifetime);
            }

            // set up the InstanceEntry
            newInstanceEntryPointer->InstanceNameHashCode = instanceNameHashCode;
            newInstanceEntryPointer->NextInstanceOffset = 0;
            newInstanceEntryPointer->FirstCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);
            newInstanceEntryPointer->RefCount = 1;
            newInstanceEntryPointer->InstanceNameOffset = (int)(nextPtr - _baseAddress);
            SafeMarshalCopy(instanceName, (IntPtr)nextPtr);

            nextPtr += instanceNameLength;

            if (_categoryData.UseUniqueSharedMemory)
            {
                // in the unique shared mem we'll assume that the CounterEntries of the first instance
                // are all created.  Then we can just refer to the old counter name rather than copying in a new one.
                var firstInstanceInCategoryPointer = (InstanceEntry*)ResolveOffset(categoryPointer->FirstInstanceOffset, InstanceEntrySize);
                var firstCounterInCategoryPointer = (CounterEntry*)ResolveOffset(firstInstanceInCategoryPointer->FirstCounterOffset, CounterEntrySize);
                newCounterEntryPointer->CounterNameHashCode = firstCounterInCategoryPointer->CounterNameHashCode;
                SetValue(newCounterEntryPointer, 0);
                newCounterEntryPointer->CounterNameOffset = firstCounterInCategoryPointer->CounterNameOffset;

                // now create the rest of the CounterEntrys
                for (var i = 1; i < _categoryData.CounterNames.Count; i++)
                {
                    var previousCounterEntryPointer = newCounterEntryPointer;

                    newCounterEntryPointer++;
                    Debug.Assert(firstCounterInCategoryPointer->NextCounterOffset != 0, "The unique shared memory should have all of its counters created by the time we hit CreateInstance");
                    firstCounterInCategoryPointer = (CounterEntry*)ResolveOffset(firstCounterInCategoryPointer->NextCounterOffset, CounterEntrySize);
                    newCounterEntryPointer->CounterNameHashCode = firstCounterInCategoryPointer->CounterNameHashCode;
                    SetValue(newCounterEntryPointer, 0);
                    newCounterEntryPointer->CounterNameOffset = firstCounterInCategoryPointer->CounterNameOffset;

                    previousCounterEntryPointer->NextCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);
                }
            }
            else
            {
                // now create the rest of the CounterEntrys
                CounterEntry* previousCounterEntryPointer = null;
                for (var i = 0; i < _categoryData.CounterNames.Count; i++)
                {
                    var counterName = _categoryData.CounterNames[i];
                    newCounterEntryPointer->CounterNameHashCode = GetWstrHashCode(counterName);
                    newCounterEntryPointer->CounterNameOffset = (int)(nextPtr - _baseAddress);
                    SafeMarshalCopy(counterName, (IntPtr)nextPtr);
                    nextPtr += (counterName.Length + 1) * 2;

                    SetValue(newCounterEntryPointer, 0);

                    if (i != 0)
                        previousCounterEntryPointer->NextCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);

                    previousCounterEntryPointer = newCounterEntryPointer;
                    newCounterEntryPointer++;
                }
            }

            Debug.Assert(nextPtr - _baseAddress == freeMemoryOffset + totalSize, "We should have used all of the space we requested at this point");

            var offset = (int)((long)newInstanceEntryPointer - _baseAddress);
            categoryPointer->IsConsistent = 0;

            // prepend the new instance rather than append, helps with perf of hooking up subsequent counters 
            newInstanceEntryPointer->NextInstanceOffset = categoryPointer->FirstInstanceOffset;
            categoryPointer->FirstInstanceOffset = offset;

            if (_categoryData.UseUniqueSharedMemory)
            {
                *((int*)_baseAddress) = newOffset;
                categoryPointer->IsConsistent = 1;
            }

            return freeMemoryOffset;
        }

        unsafe int CreateCounter(CounterEntry* lastCounterPointer,
                                 int counterNameHashCode, string counterName)
        {
            var counterNameLength = (counterName.Length + 1) * 2;
            var totalSize = sizeof(CounterEntry) + counterNameLength;
            int freeMemoryOffset;

            Debug.Assert(!_categoryData.UseUniqueSharedMemory, "We should never be calling CreateCounter in the unique shared memory");
            freeMemoryOffset = CalculateAndAllocateMemory(totalSize, out int alignmentAdjustment);

            freeMemoryOffset += alignmentAdjustment;

            var nextPtr = ResolveOffset(freeMemoryOffset, totalSize);
            var newCounterEntryPointer = (CounterEntry*)nextPtr;
            nextPtr += sizeof(CounterEntry);

            newCounterEntryPointer->CounterNameOffset = (int)(nextPtr - _baseAddress);
            newCounterEntryPointer->CounterNameHashCode = counterNameHashCode;
            newCounterEntryPointer->NextCounterOffset = 0;
            SetValue(newCounterEntryPointer, 0);
            SafeMarshalCopy(counterName, (IntPtr)nextPtr);

            Debug.Assert(nextPtr + counterNameLength - _baseAddress == freeMemoryOffset + totalSize, "We should have used all of the space we requested at this point");

            lastCounterPointer->NextCounterOffset = (int)((long)newCounterEntryPointer - _baseAddress);
            return freeMemoryOffset;
        }

        unsafe static void PopulateLifetimeEntry(ProcessLifetimeEntry* lifetimeEntry, PerformanceCounterInstanceLifetime lifetime)
        {
            if (lifetime == PerformanceCounterInstanceLifetime.Process)
            {
                lifetimeEntry->LifetimeType = (int)PerformanceCounterInstanceLifetime.Process;
                lifetimeEntry->ProcessId = ProcessData.ProcessId;
                lifetimeEntry->StartupTime = ProcessData.StartupTime;
            }
            else
            {
                lifetimeEntry->ProcessId = 0;
                lifetimeEntry->StartupTime = 0;
            }
        }

        unsafe static void WaitAndEnterCriticalSection(int* spinLockPointer, out bool taken)
        {
            WaitForCriticalSection(spinLockPointer);

            // Note - we are taking a lock here, but it probably isn't 
            // worthwhile to use Thread.BeginCriticalRegion & EndCriticalRegion.
            // These only really help the CLR escalate from a thread abort
            // to an appdomain unload, under the assumption that you may be
            // editing shared state within the appdomain.  Here you are editing
            // shared state, but it is shared across processes.  Unloading the
            // appdomain isn't exactly helping.  The only thing that would help
            // would be if the CLR tells the host to ensure all allocations 
            // have a higher chance of succeeding within this critical region,
            // but of course that's only a probabilisitic statement.

            // Must be able to assign to the out param.
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
            }
            finally
            {
                var r = Interlocked.CompareExchange(ref *spinLockPointer, 1, 0);
                taken = r == 0;
            }
        }

        unsafe static void WaitForCriticalSection(int* spinLockPointer)
        {
            var spinCount = MaxSpinCount;
            for (; spinCount > 0 && *spinLockPointer != 0; spinCount--)
            {
                // We suspect there are scenarios where the finalizer thread
                // will call this method.  The finalizer thread runs with 
                // a higher priority than the other code.  Using SpinWait
                // isn't sufficient, since it only spins, but doesn't yield
                // to any lower-priority threads.  Call Thread.Sleep(1).
                if (*spinLockPointer != 0)
                    Thread.Sleep(1);
            }

            // if the lock still isn't free, most likely there's a deadlock caused by a process
            // getting killed while it held the lock.  We'll just free the lock
            if (spinCount == 0 && *spinLockPointer != 0)
                *spinLockPointer = 0;
        }

        unsafe static void ExitCriticalSection(int* spinLockPointer)
        {
            *spinLockPointer = 0;
        }

        // WARNING WARNING WARNING WARNING WARNING WARNING WARNING WARNING
        // This hashcode function is identical to the one in SharedPerformanceCounter.cpp.  If
        // you change one without changing the other, perfcounters will break.
        internal static int GetWstrHashCode(string wstr)
        {
            uint hash = 5381;
            for (uint i = 0; i < wstr.Length; i++)
                hash = ((hash << 5) + hash) ^ wstr[(int)i];

            return (int)hash;
        }

        // Calculate the length of a string in the shared memory.  If we reach the end of the shared memory 
        // before we see a null terminator, we throw.
        unsafe int GetStringLength(char* startChar)
        {
            var currentChar = startChar;
            var endAddress = (ulong)(_baseAddress + FileView.FileMappingSize);

            while ((ulong)currentChar < endAddress - 2)
            {
                if (*currentChar == 0)
                    return (int)(currentChar - startChar);

                currentChar++;
            }

            throw new InvalidOperationException(SR.GetString("Cannot continue the current operation, the performance counters memory mapping has been corrupted."));
        }

        // Compare a managed string to a string located at a given offset.  If we walk past the end of the 
        // shared memory, we throw. 
        unsafe bool StringEquals(string stringA, int offset)
        {
            var currentChar = (char*)ResolveOffset(offset, 0);
            var endAddress = (ulong)(_baseAddress + FileView.FileMappingSize);

            int i;
            for (i = 0; i < stringA.Length; i++)
            {
                if ((ulong)(currentChar + i) > endAddress - 2)
                    throw new InvalidOperationException(SR.GetString("Cannot continue the current operation, the performance counters memory mapping has been corrupted."));

                if (stringA[i] != currentChar[i])
                    return false;
            }

            // now check for the null termination. 
            if ((ulong)(currentChar + i) > endAddress - 2)
                throw new InvalidOperationException(SR.GetString("Cannot continue the current operation, the performance counters memory mapping has been corrupted."));

            return currentChar[i] == 0;
        }

        unsafe CategoryData GetCategoryData()
        {
            if (!CategoryDataTable.TryGetValue(_categoryName, out var data))
            {
                lock (CategoryDataTable)
                {
                    if (!CategoryDataTable.TryGetValue(_categoryName, out data))
                    {
                        data = new CategoryData
                        {
                            FileMappingName = DefaultFileMappingName,
                            MutexName = _categoryName
                        };

                        RegistryKey categoryKey = null;
                        try
                        {
                            categoryKey = Registry.LocalMachine.OpenSubKey(PerformanceCounterLib.ServicePath + "\\" + _categoryName + "\\Performance");

                            // first read the options
                            var optionsObject = categoryKey.GetValue("CategoryOptions");
                            if (optionsObject != null)
                            {
                                var options = (int)optionsObject;
                                data.EnableReuse = ((PerformanceCounterCategoryOptions)options & PerformanceCounterCategoryOptions.EnableReuse) != 0;

                                if (((PerformanceCounterCategoryOptions)options & PerformanceCounterCategoryOptions.UseUniqueSharedMemory) != 0)
                                {
                                    data.UseUniqueSharedMemory = true;
                                    InitialOffset = 8;
                                    data.FileMappingName = DefaultFileMappingName + _categoryName;
                                }
                            }

                            int fileMappingSize;
                            var fileMappingSizeObject = categoryKey.GetValue("FileMappingSize");
                            if (fileMappingSizeObject != null && data.UseUniqueSharedMemory)
                            {
                                // we only use this reg value in the unique shared memory case. 
                                fileMappingSize = (int)fileMappingSizeObject;
                                if (fileMappingSize < MinCountersFileMappingSize)
                                    fileMappingSize = MinCountersFileMappingSize;

                                if (fileMappingSize > MaxCountersFileMappingSize)
                                    fileMappingSize = MaxCountersFileMappingSize;
                            }
                            else
                            {
                                fileMappingSize = GetFileMappingSizeFromConfig();
                                if (data.UseUniqueSharedMemory)
                                    fileMappingSize = fileMappingSize >> 2; // if we have a custom filemapping, only make it 25% as large. 
                            }

                            // now read the counter names
                            var counterNamesObject = categoryKey.GetValue("Counter Names");

                            if (counterNamesObject is byte[] counterNamesBytes)
                            {
                                var names = new List<string>();
                                fixed (byte* counterNamesPtr = counterNamesBytes)
                                {
                                    var start = 0;
                                    for (var i = 0; i < counterNamesBytes.Length - 1; i += 2)
                                    {
                                        if (counterNamesBytes[i] == 0 && counterNamesBytes[i + 1] == 0 && start != i)
                                        {
                                            var counter = new string((char*)counterNamesPtr, start, i - start);
                                            names.Add(counter.ToLowerInvariant());
                                            start = i + 2;
                                        }
                                    }
                                }
                                data.CounterNames = names;
                            }
                            else
                            {
                                var counterNames = (string[])counterNamesObject;
                                for (var i = 0; i < counterNames.Length; i++)
                                    counterNames[i] = counterNames[i].ToLowerInvariant();

                                data.CounterNames = new List<string>(counterNames);
                            }

                            // figure out the shared memory name
                            //if (SharedUtils.CurrentEnvironment == SharedUtils.W2kEnvironment)
                            {
                                data.FileMappingName = "Global\\" + data.FileMappingName;
                                data.MutexName = "Global\\" + _categoryName;
                            }

                            data.FileMapping = new FileMapping(data.FileMappingName, fileMappingSize, InitialOffset);
                            CategoryDataTable[_categoryName] = data;
                        }
                        finally
                        {
                            categoryKey?.Dispose();
                        }
                    }
                }
            }

            _baseAddress = (long)data.FileMapping.FileViewAddress;

            if (data.UseUniqueSharedMemory)
                InitialOffset = 8;

            return data;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int GetFileMappingSizeFromConfig()
        {
            return MaxCountersFileMappingSize;
        }

        static void RemoveCategoryData(string categoryName)
        {
            lock (CategoryDataTable)
                CategoryDataTable.Remove(categoryName);
        }

        unsafe CounterEntry* GetCounter(string counterName, string instanceName, bool enableReuse, PerformanceCounterInstanceLifetime lifetime)
        {
            var counterNameHashCode = GetWstrHashCode(counterName);
            int instanceNameHashCode;
            if (!string.IsNullOrEmpty(instanceName))
                instanceNameHashCode = GetWstrHashCode(instanceName);
            else
            {
                instanceNameHashCode = SingleInstanceHashCode;
                instanceName = SingleInstanceName;
            }

            Mutex mutex = null;
            CounterEntry* counterPointer = null;
            InstanceEntry* instancePointer = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutexWithoutGlobal(_categoryData.MutexName, ref mutex);
                CategoryEntry* categoryPointer;
                bool counterFound;
                while (!FindCategory(&categoryPointer))
                {
                    // don't bother locking again if we're using a separate shared memory.  
                    bool sectionEntered;
                    if (_categoryData.UseUniqueSharedMemory)
                        sectionEntered = true;
                    else
                        WaitAndEnterCriticalSection(&categoryPointer->SpinLock, out sectionEntered);

                    if (sectionEntered)
                    {
                        int newCategoryOffset;
                        try
                        {
                            newCategoryOffset = CreateCategory(categoryPointer, instanceNameHashCode, instanceName, lifetime);
                        }
                        finally
                        {
                            if (!_categoryData.UseUniqueSharedMemory)
                                ExitCriticalSection(&categoryPointer->SpinLock);
                        }

                        categoryPointer = (CategoryEntry*)ResolveOffset(newCategoryOffset, CategoryEntrySize);
                        instancePointer = (InstanceEntry*)ResolveOffset(categoryPointer->FirstInstanceOffset, InstanceEntrySize);
                        counterFound = FindCounter(counterNameHashCode, counterName, instancePointer, &counterPointer);
                        Debug.Assert(counterFound, "All counters should be created, so we should always find the counter");
                        return counterPointer;
                    }
                }

                while (!FindInstance(instanceNameHashCode, instanceName, categoryPointer, &instancePointer, true, lifetime, out bool foundFreeInstance))
                {
                    var lockInstancePointer = instancePointer;

                    // don't bother locking again if we're using a separate shared memory.  
                    bool sectionEntered;
                    if (_categoryData.UseUniqueSharedMemory)
                        sectionEntered = true;
                    else
                        WaitAndEnterCriticalSection(&lockInstancePointer->SpinLock, out sectionEntered);

                    if (sectionEntered)
                    {
                        try
                        {
                            var reused = false;

                            if (enableReuse && foundFreeInstance)
                            {
                                reused = TryReuseInstance(instanceNameHashCode, instanceName, categoryPointer, &instancePointer, lifetime, lockInstancePointer);
                                // at this point we might have reused an instance that came from v1.1/v1.0.  We can't assume it will have the counter
                                // we're looking for. 
                            }

                            if (!reused)
                            {
                                var newInstanceOffset = CreateInstance(categoryPointer, instanceNameHashCode, instanceName, lifetime);
                                instancePointer = (InstanceEntry*)ResolveOffset(newInstanceOffset, InstanceEntrySize);

                                counterFound = FindCounter(counterNameHashCode, counterName, instancePointer, &counterPointer);
                                Debug.Assert(counterFound, "All counters should be created, so we should always find the counter");
                                return counterPointer;
                            }
                        }
                        finally
                        {
                            if (!_categoryData.UseUniqueSharedMemory)
                                ExitCriticalSection(&lockInstancePointer->SpinLock);
                        }
                    }
                }

                if (_categoryData.UseUniqueSharedMemory)
                {
                    counterFound = FindCounter(counterNameHashCode, counterName, instancePointer, &counterPointer);
                    Debug.Assert(counterFound, "All counters should be created, so we should always find the counter");
                    return counterPointer;
                }
                else
                {
                    while (!FindCounter(counterNameHashCode, counterName, instancePointer, &counterPointer))
                    {
                        WaitAndEnterCriticalSection(&counterPointer->SpinLock, out bool sectionEntered);

                        if (sectionEntered)
                        {
                            try
                            {
                                var newCounterOffset = CreateCounter(counterPointer, counterNameHashCode, counterName);
                                return (CounterEntry*)ResolveOffset(newCounterOffset, CounterEntrySize);
                            }
                            finally
                            {
                                ExitCriticalSection(&counterPointer->SpinLock);
                            }
                        }
                    }

                    return counterPointer;
                }
            }
            finally
            {
                // cache this instance for reuse 
                try
                {
                    if (counterPointer != null && instancePointer != null)
                        _thisInstanceOffset = ResolveAddress((long)instancePointer, InstanceEntrySize);
                }
                catch (InvalidOperationException)
                {
                    _thisInstanceOffset = -1;
                }

                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        //
        // FindCategory -
        //
        // * when the function returns true the returnCategoryPointerReference is set to the CategoryEntry
        //   that matches 'categoryNameHashCode' and 'categoryName'
        //
        // * when the function returns false the returnCategoryPointerReference is set to the last CategoryEntry
        //   in the linked list
        // 
        unsafe bool FindCategory(CategoryEntry** returnCategoryPointerReference)
        {
            var firstCategoryPointer = (CategoryEntry*)ResolveOffset(InitialOffset, CategoryEntrySize);
            var currentCategoryPointer = firstCategoryPointer;

            for (;;)
            {
                if (currentCategoryPointer->IsConsistent == 0)
                    Verify(currentCategoryPointer);

                if (currentCategoryPointer->CategoryNameHashCode == _categoryNameHashCode)
                {
                    if (StringEquals(_categoryName, currentCategoryPointer->CategoryNameOffset))
                    {
                        *returnCategoryPointerReference = currentCategoryPointer;
                        return true;
                    }
                }

                var previousCategoryPointer = currentCategoryPointer;
                if (currentCategoryPointer->NextCategoryOffset != 0)
                    currentCategoryPointer = (CategoryEntry*)ResolveOffset(currentCategoryPointer->NextCategoryOffset, CategoryEntrySize);
                else
                {
                    *returnCategoryPointerReference = previousCategoryPointer;
                    return false;
                }
            }
        }

        unsafe bool FindCounter(int counterNameHashCode, string counterName, InstanceEntry* instancePointer, CounterEntry** returnCounterPointerReference)
        {
            var currentCounterPointer = (CounterEntry*)ResolveOffset(instancePointer->FirstCounterOffset, CounterEntrySize);
            for (;;)
            {
                if (currentCounterPointer->CounterNameHashCode == counterNameHashCode)
                {
                    if (StringEquals(counterName, currentCounterPointer->CounterNameOffset))
                    {
                        *returnCounterPointerReference = currentCounterPointer;
                        return true;
                    }
                }

                var previousCounterPointer = currentCounterPointer;
                if (currentCounterPointer->NextCounterOffset != 0)
                    currentCounterPointer = (CounterEntry*)ResolveOffset(currentCounterPointer->NextCounterOffset, CounterEntrySize);
                else
                {
                    *returnCounterPointerReference = previousCounterPointer;
                    return false;
                }
            }
        }

        unsafe bool FindInstance(int instanceNameHashCode, string instanceName,
                                 CategoryEntry* categoryPointer, InstanceEntry** returnInstancePointerReference,
                                 bool activateUnusedInstances, PerformanceCounterInstanceLifetime lifetime,
                                 out bool foundFreeInstance)
        {
            var currentInstancePointer = (InstanceEntry*)ResolveOffset(categoryPointer->FirstInstanceOffset, InstanceEntrySize);
            foundFreeInstance = false;
            // Look at the first instance to determine if this is single or multi instance. 
            if (currentInstancePointer->InstanceNameHashCode == SingleInstanceHashCode)
            {
                if (StringEquals(SingleInstanceName, currentInstancePointer->InstanceNameOffset))
                {
                    if (instanceName != SingleInstanceName)
                        throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as single-instance.  Performance counters in this category can only be created without instance names.", _categoryName));
                }
                else
                {
                    if (instanceName == SingleInstanceName)
                        throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as multi-instance.  Performance counters in this category can only be created with instance names.", _categoryName));
                }
            }
            else
            {
                if (instanceName == SingleInstanceName)
                    throw new InvalidOperationException(SR.GetString("Category '{0}' is marked as multi-instance.  Performance counters in this category can only be created with instance names.", _categoryName));
            }

            //
            // 1st pass find exact matching!
            // 
            // We don't need to aggressively claim unused instances. For performance, we would proactively 
            // verify lifetime of instances if activateUnusedInstances is specified and certain time 
            // has elapsed since last sweep or we are running out of shared memory.  
            var verifyLifeTime = activateUnusedInstances;
            if (activateUnusedInstances)
            {
                var totalSize = InstanceEntrySize + ProcessLifetimeEntrySize + InstanceNameSlotSize + CounterEntrySize * _categoryData.CounterNames.Count;
                var freeMemoryOffset = *((int*)_baseAddress);
                var newOffset = CalculateMemoryNoBoundsCheck(freeMemoryOffset, totalSize, out int _);

                if (!(newOffset > FileView.FileMappingSize || newOffset < 0))
                {
                    var tickDelta = DateTime.Now.Ticks - Volatile.Read(ref _lastInstanceLifetimeSweepTick);
                    if (tickDelta < InstanceLifetimeSweepWindow)
                        verifyLifeTime = false;
                }
            }

            try
            {
                for (;;)
                {
                    var verifiedLifetimeOfThisInstance = false;
                    if (verifyLifeTime && currentInstancePointer->RefCount != 0)
                    {
                        verifiedLifetimeOfThisInstance = true;
                        VerifyLifetime(currentInstancePointer);
                    }

                    if (currentInstancePointer->InstanceNameHashCode == instanceNameHashCode)
                    {
                        if (StringEquals(instanceName, currentInstancePointer->InstanceNameOffset))
                        {
                            // we found a matching instance. 
                            *returnInstancePointerReference = currentInstancePointer;

                            var firstCounter = (CounterEntry*)ResolveOffset(currentInstancePointer->FirstCounterOffset, CounterEntrySize);
                            ProcessLifetimeEntry* lifetimeEntry;
                            if (_categoryData.UseUniqueSharedMemory)
                                lifetimeEntry = (ProcessLifetimeEntry*)ResolveOffset(firstCounter->LifetimeOffset, ProcessLifetimeEntrySize);
                            else
                                lifetimeEntry = null;

                            // ensure that we have verified the lifetime of the matched instance
                            if (!verifiedLifetimeOfThisInstance && currentInstancePointer->RefCount != 0)
                                VerifyLifetime(currentInstancePointer);

                            if (currentInstancePointer->RefCount != 0)
                            {
                                if (lifetimeEntry != null && lifetimeEntry->ProcessId != 0)
                                {
                                    if (lifetime != PerformanceCounterInstanceLifetime.Process)
                                        throw new InvalidOperationException(SR.GetString("An instance with a lifetime of Process can only be accessed from a PerformanceCounter with the InstanceLifetime set to PerformanceCounterInstanceLifetime.Process."));

                                    // make sure only one process is using this instance. 
                                    if (ProcessData.ProcessId != lifetimeEntry->ProcessId)
                                        throw new InvalidOperationException(SR.GetString("Instance '{0}' already exists with a lifetime of Process.  It cannot be recreated or reused until it has been removed or until the process using it has exited.", instanceName));

                                    // compare start time of the process, account for ACL issues in querying process information
                                    if (lifetimeEntry->StartupTime != -1 && ProcessData.StartupTime != -1)
                                    {
                                        if (ProcessData.StartupTime != lifetimeEntry->StartupTime)
                                            throw new InvalidOperationException(SR.GetString("Instance '{0}' already exists with a lifetime of Process.  It cannot be recreated or reused until it has been removed or until the process using it has exited.", instanceName));
                                    }
                                }
                                else
                                {
                                    if (lifetime == PerformanceCounterInstanceLifetime.Process)
                                        throw new InvalidOperationException(SR.GetString("An instance with a lifetime of Global can only be accessed from a PerformanceCounter with the InstanceLifetime set to PerformanceCounterInstanceLifetime.Global."));
                                }

                                return true;
                            }

                            if (activateUnusedInstances)
                            {
                                Mutex mutex = null;
                                //RuntimeHelpers.PrepareConstrainedRegions();
                                try
                                {
                                    SharedUtils.EnterMutexWithoutGlobal(_categoryData.MutexName, ref mutex);
                                    ClearCounterValues(currentInstancePointer);
                                    if (lifetimeEntry != null)
                                        PopulateLifetimeEntry(lifetimeEntry, lifetime);

                                    currentInstancePointer->RefCount = 1;
                                    return true;
                                }
                                finally
                                {
                                    if (mutex != null)
                                    {
                                        mutex.ReleaseMutex();
                                        mutex.Dispose();
                                    }
                                }
                            }
                            else
                                return false;
                        }
                    }

                    if (currentInstancePointer->RefCount == 0)
                        foundFreeInstance = true;

                    var previousInstancePointer = currentInstancePointer;
                    if (currentInstancePointer->NextInstanceOffset != 0)
                        currentInstancePointer = (InstanceEntry*)ResolveOffset(currentInstancePointer->NextInstanceOffset, InstanceEntrySize);
                    else
                    {
                        *returnInstancePointerReference = previousInstancePointer;
                        return false;
                    }
                }
            }
            finally
            {
                if (verifyLifeTime)
                    Volatile.Write(ref _lastInstanceLifetimeSweepTick, DateTime.Now.Ticks);
            }
        }

        unsafe bool TryReuseInstance(int instanceNameHashCode, string instanceName,
                                     CategoryEntry* categoryPointer, InstanceEntry** returnInstancePointerReference,
                                     PerformanceCounterInstanceLifetime lifetime,
                                     InstanceEntry* lockInstancePointer)
        {
            //
            // 2nd pass find a free instance slot
            // 
            var currentInstancePointer = (InstanceEntry*)ResolveOffset(categoryPointer->FirstInstanceOffset, InstanceEntrySize);
            for (;;)
            {
                if (currentInstancePointer->RefCount == 0)
                {
                    bool hasFit;
                    long instanceNamePtr; // we need cache this to avoid race conditions. 

                    if (_categoryData.UseUniqueSharedMemory)
                    {
                        instanceNamePtr = ResolveOffset(currentInstancePointer->InstanceNameOffset, InstanceNameSlotSize);
                        // In the separate shared memory case we should always have enough space for instances.  The
                        // name slot size is fixed. 
                        Debug.Assert((instanceName.Length + 1) * 2 <= InstanceNameSlotSize, "The instance name length should always fit in our slot size");
                        hasFit = true;
                    }
                    else
                    {
                        // we don't know the string length yet. 
                        instanceNamePtr = ResolveOffset(currentInstancePointer->InstanceNameOffset, 0);

                        // In the global shared memory, we require names to be exactly the same length in order
                        // to reuse them.  This way we don't end up leaking any space and we don't need to 
                        // depend on the layout of the memory to calculate the space we have. 
                        var length = GetStringLength((char*)instanceNamePtr);
                        hasFit = length == instanceName.Length;
                    }

                    var noSpinLock = lockInstancePointer == currentInstancePointer || _categoryData.UseUniqueSharedMemory;
                    // Instance name fit
                    if (hasFit)
                    {
                        // don't bother locking again if we're using a separate shared memory.  
                        bool sectionEntered;
                        if (noSpinLock)
                            sectionEntered = true;
                        else
                            WaitAndEnterCriticalSection(&currentInstancePointer->SpinLock, out sectionEntered);

                        if (sectionEntered)
                        {
                            try
                            {
                                // Make copy with zero-term
                                SafeMarshalCopy(instanceName, (IntPtr)instanceNamePtr);
                                currentInstancePointer->InstanceNameHashCode = instanceNameHashCode;

                                // return
                                *returnInstancePointerReference = currentInstancePointer;
                                // clear the counter values. 
                                ClearCounterValues(*returnInstancePointerReference);

                                if (_categoryData.UseUniqueSharedMemory)
                                {
                                    var counterPointer = (CounterEntry*)ResolveOffset(currentInstancePointer->FirstCounterOffset, CounterEntrySize);
                                    var lifetimeEntry = (ProcessLifetimeEntry*)ResolveOffset(counterPointer->LifetimeOffset, ProcessLifetimeEntrySize);
                                    PopulateLifetimeEntry(lifetimeEntry, lifetime);
                                }

                                (*returnInstancePointerReference)->RefCount = 1;
                                return true;
                            }
                            finally
                            {
                                if (!noSpinLock)
                                    ExitCriticalSection(&currentInstancePointer->SpinLock);
                            }
                        }
                    }
                }

                var previousInstancePointer = currentInstancePointer;
                if (currentInstancePointer->NextInstanceOffset != 0)
                    currentInstancePointer = (InstanceEntry*)ResolveOffset(currentInstancePointer->NextInstanceOffset, InstanceEntrySize);
                else
                {
                    *returnInstancePointerReference = previousInstancePointer;
                    return false;
                }
            }
        }

        unsafe void Verify(CategoryEntry* currentCategoryPointer)
        {
            if (!_categoryData.UseUniqueSharedMemory)
                return;

            Mutex mutex = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutexWithoutGlobal(_categoryData.MutexName, ref mutex);
                VerifyCategory(currentCategoryPointer);
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        unsafe void VerifyCategory(CategoryEntry* currentCategoryPointer)
        {
            var freeOffset = *((int*)_baseAddress);
            ResolveOffset(freeOffset, 0); // verify next free offset

            // begin by verifying the head node's offset
            var currentOffset = ResolveAddress((long)currentCategoryPointer, CategoryEntrySize);
            if (currentOffset >= freeOffset)
            {
                // zero out the bad head node entry
                currentCategoryPointer->SpinLock = 0;
                currentCategoryPointer->CategoryNameHashCode = 0;
                currentCategoryPointer->CategoryNameOffset = 0;
                currentCategoryPointer->FirstInstanceOffset = 0;
                currentCategoryPointer->NextCategoryOffset = 0;
                currentCategoryPointer->IsConsistent = 0;
                return;
            }

            if (currentCategoryPointer->NextCategoryOffset > freeOffset)
                currentCategoryPointer->NextCategoryOffset = 0;
            else if (currentCategoryPointer->NextCategoryOffset != 0)
                VerifyCategory((CategoryEntry*)ResolveOffset(currentCategoryPointer->NextCategoryOffset, CategoryEntrySize));

            if (currentCategoryPointer->FirstInstanceOffset != 0)
            {
                // In V3, we started prepending the new instances rather than appending (as in V2) for performance.
                // Check whether the recently added instance at the head of the list is committed. If not, rewire 
                // the head of the list to point to the next instance 
                if (currentCategoryPointer->FirstInstanceOffset > freeOffset)
                {
                    var currentInstancePointer = (InstanceEntry*)ResolveOffset(currentCategoryPointer->FirstInstanceOffset, InstanceEntrySize);
                    currentCategoryPointer->FirstInstanceOffset = currentInstancePointer->NextInstanceOffset;
                    if (currentCategoryPointer->FirstInstanceOffset > freeOffset)
                        currentCategoryPointer->FirstInstanceOffset = 0;
                }
                // 

                if (currentCategoryPointer->FirstInstanceOffset != 0)
                {
                    Debug.Assert(currentCategoryPointer->FirstInstanceOffset <= freeOffset, "The head of the list is inconsistent - possible mismatch of V2 & V3 instances?");
                    VerifyInstance((InstanceEntry*)ResolveOffset(currentCategoryPointer->FirstInstanceOffset, InstanceEntrySize));
                }
            }

            currentCategoryPointer->IsConsistent = 1;
        }

        unsafe void VerifyInstance(InstanceEntry* currentInstancePointer)
        {
            var freeOffset = *((int*)_baseAddress);
            ResolveOffset(freeOffset, 0); // verify next free offset

            if (currentInstancePointer->NextInstanceOffset > freeOffset)
                currentInstancePointer->NextInstanceOffset = 0;
            else if (currentInstancePointer->NextInstanceOffset != 0)
                VerifyInstance((InstanceEntry*)ResolveOffset(currentInstancePointer->NextInstanceOffset, InstanceEntrySize));
        }

        unsafe void VerifyLifetime(InstanceEntry* currentInstancePointer)
        {
            Debug.Assert(currentInstancePointer->RefCount != 0, "RefCount must be 1 for instances passed to VerifyLifetime");

            var counter = (CounterEntry*)ResolveOffset(currentInstancePointer->FirstCounterOffset, CounterEntrySize);
            if (counter->LifetimeOffset != 0)
            {
                var lifetime = (ProcessLifetimeEntry*)ResolveOffset(counter->LifetimeOffset, ProcessLifetimeEntrySize);
                if (lifetime->LifetimeType == (int)PerformanceCounterInstanceLifetime.Process)
                {
                    var pid = lifetime->ProcessId;
                    var startTime = lifetime->StartupTime;

                    if (pid != 0)
                    {
                        // Optimize for this process
                        if (pid == ProcessData.ProcessId)
                        {
                            if (ProcessData.StartupTime != -1 && startTime != -1 && ProcessData.StartupTime != startTime)
                            {
                                // Process id got recycled.  Reclaim this instance. 
                                currentInstancePointer->RefCount = 0;
                            }
                        }
                        else
                        {
                            using (var procHandle = SafeProcessHandle.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, pid))
                            {
                                var error = Marshal.GetLastWin32Error();
                                if (error == NativeMethods.ERROR_INVALID_PARAMETER && procHandle.IsInvalid)
                                {
                                    // The process is dead.  Reclaim this instance.  Note that we only clear the refcount here.  
                                    // If we tried to clear the pid and startup time as well, we would have a ---- where
                                    // we could clear the pid/startup time but not the refcount. 
                                    currentInstancePointer->RefCount = 0;
                                    return;
                                }

                                // Defer cleaning the instance when we had previously encountered errors in 
                                // recording process start time (i.e, when startTime == -1) until after the 
                                // process id is not valid (which will be caught in the if check above)
                                if (!procHandle.IsInvalid && startTime != -1)
                                {
                                    if (NativeMethods.GetProcessTimes(procHandle, out long processStartTime, out long temp, out temp, out temp))
                                    {
                                        if (processStartTime != startTime)
                                        {
                                            // The process is dead but a new one is using the same pid.  Reclaim this instance. 
                                            currentInstancePointer->RefCount = 0;
                                            return;
                                        }
                                    }
                                }
                            }

                            // Check to see if the process handle has been signaled by the kernel.  If this is the case then it's safe
                            // to reclaim the instance as the process is in the process of exiting.
                            using (var procHandle = SafeProcessHandle.OpenProcess(NativeMethods.SYNCHRONIZE, false, pid))
                            {
                                if (!procHandle.IsInvalid)
                                {
                                    using (var wh = new ProcessWaitHandle(procHandle))
                                    {
                                        if (wh.WaitOne(0))
                                        {
                                            // Process has exited
                                            currentInstancePointer->RefCount = 0;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        unsafe internal override long IncrementBy(long value)
        {
            if (_counterEntryPointer == null)
                return 0;

            var counterEntry = _counterEntryPointer;

            return AddToValue(counterEntry, value);
        }

        unsafe internal override long Increment()
        {
            if (_counterEntryPointer == null)
                return 0;

            return IncrementUnaligned(_counterEntryPointer);
        }

        unsafe internal override long Decrement()
        {
            if (_counterEntryPointer == null)
                return 0;

            return DecrementUnaligned(_counterEntryPointer);
        }

        internal static void RemoveAllInstances(string categoryName)
        {
            var spc = new SharedPerformanceCounter(categoryName, null, null);
            spc.RemoveAllInstances();
            RemoveCategoryData(categoryName);
        }

        unsafe void RemoveAllInstances()
        {
            CategoryEntry* categoryPointer;
            if (!FindCategory(&categoryPointer))
                return;

            var instancePointer = (InstanceEntry*)ResolveOffset(categoryPointer->FirstInstanceOffset, InstanceEntrySize);

            Mutex mutex = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutexWithoutGlobal(_categoryData.MutexName, ref mutex);
                for (;;)
                {
                    RemoveOneInstance(instancePointer, true);

                    if (instancePointer->NextInstanceOffset != 0)
                        instancePointer = (InstanceEntry*)ResolveOffset(instancePointer->NextInstanceOffset, InstanceEntrySize);
                    else
                        break;
                }
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        unsafe internal override void RemoveInstance(string instanceName, PerformanceCounterInstanceLifetime instanceLifetime)
        {
            if (string.IsNullOrEmpty(instanceName))
                return;

            var instanceNameHashCode = GetWstrHashCode(instanceName);

            CategoryEntry* categoryPointer;
            if (!FindCategory(&categoryPointer))
                return;

            InstanceEntry* instancePointer = null;
            var validatedCachedInstancePointer = false;

            Mutex mutex = null;
            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                SharedUtils.EnterMutexWithoutGlobal(_categoryData.MutexName, ref mutex);

                if (_thisInstanceOffset != -1)
                {
                    try
                    {
                        // validate whether the cached instance pointer is pointing at the right instance  
                        instancePointer = (InstanceEntry*)ResolveOffset(_thisInstanceOffset, InstanceEntrySize);
                        if (instancePointer->InstanceNameHashCode == instanceNameHashCode)
                        {
                            if (StringEquals(instanceName, instancePointer->InstanceNameOffset))
                            {
                                validatedCachedInstancePointer = true;

                                // this is probably overkill
                                var firstCounter = (CounterEntry*)ResolveOffset(instancePointer->FirstCounterOffset, CounterEntrySize);
                                if (_categoryData.UseUniqueSharedMemory)
                                {
                                    var lifetimeEntry = (ProcessLifetimeEntry*)ResolveOffset(firstCounter->LifetimeOffset, ProcessLifetimeEntrySize);
                                    if (lifetimeEntry->LifetimeType == (int)PerformanceCounterInstanceLifetime.Process
                                        && lifetimeEntry->ProcessId != 0)
                                    {
                                        validatedCachedInstancePointer &= instanceLifetime == PerformanceCounterInstanceLifetime.Process;
                                        validatedCachedInstancePointer &= ProcessData.ProcessId == lifetimeEntry->ProcessId;
                                        if (lifetimeEntry->StartupTime != -1 && ProcessData.StartupTime != -1)
                                            validatedCachedInstancePointer &= ProcessData.StartupTime == lifetimeEntry->StartupTime;
                                    }
                                    else
                                        validatedCachedInstancePointer &= instanceLifetime != PerformanceCounterInstanceLifetime.Process;
                                }
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        validatedCachedInstancePointer = false;
                    }
                    if (!validatedCachedInstancePointer)
                        _thisInstanceOffset = -1;
                }
                
                if (!validatedCachedInstancePointer && !FindInstance(instanceNameHashCode, instanceName, categoryPointer, &instancePointer, false, instanceLifetime, out var _))
                    return;

                if (instancePointer != null)
                    RemoveOneInstance(instancePointer, false);
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }

        unsafe void RemoveOneInstance(InstanceEntry* instancePointer, bool clearValue)
        {
            var sectionEntered = false;

            //RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                if (!_categoryData.UseUniqueSharedMemory)
                {
                    while (!sectionEntered)
                        WaitAndEnterCriticalSection(&instancePointer->SpinLock, out sectionEntered);
                }

                instancePointer->RefCount = 0;

                if (clearValue)
                    ClearCounterValues(instancePointer);
            }
            finally
            {
                if (sectionEntered)
                    ExitCriticalSection(&instancePointer->SpinLock);
            }
        }

        unsafe void ClearCounterValues(InstanceEntry* instancePointer)
        {
            //Clear counter instance values
            CounterEntry* currentCounterPointer = null;

            if (instancePointer->FirstCounterOffset != 0)
                currentCounterPointer = (CounterEntry*)ResolveOffset(instancePointer->FirstCounterOffset, CounterEntrySize);

            while (currentCounterPointer != null)
            {
                SetValue(currentCounterPointer, 0);

                if (currentCounterPointer->NextCounterOffset != 0)
                    currentCounterPointer = (CounterEntry*)ResolveOffset(currentCounterPointer->NextCounterOffset, CounterEntrySize);
                else
                    currentCounterPointer = null;
            }
        }

        unsafe static long AddToValue(CounterEntry* counterEntry, long addend)
        {
            // Called while holding a lock - shouldn't have to worry about
            // reading misaligned data & getting old vs. new parts of an Int64.
            if (IsMisaligned(counterEntry))
            {
                ulong newvalue;

                var entry = (CounterEntryMisaligned*)counterEntry;
                newvalue = (uint)entry->Value_hi;
                newvalue <<= 32;
                newvalue |= (uint)entry->Value_lo;

                newvalue = (ulong)((long)newvalue + addend);

                entry->Value_hi = (int)(newvalue >> 32);
                entry->Value_lo = (int)(newvalue & 0xffffffff);

                return (long)newvalue;
            }
            else
                return Interlocked.Add(ref counterEntry->Value, addend);
        }

        unsafe static long DecrementUnaligned(CounterEntry* counterEntry)
        {
            if (IsMisaligned(counterEntry))
                return AddToValue(counterEntry, -1);
            else
                return Interlocked.Decrement(ref counterEntry->Value);
        }

        unsafe static long GetValue(CounterEntry* counterEntry)
        {
            if (IsMisaligned(counterEntry))
            {
                ulong value;
                var entry = (CounterEntryMisaligned*)counterEntry;
                value = (uint)entry->Value_hi;
                value <<= 32;
                value |= (uint)entry->Value_lo;

                return (long)value;
            }
            else
                return counterEntry->Value;
        }

        unsafe static long IncrementUnaligned(CounterEntry* counterEntry)
        {
            if (IsMisaligned(counterEntry))
                return AddToValue(counterEntry, 1);
            else
                return Interlocked.Increment(ref counterEntry->Value);
        }

        unsafe static void SetValue(CounterEntry* counterEntry, long value)
        {
            if (IsMisaligned(counterEntry))
            {
                var entry = (CounterEntryMisaligned*)counterEntry;
                entry->Value_lo = (int)(value & 0xffffffff);
                entry->Value_hi = (int)(value >> 32);
            }
            else
                counterEntry->Value = value;
        }

        unsafe static bool IsMisaligned(CounterEntry* counterEntry)
        {
            return ((long)counterEntry & 0x7) != 0;
        }

        // ReSharper disable once UnusedParameter.Local
        long ResolveOffset(int offset, int sizeToRead)
        {
            //It is very important to check the integrity of the shared memory
            //everytime a new address is resolved.
            if (offset > FileView.FileMappingSize - sizeToRead || offset < 0)
                throw new InvalidOperationException(SR.GetString("Cannot continue the current operation, the performance counters memory mapping has been corrupted."));

            var address = _baseAddress + offset;

            return address;
        }

        // ReSharper disable once UnusedParameter.Local
        int ResolveAddress(long address, int sizeToRead)
        {
            var offset = (int)(address - _baseAddress);

            //It is very important to check the integrity of the shared memory
            //everytime a new address is resolved.
            if (offset > FileView.FileMappingSize - sizeToRead || offset < 0)
                throw new InvalidOperationException(SR.GetString("Cannot continue the current operation, the performance counters memory mapping has been corrupted."));

            return offset;
        }

        // SafeMarshalCopy always null terminates the char array
        // before copying it to native memory
        //
        static void SafeMarshalCopy(string str, IntPtr nativePointer)
        {
            // convert str to a char array and copy it to the unmanaged memory pointer
            var tmp = new char[str.Length + 1];
            str.CopyTo(0, tmp, 0, str.Length);
            tmp[str.Length] = '\0'; // make sure the char[] is null terminated
            Marshal.Copy(tmp, 0, nativePointer, tmp.Length);
        }
    }

    internal class ProcessData
    {
        public int ProcessId;
        public long StartupTime;
        public ProcessData(int pid, long startTime)
        {
            ProcessId = pid;
            StartupTime = startTime;
        }
    }
}