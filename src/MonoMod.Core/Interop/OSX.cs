using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{
    internal static unsafe class OSX
    {

        public const string LibSystem = "libSystem";

        [DllImport(LibSystem, EntryPoint = "getpagesize")]
        public static extern int GetPageSize();

        [DllImport(LibSystem, EntryPoint = "sys_icache_invalidate")]
        public static unsafe extern void sys_icache_invalidate(void* start, nuint size);

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mkstemp")]
        public static extern unsafe int MkSTemp(byte* template);

        [DllImport(LibSystem, CallingConvention = CallingConvention.Cdecl, EntryPoint = "__error")]
        public static extern unsafe int* __error();
        public static unsafe int Errno => *__error();

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t mach_vm_region_recurse
        (
            vm_map_read_t target_task,
            mach_vm_address_t* address,
            mach_vm_size_t* size,
            natural_t* nesting_depth,
            vm_region_recurse_info_t info,
            mach_msg_type_number_t* infoCnt
        );
        */

        // vm_map_read_t = mach_msg_type_number_t = mach_port_t = natural_t = int/uint
        // mach_vm_size_t = uint64 everywhere but ios
        [DllImport(LibSystem, EntryPoint = "mach_vm_region_recurse")]
        public static extern kern_return_t mach_vm_region_recurse(int targetTask, [In, Out] ulong* address, [Out] ulong* size,
            [In, Out] int* nestingDepth, [Out] vm_region_submap_short_info_64* info, [In, Out] int* infoSize);

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t mach_vm_protect
        (
            vm_map_t target_task,
            mach_vm_address_t address,
            mach_vm_size_t size,
            boolean_t set_maximum,
            vm_prot_t new_protection
        );
        */

        [DllImport(LibSystem, EntryPoint = "mach_vm_protect")]
        public static extern kern_return_t mach_vm_protect(int targetTask, ulong address, ulong size, boolean_t setMax, vm_prot_t protection);

        /// <summary>
        /// Allocate zero-filled memory in the address space of the target task, either at the specified addres, or wherever space can be found (controlled by flags),
        /// of the specified size. The address at which the allocation actually took place is returned.
        /// </summary>
        [DllImport(LibSystem, EntryPoint = "mach_vm_allocate")]
        public static extern kern_return_t mach_vm_allocate(int targetTask, [In, Out] ulong* address, ulong size, vm_flags flags);

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t mach_vm_map
        (
            vm_map_t target_task,
            mach_vm_address_t* address,
            mach_vm_size_t size,
            mach_vm_offset_t mask,
            int flags,
            mem_entry_name_port_t object,
            memory_object_offset_t offset,
            boolean_t copy,
            vm_prot_t cur_protection,
            vm_prot_t max_protection,
            vm_inherit_t inheritance
        );
        */
        // mem_entry_name_port_t = mach_port_t = int
        // object can be NULL port (aka 0) for newly allocated pages, a la mach_vm_allocate
        /// <summary>
        /// Map a user-supplie memory object into the virtual address
        /// space of the target task.  If desired (anywhere is TRUE),
        /// the kernel will find a suitable address range of the
        /// specified size; else, the specific address will be allocated.
        /// 
        /// The beginning address of the range will be aligned on a virtual
        /// page boundary, be at or beyond the address specified, and
        /// meet the mask requirements (bits turned on in the mask must not
        /// be turned on in the result); the size of the range, in bytes,
        /// will be rounded up to an integral number of virtual pages.
        /// 
        /// The memory in the resulting range will be associated with the
        /// specified memory object, with the beginning of the memory range
        /// referring to the specified offset into the memory object.
        /// 
        /// The mapping will take the current and maximum protections and
        /// the inheritance attributes specified; see the vm_protect and
        /// vm_inherit calls for a description of these attributes.
        /// 
        /// If desired (copy is TRUE), the memory range will be filled
        /// with a copy of the data from the memory object; this copy will
        /// be private to this mapping in this target task.  Otherwise,
        /// the memory in this mapping will be shared with other mappings
        /// of the same memory object at the same offset (in this task or
        /// in other tasks).  [The Mach kernel only enforces shared memory
        /// consistency among mappings on one host with similar page alignments.
        /// The user-defined memory manager for this object is responsible
        /// for further consistency.]
        /// </summary>
        [DllImport(LibSystem, EntryPoint = "mach_vm_map")]
        public static extern kern_return_t mach_vm_map(int targetTask, [In, Out] ulong* address, ulong size, ulong mask, vm_flags flags,
            int @object, ulong offset, boolean_t copy, vm_prot_t curProt, vm_prot_t maxProt, vm_inherit_t inheritance);

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t mach_vm_remap
        (
            vm_map_t target_task,
            mach_vm_address_t* target_address,
            mach_vm_size_t size,
            mach_vm_offset_t mask,
            int flags,
            vm_map_t src_task,
            mach_vm_address_t src_address,
            boolean_t copy,
            vm_prot_t* cur_protection,
            vm_prot_t* max_protection,
            vm_inherit_t inheritance
        );
        */
        [DllImport(LibSystem, EntryPoint = "mach_vm_remap")]
        public static extern kern_return_t mach_vm_remap(int targetTask, [In, Out] ulong* targetAddress, ulong size, ulong offset, vm_flags flags,
            int srcTask, ulong srcAddress, boolean_t copy, [Out] vm_prot_t* curProt, [Out] vm_prot_t* maxProt, vm_inherit_t inherit);

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t mach_make_memory_entry_64
        (
            vm_map_t target_task,
            memory_object_size_t* size,
            memory_object_offset_t offset,
            vm_prot_t permission,
            mach_port_t* object_handle,
            mem_entry_name_port_t parent_entry
        );
        */
        /// <summary>
        /// Allow application level processes to create named entries which
        /// correspond to mapped portions of their address space.  These named
        /// entries can then be manipulated, shared with other processes in
        /// other address spaces and ultimately mapped in ohter address spaces
        ///
        /// THIS INTERFACE IS STILL EVOLVING.
        /// </summary>
        [DllImport(LibSystem, EntryPoint = "mach_make_memory_entry_64")]
        public static extern kern_return_t mach_make_memory_entry_64(int targetTask, [In, Out] ulong* size, ulong offset, vm_prot_t permission, int* objectHandle, int parentHandle);

        [DllImport(LibSystem, EntryPoint = "mach_vm_deallocate")]
        public static extern kern_return_t mach_vm_deallocate(int targetTask, ulong address, ulong size);

        // mach_task_self() is a macro which accesses the global mach_task_self_, hence this whole rigamarole
        private static unsafe int* mach_task_self_;
        public static unsafe int mach_task_self()
        {
            var ptr = mach_task_self_;
            if (ptr is null)
            {
                var lib = DynDll.OpenLibrary(LibSystem);
                try
                {
                    mach_task_self_ = ptr = (int*)DynDll.GetExport(lib, "mach_task_self_");
                }
                finally
                {
                    DynDll.CloseLibrary(lib);
                }
            }
            return *ptr;
        }

        /*
        #ifdef  mig_external
        mig_external
        #else
        extern
        #endif
        kern_return_t task_info
        (
                task_name_t target_task,
                task_flavor_t flavor,
                task_info_t task_info_out,
                mach_msg_type_number_t* task_info_outCnt
        );
        */
        [DllImport(LibSystem, EntryPoint = "task_info")]
        public static extern kern_return_t task_info(int targetTask, task_flavor_t flavor, [Out] task_dyld_info* taskInfoOut, int* taskInfoCnt);


        // https://github.com/apple-oss-distributions/xnu/blob/5c2921b07a2480ab43ec66f5b9e41cb872bc554f/osfmk/mach/vm_region.h#L254
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct vm_region_submap_short_info_64
        {
            public vm_prot_t protection;
            public vm_prot_t max_protection;
            public vm_inherit_t inheritance;
            public ulong offset; // vm_object_offset_t
            public uint user_tag;
            public uint ref_count;
            public ushort shadow_depth;
            public byte external_pager;
            public ShareMode share_mode; // enum???
            public boolean_t is_submap;
            public vm_behavior_t behavior;
            public uint object_id; // vm32_object_id_t
            public ushort user_wired_count;

            public static unsafe int Count => sizeof(vm_region_submap_short_info_64) / sizeof(int);
        }

        // https://github.com/apple-oss-distributions/xnu/blob/5c2921b07a2480ab43ec66f5b9e41cb872bc554f/osfmk/mach/vm_region.h#L120
        public enum ShareMode : byte
        {
            COW = 1,
            Private = 2,
            Empty = 3,
            Shared = 4,
            TrueShared = 5,
            PrivateAliased = 6,
            SharedAliased = 7,
            LargePage = 8,
        }


        // https://github.com/apple-oss-distributions/xnu/blob/5c2921b07a2480ab43ec66f5b9e41cb872bc554f/osfmk/mach/task_info.h#L279
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct task_dyld_info
        {
            public ulong all_image_info_addr; // mach_vm_address_t
            public ulong all_image_info_size; // mach_vm_size_t
            public task_dyld_all_image_info_format all_image_info_format;

            public unsafe dyld_all_image_infos* all_image_infos => (dyld_all_image_infos*)(nuint)all_image_info_addr;
            public static unsafe int Count => sizeof(task_dyld_info) / sizeof(int);
        }

        // https://stackoverflow.com/a/23229148
        // because this is all in-process, we can use pointers normally
        // this is defined in /usr/include/mach-o/dyld_images.h in the SDK
        [StructLayout(LayoutKind.Sequential)] // don't know the pack setting unfortunately
        public unsafe struct dyld_all_image_infos
        {
            public uint version;
            public uint infoArrayCount;
            public dyld_image_info* infoArray;
            // ...
            // There is actually more, but it's not relevant to us.
            public ReadOnlySpan<dyld_image_info> InfoArray => new(infoArray, (int)infoArrayCount);
        }

        [StructLayout(LayoutKind.Sequential)] // don't know the pack setting unfortunately
        public unsafe struct dyld_image_info
        {
            public void* imageLoadAddress; // mach_header*
            public PCSTR imageFilePath; // const char* (we use PCSTR because it already can give us a string with no extra work)
            public nuint imageFileModDate; // uintptr_t
        }

        public enum task_dyld_all_image_info_format : int
        {
            Bits32 = 0,
            Bits64 = 1,
        }

        public enum task_flavor_t : uint
        {
            DyldInfo = 17,
        }

        public enum vm_region_flavor_t : int
        {
            BasicInfo64 = 9
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/vm_prot.h.auto.html
        [Flags]
        public enum vm_prot_t : int
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
            Default = Read | Write,
            All = Read | Write | Execute,

            [Obsolete("Only used for memory_object_lock_request. Invalid otherwise.")]
            NoChange = 0x8,

            // makes COW if write cannot be obtained
            Copy = 0x10,
            // inline docs say this is invalid, but its the same as above
            WantsCopy = Copy,

            [Obsolete("Invalid value. Indicates that other bits are to be applied as mask to actual bits.")]
            IsMask = 0x40,

            [Obsolete("Invalid value. Tells mprotect to not set Read. Used for execute-only.")]
            StripRead = 0x80,

            [Obsolete("Invalid value. Use only for mprotect.")]
            ExecuteOnly = Execute | StripRead,
        }

        public static VmProtFmtProxy P(vm_prot_t prot) => new(prot);

        public struct VmProtFmtProxy : IDebugFormattable
        {
            private readonly vm_prot_t value;
            public VmProtFmtProxy(vm_prot_t value) => this.value = value;

            // format is: [~]rwx[!][c][ (mask)]
            public bool TryFormatInto(Span<char> span, out int wrote)
            {
                var w = 0;

#pragma warning disable CS0618 // Type or member is obsolete
                if (value.Has(vm_prot_t.NoChange))
                {
                    if (span.Slice(w).Length < 1)
                    {
                        wrote = w;
                        return false;
                    }
                    span[w++] = '~';
                }

                if (span.Slice(w).Length < 3)
                {
                    wrote = 0;
                    return false;
                }
                span[w++] = value.Has(vm_prot_t.Read) ? 'r' : '-';
                span[w++] = value.Has(vm_prot_t.Write) ? 'w' : '-';
                span[w++] = value.Has(vm_prot_t.Execute) ? 'x' : '-';

                if (value.Has(vm_prot_t.StripRead))
                {
                    if (span.Slice(w).Length < 1)
                    {
                        wrote = w;
                        return false;
                    }
                    span[w++] = '!';
                }

                if (value.Has(vm_prot_t.Copy))
                {
                    if (span.Slice(w).Length < 1)
                    {
                        wrote = w;
                        return false;
                    }
                    span[w++] = 'c';
                }

                if (value.Has(vm_prot_t.IsMask))
                {
                    const string MaskStr = " (mask)";
                    if (span.Slice(w).Length < MaskStr.Length)
                    {
                        wrote = w;
                        return false;
                    }
                    MaskStr.AsSpan().CopyTo(span.Slice(w));
                    w += MaskStr.Length;
                }
#pragma warning restore CS0618 // Type or member is obsolete

                wrote = w;
                return true;
            }
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/vm_statistics.h.auto.html
        [Flags]
        public enum vm_flags : int
        {
            // Allocate at the specified virtual address.
            Fixed = 0x0000,
            // Allocate anywhere in the address space.
            Anywhere = 0x0001,
            // Create a purgable VM object for that new VM region
            Purgable = 0x0002,
            // The new VM region will be chunked up into 4GB sized pieces.
            Chunk4GB = 0x0004,
            RandomAddr = 0x0008,
            // Pages brought in to this VM region are placed on the speculative queue instead of the active queue.
            // In other words, they are not cached so that they will be stolen first if memory runs low.
            NoCache = 0x0010,
            // The new VM region can replace existing VM regions if necessary.
            Overwrite = 0x4000,
            SuperpageMask = 0x70000,
            SuperpageSizeAny = 0x10000,
            SuperpageWSize2MB = 0x20000,
            AliasMask = unchecked((int)0xff000000),
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/vm_inherit.h.auto.html
        public enum vm_inherit_t : uint
        {
            Share = 0, // share with child
            Copy = 1, // copy into child
            None = 2, // absent form child
            DonateCopy = 3, // copy and delete

            Default = Copy,
            LastValid = None,
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/vm_behavior.h.auto.html
        public enum vm_behavior_t : int
        {
            // stroed in t VM map entry
            Default = 0,
            Random = 1,
            Sequential = 2,
            ReverseSequential = 3,
            // only affect time-of-call and not saved in VM map entry
            WillNeed = 4,
            DontNeed = 5,
            Free = 6,
            ZeroWiredPages = 7,
            Reusable = 8,
            Reuse = 9,
            CanReuse = 10,
            PageOut = 11,
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/machine/boolean.h.auto.html
        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/i386/boolean.h.auto.html
        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/arm/boolean.h.auto.html
        [DebuggerDisplay($"{{{nameof(ToString)}(),nq}}")]
        [StructLayout(LayoutKind.Sequential)]
        public struct boolean_t
        {
            // note: this is uint on x86_64, but int everywhere else
            private int value;

            public boolean_t(bool value) => this.value = value ? 1 : 0;

            public static implicit operator bool(boolean_t v) => v.value != 0;
            public static implicit operator boolean_t(bool v) => new(v);
            public static bool operator true(boolean_t v) => v;
            public static bool operator false(boolean_t v) => !v;
            public override string ToString() => this ? "true" : "false";
        }

        // https://opensource.apple.com/source/xnu/xnu-7195.81.3/osfmk/mach/kern_return.h.auto.html
        [DebuggerDisplay($"{{{nameof(Value)}}}")]
        [StructLayout(LayoutKind.Sequential)]
        public struct kern_return_t : IEquatable<kern_return_t>
        {
            // note: this is uint on x86_64, but int everywhere else
            private int value;

            public int Value => value;

            public kern_return_t(int value) => this.value = value;

            public static implicit operator bool(kern_return_t v) => v.value == 0;

            public static bool operator ==(kern_return_t x, kern_return_t y) => x.value == y.value;
            public static bool operator !=(kern_return_t x, kern_return_t y) => x.value != y.value;

            public override bool Equals(object? obj)
            {
                return obj is kern_return_t t && Equals(t);
            }

            public bool Equals(kern_return_t other)
            {
                return value == other.value;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(value);
            }

            public static kern_return_t Success = new(0);
            public static kern_return_t InvalidAddress = new(1);
            public static kern_return_t ProtectionFailure = new(2);
            public static kern_return_t NoSpace = new(3);
            public static kern_return_t InvalidArgument = new(4);
            public static kern_return_t Failure = new(5);
            // TODO: more
        }
    }
}
