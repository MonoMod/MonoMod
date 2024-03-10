using MonoMod.Backports;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Interop
{

    // IMPORTANT: Instead of manually writing the interop code for Windows, we mostly use Microsoft.Windows.CsWin32 to generate them.
    // New Win32 methods should be added to NativeMethods.txt and used as Windows.Win32.Interop.*

    internal static unsafe class Windows
    {
        // Definitions copied from source.terrafx.dev

        [Conditional("NEVER")]
        [AttributeUsage(AttributeTargets.All)]
        private sealed class SetsLastSystemErrorAttribute : Attribute { }
        [Conditional("NEVER")]
        [AttributeUsage(AttributeTargets.All)]
        private sealed class NativeTypeNameAttribute : Attribute
        {
            public NativeTypeNameAttribute(string x) { }
        }


        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [return: NativeTypeName("LPVOID")]
        public static extern void* VirtualAlloc([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint flAllocationType, [NativeTypeName("DWORD")] uint flProtect);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        public static extern BOOL VirtualProtect([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint flNewProtect, [NativeTypeName("PDWORD")] uint* lpflOldProtect);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        public static extern BOOL VirtualFree([NativeTypeName("LPVOID")] void* lpAddress, [NativeTypeName("SIZE_T")] nuint dwSize, [NativeTypeName("DWORD")] uint dwFreeType);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [return: NativeTypeName("SIZE_T")]
        public static extern nuint VirtualQuery([NativeTypeName("LPCVOID")] void* lpAddress, [NativeTypeName("PMEMORY_BASIC_INFORMATION")] MEMORY_BASIC_INFORMATION* lpBuffer, [NativeTypeName("SIZE_T")] nuint dwLength);

        [DllImport("kernel32", ExactSpelling = true)]
        public static extern void GetSystemInfo([NativeTypeName("LPSYSTEM_INFO")] SYSTEM_INFO* lpSystemInfo);

        [DllImport("kernel32", ExactSpelling = true)]
        public static extern HANDLE GetCurrentProcess();

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        public static extern BOOL FlushInstructionCache(HANDLE hProcess, [NativeTypeName("LPCVOID")] void* lpBaseAddress, [NativeTypeName("SIZE_T")] nuint dwSize);

        [DllImport("kernel32", ExactSpelling = true)]
        [return: NativeTypeName("DWORD")]
        public static extern uint GetLastError();

        public unsafe partial struct MEMORY_BASIC_INFORMATION
        {
            [NativeTypeName("PVOID")]
            public void* BaseAddress;
            [NativeTypeName("PVOID")]
            public void* AllocationBase;
            [NativeTypeName("DWORD")]
            public uint AllocationProtect;

            // Note: in Win64 this has a PartitionId field. However, we don't care about it.
            // It is OK to leave it out because padding to RegionSize keeps the struct the same size anyway.

            [NativeTypeName("SIZE_T")]
            public nuint RegionSize;
            [NativeTypeName("DWORD")]
            public uint State;
            [NativeTypeName("DWORD")]
            public uint Protect;
            [NativeTypeName("DWORD")]
            public uint Type;
        }

        public unsafe partial struct SYSTEM_INFO
        {
            [NativeTypeName("_SYSTEM_INFO::(anonymous union at C:/Program Files (x86)/Windows Kits/10/include/10.0.22621.0/um/sysinfoapi.h:48:5)")]
            public _Anonymous_e__Union Anonymous;

            [NativeTypeName("DWORD")]
            public uint dwPageSize;
            [NativeTypeName("LPVOID")]
            public void* lpMinimumApplicationAddress;
            [NativeTypeName("LPVOID")]
            public void* lpMaximumApplicationAddress;
            [NativeTypeName("DWORD_PTR")]
            public nuint dwActiveProcessorMask;
            [NativeTypeName("DWORD")]
            public uint dwNumberOfProcessors;
            [NativeTypeName("DWORD")]
            public uint dwProcessorType;
            [NativeTypeName("DWORD")]
            public uint dwAllocationGranularity;
            [NativeTypeName("WORD")]
            public ushort wProcessorLevel;
            [NativeTypeName("WORD")]
            public ushort wProcessorRevision;

            [UnscopedRef]
            public ref uint dwOemId
            {
                [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
                get
                {
                    return ref Anonymous.dwOemId;
                }
            }

            [UnscopedRef]
            public ref ushort wProcessorArchitecture
            {
                [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
                get
                {
                    return ref Anonymous.Anonymous.wProcessorArchitecture;
                }
            }

            [UnscopedRef]
            public ref ushort wReserved
            {
                [MethodImpl(MethodImplOptionsEx.AggressiveInlining)]
                get
                {
                    return ref Anonymous.Anonymous.wReserved;
                }
            }

            [StructLayout(LayoutKind.Explicit)]
            public partial struct _Anonymous_e__Union
            {
                [FieldOffset(0)]
                [NativeTypeName("DWORD")]
                public uint dwOemId;
                [FieldOffset(0)]
                [NativeTypeName("_SYSTEM_INFO::(anonymous struct at C:/Program Files (x86)/Windows Kits/10/include/10.0.22621.0/um/sysinfoapi.h:50:9)")]
                public _Anonymous_e__Struct Anonymous;
                public partial struct _Anonymous_e__Struct
                {
                    [NativeTypeName("WORD")]
                    public ushort wProcessorArchitecture;
                    [NativeTypeName("WORD")]
                    public ushort wReserved;
                }
            }
        }

        public readonly partial struct BOOL : IComparable, IComparable<BOOL>, IEquatable<BOOL>, IFormattable
        {
            public readonly int Value;

            public BOOL(int value)
            {
                Value = value;
            }

            public static BOOL FALSE => new BOOL(0);

            public static BOOL TRUE => new BOOL(1);

            public static bool operator ==(BOOL left, BOOL right) => left.Value == right.Value;

            public static bool operator !=(BOOL left, BOOL right) => left.Value != right.Value;

            public static bool operator <(BOOL left, BOOL right) => left.Value < right.Value;

            public static bool operator <=(BOOL left, BOOL right) => left.Value <= right.Value;

            public static bool operator >(BOOL left, BOOL right) => left.Value > right.Value;

            public static bool operator >=(BOOL left, BOOL right) => left.Value >= right.Value;

            public static implicit operator bool(BOOL value) => value.Value != 0;

            public static implicit operator BOOL(bool value) => new BOOL(value ? 1 : 0);

            public static bool operator false(BOOL value) => value.Value == 0;

            public static bool operator true(BOOL value) => value.Value != 0;

            public static implicit operator BOOL(byte value) => new BOOL(value);

            public static explicit operator byte(BOOL value) => (byte)(value.Value);

            public static implicit operator BOOL(short value) => new BOOL(value);

            public static explicit operator short(BOOL value) => (short)(value.Value);

            public static implicit operator BOOL(int value) => new BOOL(value);

            public static implicit operator int(BOOL value) => value.Value;

            public static explicit operator BOOL(long value) => new BOOL(unchecked((int)(value)));

            public static implicit operator long(BOOL value) => value.Value;

            public static explicit operator BOOL(nint value) => new BOOL(unchecked((int)(value)));

            public static implicit operator nint(BOOL value) => value.Value;

            public static implicit operator BOOL(sbyte value) => new BOOL(value);

            public static explicit operator sbyte(BOOL value) => (sbyte)(value.Value);

            public static implicit operator BOOL(ushort value) => new BOOL(value);

            public static explicit operator ushort(BOOL value) => (ushort)(value.Value);

            public static explicit operator BOOL(uint value) => new BOOL(unchecked((int)(value)));

            public static explicit operator uint(BOOL value) => (uint)(value.Value);

            public static explicit operator BOOL(ulong value) => new BOOL(unchecked((int)(value)));

            public static explicit operator ulong(BOOL value) => (ulong)(value.Value);

            public static explicit operator BOOL(nuint value) => new BOOL(unchecked((int)(value)));

            public static explicit operator nuint(BOOL value) => (nuint)(value.Value);

            public int CompareTo(object? obj)
            {
                if (obj is BOOL other)
                {
                    return CompareTo(other);
                }

                return (obj is null) ? 1 : throw new ArgumentException("obj is not an instance of BOOL.");
            }

            public int CompareTo(BOOL other) => Value.CompareTo(other.Value);

            public override bool Equals(object? obj) => (obj is BOOL other) && Equals(other);

            public bool Equals(BOOL other) => Value.Equals(other.Value);

            public override int GetHashCode() => Value.GetHashCode();

            public override string ToString() => Value.ToString(provider: null);

            public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
        }

        public readonly unsafe partial struct HANDLE : IComparable, IComparable<HANDLE>, IEquatable<HANDLE>, IFormattable
        {
            public readonly void* Value;

            public HANDLE(void* value)
            {
                Value = value;
            }

            public static HANDLE INVALID_VALUE => new HANDLE((void*)(-1));

            public static HANDLE NULL => new HANDLE(null);

            public static bool operator ==(HANDLE left, HANDLE right) => left.Value == right.Value;

            public static bool operator !=(HANDLE left, HANDLE right) => left.Value != right.Value;

            public static bool operator <(HANDLE left, HANDLE right) => left.Value < right.Value;

            public static bool operator <=(HANDLE left, HANDLE right) => left.Value <= right.Value;

            public static bool operator >(HANDLE left, HANDLE right) => left.Value > right.Value;

            public static bool operator >=(HANDLE left, HANDLE right) => left.Value >= right.Value;

            public static explicit operator HANDLE(void* value) => new HANDLE(value);

            public static implicit operator void*(HANDLE value) => value.Value;

            public static explicit operator HANDLE(byte value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator byte(HANDLE value) => (byte)(value.Value);

            public static explicit operator HANDLE(short value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator short(HANDLE value) => (short)(value.Value);

            public static explicit operator HANDLE(int value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator int(HANDLE value) => (int)(value.Value);

            public static explicit operator HANDLE(long value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator long(HANDLE value) => (long)(value.Value);

            public static explicit operator HANDLE(nint value) => new HANDLE(unchecked((void*)(value)));

            public static implicit operator nint(HANDLE value) => (nint)(value.Value);

            public static explicit operator HANDLE(sbyte value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator sbyte(HANDLE value) => (sbyte)(value.Value);

            public static explicit operator HANDLE(ushort value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator ushort(HANDLE value) => (ushort)(value.Value);

            public static explicit operator HANDLE(uint value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator uint(HANDLE value) => (uint)(value.Value);

            public static explicit operator HANDLE(ulong value) => new HANDLE(unchecked((void*)(value)));

            public static explicit operator ulong(HANDLE value) => (ulong)(value.Value);

            public static explicit operator HANDLE(nuint value) => new HANDLE(unchecked((void*)(value)));

            public static implicit operator nuint(HANDLE value) => (nuint)(value.Value);

            public int CompareTo(object? obj)
            {
                if (obj is HANDLE other)
                {
                    return CompareTo(other);
                }

                return (obj is null) ? 1 : throw new ArgumentException("obj is not an instance of HANDLE.");
            }

            public int CompareTo(HANDLE other)
                => sizeof(nint) == 4
                    ? ((uint)(Value)).CompareTo((uint)(other.Value))
                    : ((ulong)(Value)).CompareTo((ulong)(other.Value));

            public override bool Equals(object? obj) => (obj is HANDLE other) && Equals(other);

            public bool Equals(HANDLE other) => ((nuint)(Value)).Equals((nuint)(other.Value));

            public override int GetHashCode() => ((nuint)(Value)).GetHashCode();

            public override string ToString()
                => sizeof(nuint) == 4
                    ? ((uint)(Value)).ToString("X8", null)
                    : ((ulong)(Value)).ToString("X16", null);

            public string ToString(string? format, IFormatProvider? formatProvider)
                => sizeof(nint) == 4
                    ? ((uint)(Value)).ToString(format, formatProvider)
                    : ((ulong)(Value)).ToString(format, formatProvider);
        }

        [NativeTypeName("#define MEM_COMMIT 0x00001000")]
        public const int MEM_COMMIT = 0x00001000;

        [NativeTypeName("#define MEM_RESERVE 0x00002000")]
        public const int MEM_RESERVE = 0x00002000;

        [NativeTypeName("#define MEM_REPLACE_PLACEHOLDER 0x00004000")]
        public const int MEM_REPLACE_PLACEHOLDER = 0x00004000;

        [NativeTypeName("#define MEM_RESERVE_PLACEHOLDER 0x00040000")]
        public const int MEM_RESERVE_PLACEHOLDER = 0x00040000;

        [NativeTypeName("#define MEM_RESET 0x00080000")]
        public const int MEM_RESET = 0x00080000;

        [NativeTypeName("#define MEM_TOP_DOWN 0x00100000")]
        public const int MEM_TOP_DOWN = 0x00100000;

        [NativeTypeName("#define MEM_WRITE_WATCH 0x00200000")]
        public const int MEM_WRITE_WATCH = 0x00200000;

        [NativeTypeName("#define MEM_PHYSICAL 0x00400000")]
        public const int MEM_PHYSICAL = 0x00400000;

        [NativeTypeName("#define MEM_ROTATE 0x00800000")]
        public const int MEM_ROTATE = 0x00800000;

        [NativeTypeName("#define MEM_DIFFERENT_IMAGE_BASE_OK 0x00800000")]
        public const int MEM_DIFFERENT_IMAGE_BASE_OK = 0x00800000;

        [NativeTypeName("#define MEM_RESET_UNDO 0x01000000")]
        public const int MEM_RESET_UNDO = 0x01000000;

        [NativeTypeName("#define MEM_LARGE_PAGES 0x20000000")]
        public const int MEM_LARGE_PAGES = 0x20000000;

        [NativeTypeName("#define MEM_4MB_PAGES 0x80000000")]
        public const uint MEM_4MB_PAGES = 0x80000000;

        [NativeTypeName("#define MEM_64K_PAGES (MEM_LARGE_PAGES | MEM_PHYSICAL)")]
        public const int MEM_64K_PAGES = (0x20000000 | 0x00400000);

        [NativeTypeName("#define MEM_UNMAP_WITH_TRANSIENT_BOOST 0x00000001")]
        public const int MEM_UNMAP_WITH_TRANSIENT_BOOST = 0x00000001;

        [NativeTypeName("#define MEM_COALESCE_PLACEHOLDERS 0x00000001")]
        public const int MEM_COALESCE_PLACEHOLDERS = 0x00000001;

        [NativeTypeName("#define MEM_PRESERVE_PLACEHOLDER 0x00000002")]
        public const int MEM_PRESERVE_PLACEHOLDER = 0x00000002;

        [NativeTypeName("#define MEM_DECOMMIT 0x00004000")]
        public const int MEM_DECOMMIT = 0x00004000;

        [NativeTypeName("#define MEM_RELEASE 0x00008000")]
        public const int MEM_RELEASE = 0x00008000;

        [NativeTypeName("#define MEM_FREE 0x00010000")]
        public const int MEM_FREE = 0x00010000;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_GRAPHICS 0x00000001")]
        public const int MEM_EXTENDED_PARAMETER_GRAPHICS = 0x00000001;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_NONPAGED 0x00000002")]
        public const int MEM_EXTENDED_PARAMETER_NONPAGED = 0x00000002;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_ZERO_PAGES_OPTIONAL 0x00000004")]
        public const int MEM_EXTENDED_PARAMETER_ZERO_PAGES_OPTIONAL = 0x00000004;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_NONPAGED_LARGE 0x00000008")]
        public const int MEM_EXTENDED_PARAMETER_NONPAGED_LARGE = 0x00000008;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_NONPAGED_HUGE 0x00000010")]
        public const int MEM_EXTENDED_PARAMETER_NONPAGED_HUGE = 0x00000010;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_SOFT_FAULT_PAGES 0x00000020")]
        public const int MEM_EXTENDED_PARAMETER_SOFT_FAULT_PAGES = 0x00000020;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_EC_CODE 0x00000040")]
        public const int MEM_EXTENDED_PARAMETER_EC_CODE = 0x00000040;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_IMAGE_NO_HPAT 0x00000080")]
        public const int MEM_EXTENDED_PARAMETER_IMAGE_NO_HPAT = 0x00000080;

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_NUMA_NODE_MANDATORY MINLONG64")]
        public const long MEM_EXTENDED_PARAMETER_NUMA_NODE_MANDATORY = unchecked((long)(~((long)(((ulong)(~((ulong)(0)))) >> 1))));

        [NativeTypeName("#define MEM_EXTENDED_PARAMETER_TYPE_BITS 8")]
        public const int MEM_EXTENDED_PARAMETER_TYPE_BITS = 8;

        [NativeTypeName("#define MEM_DEDICATED_ATTRIBUTE_NOT_SPECIFIED ((DWORD64) -1)")]
        public const ulong MEM_DEDICATED_ATTRIBUTE_NOT_SPECIFIED = unchecked((ulong)(-1));

        [NativeTypeName("#define MEM_PRIVATE 0x00020000")]
        public const int MEM_PRIVATE = 0x00020000;

        [NativeTypeName("#define MEM_MAPPED 0x00040000")]
        public const int MEM_MAPPED = 0x00040000;

        [NativeTypeName("#define MEM_IMAGE 0x01000000")]
        public const int MEM_IMAGE = 0x01000000;

        [NativeTypeName("#define PAGE_NOACCESS 0x01")]
        public const int PAGE_NOACCESS = 0x01;

        [NativeTypeName("#define PAGE_READONLY 0x02")]
        public const int PAGE_READONLY = 0x02;

        [NativeTypeName("#define PAGE_READWRITE 0x04")]
        public const int PAGE_READWRITE = 0x04;

        [NativeTypeName("#define PAGE_WRITECOPY 0x08")]
        public const int PAGE_WRITECOPY = 0x08;

        [NativeTypeName("#define PAGE_EXECUTE 0x10")]
        public const int PAGE_EXECUTE = 0x10;

        [NativeTypeName("#define PAGE_EXECUTE_READ 0x20")]
        public const int PAGE_EXECUTE_READ = 0x20;

        [NativeTypeName("#define PAGE_EXECUTE_READWRITE 0x40")]
        public const int PAGE_EXECUTE_READWRITE = 0x40;

        [NativeTypeName("#define PAGE_EXECUTE_WRITECOPY 0x80")]
        public const int PAGE_EXECUTE_WRITECOPY = 0x80;

        [NativeTypeName("#define PAGE_GUARD 0x100")]
        public const int PAGE_GUARD = 0x100;

        [NativeTypeName("#define PAGE_NOCACHE 0x200")]
        public const int PAGE_NOCACHE = 0x200;

        [NativeTypeName("#define PAGE_WRITECOMBINE 0x400")]
        public const int PAGE_WRITECOMBINE = 0x400;

        [NativeTypeName("#define PAGE_GRAPHICS_NOACCESS 0x0800")]
        public const int PAGE_GRAPHICS_NOACCESS = 0x0800;

        [NativeTypeName("#define PAGE_GRAPHICS_READONLY 0x1000")]
        public const int PAGE_GRAPHICS_READONLY = 0x1000;

        [NativeTypeName("#define PAGE_GRAPHICS_READWRITE 0x2000")]
        public const int PAGE_GRAPHICS_READWRITE = 0x2000;

        [NativeTypeName("#define PAGE_GRAPHICS_EXECUTE 0x4000")]
        public const int PAGE_GRAPHICS_EXECUTE = 0x4000;

        [NativeTypeName("#define PAGE_GRAPHICS_EXECUTE_READ 0x8000")]
        public const int PAGE_GRAPHICS_EXECUTE_READ = 0x8000;

        [NativeTypeName("#define PAGE_GRAPHICS_EXECUTE_READWRITE 0x10000")]
        public const int PAGE_GRAPHICS_EXECUTE_READWRITE = 0x10000;

        [NativeTypeName("#define PAGE_GRAPHICS_COHERENT 0x20000")]
        public const int PAGE_GRAPHICS_COHERENT = 0x20000;

        [NativeTypeName("#define PAGE_GRAPHICS_NOCACHE 0x40000")]
        public const int PAGE_GRAPHICS_NOCACHE = 0x40000;

        [NativeTypeName("#define PAGE_ENCLAVE_THREAD_CONTROL 0x80000000")]
        public const uint PAGE_ENCLAVE_THREAD_CONTROL = 0x80000000;

        [NativeTypeName("#define PAGE_REVERT_TO_FILE_MAP 0x80000000")]
        public const uint PAGE_REVERT_TO_FILE_MAP = 0x80000000;

        [NativeTypeName("#define PAGE_TARGETS_NO_UPDATE 0x40000000")]
        public const int PAGE_TARGETS_NO_UPDATE = 0x40000000;

        [NativeTypeName("#define PAGE_TARGETS_INVALID 0x40000000")]
        public const int PAGE_TARGETS_INVALID = 0x40000000;

        [NativeTypeName("#define PAGE_ENCLAVE_UNVALIDATED 0x20000000")]
        public const int PAGE_ENCLAVE_UNVALIDATED = 0x20000000;

        [NativeTypeName("#define PAGE_ENCLAVE_MASK 0x10000000")]
        public const int PAGE_ENCLAVE_MASK = 0x10000000;

        [NativeTypeName("#define PAGE_ENCLAVE_DECOMMIT (PAGE_ENCLAVE_MASK | 0)")]
        public const int PAGE_ENCLAVE_DECOMMIT = (0x10000000 | 0);

        [NativeTypeName("#define PAGE_ENCLAVE_SS_FIRST (PAGE_ENCLAVE_MASK | 1)")]
        public const int PAGE_ENCLAVE_SS_FIRST = (0x10000000 | 1);

        [NativeTypeName("#define PAGE_ENCLAVE_SS_REST (PAGE_ENCLAVE_MASK | 2)")]
        public const int PAGE_ENCLAVE_SS_REST = (0x10000000 | 2);

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_INTEL 0")]
        public const int PROCESSOR_ARCHITECTURE_INTEL = 0;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_MIPS 1")]
        public const int PROCESSOR_ARCHITECTURE_MIPS = 1;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_ALPHA 2")]
        public const int PROCESSOR_ARCHITECTURE_ALPHA = 2;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_PPC 3")]
        public const int PROCESSOR_ARCHITECTURE_PPC = 3;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_SHX 4")]
        public const int PROCESSOR_ARCHITECTURE_SHX = 4;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_ARM 5")]
        public const int PROCESSOR_ARCHITECTURE_ARM = 5;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_IA64 6")]
        public const int PROCESSOR_ARCHITECTURE_IA64 = 6;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_ALPHA64 7")]
        public const int PROCESSOR_ARCHITECTURE_ALPHA64 = 7;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_MSIL 8")]
        public const int PROCESSOR_ARCHITECTURE_MSIL = 8;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_AMD64 9")]
        public const int PROCESSOR_ARCHITECTURE_AMD64 = 9;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_IA32_ON_WIN64 10")]
        public const int PROCESSOR_ARCHITECTURE_IA32_ON_WIN64 = 10;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_NEUTRAL 11")]
        public const int PROCESSOR_ARCHITECTURE_NEUTRAL = 11;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_ARM64 12")]
        public const int PROCESSOR_ARCHITECTURE_ARM64 = 12;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_ARM32_ON_WIN64 13")]
        public const int PROCESSOR_ARCHITECTURE_ARM32_ON_WIN64 = 13;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_IA32_ON_ARM64 14")]
        public const int PROCESSOR_ARCHITECTURE_IA32_ON_ARM64 = 14;

        [NativeTypeName("#define PROCESSOR_ARCHITECTURE_UNKNOWN 0xFFFF")]
        public const int PROCESSOR_ARCHITECTURE_UNKNOWN = 0xFFFF;

    }

}
