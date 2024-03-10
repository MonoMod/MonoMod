using MonoMod.Backports;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MonoMod.Utils.Interop
{
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void GetSystemInfo([NativeTypeName("LPSYSTEM_INFO")] SYSTEM_INFO* lpSystemInfo);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern HMODULE GetModuleHandleW([NativeTypeName("LPCWSTR")] ushort* lpModuleName);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [return: NativeTypeName("FARPROC")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetProcAddress(HMODULE hModule, [NativeTypeName("LPCSTR")] sbyte* lpProcName);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern HMODULE LoadLibraryW([NativeTypeName("LPCWSTR")] ushort* lpLibFileName);

        [DllImport("kernel32", ExactSpelling = true)]
        [SetsLastSystemError]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern BOOL FreeLibrary(HMODULE hLibModule);

        [DllImport("kernel32", ExactSpelling = true)]
        [return: NativeTypeName("DWORD")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetLastError();

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

        public readonly unsafe partial struct HMODULE : IComparable, IComparable<HMODULE>, IEquatable<HMODULE>, IFormattable
        {
            public readonly void* Value;

            public HMODULE(void* value)
            {
                Value = value;
            }

            public static HMODULE INVALID_VALUE => new HMODULE((void*)(-1));

            public static HMODULE NULL => new HMODULE(null);

            public static bool operator ==(HMODULE left, HMODULE right) => left.Value == right.Value;

            public static bool operator !=(HMODULE left, HMODULE right) => left.Value != right.Value;

            public static bool operator <(HMODULE left, HMODULE right) => left.Value < right.Value;

            public static bool operator <=(HMODULE left, HMODULE right) => left.Value <= right.Value;

            public static bool operator >(HMODULE left, HMODULE right) => left.Value > right.Value;

            public static bool operator >=(HMODULE left, HMODULE right) => left.Value >= right.Value;

            public static explicit operator HMODULE(void* value) => new HMODULE(value);

            public static implicit operator void*(HMODULE value) => value.Value;

            public static explicit operator HMODULE(HANDLE value) => new HMODULE(value);

            public static implicit operator HANDLE(HMODULE value) => new HANDLE(value.Value);

            public static explicit operator HMODULE(byte value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator byte(HMODULE value) => (byte)(value.Value);

            public static explicit operator HMODULE(short value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator short(HMODULE value) => (short)(value.Value);

            public static explicit operator HMODULE(int value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator int(HMODULE value) => (int)(value.Value);

            public static explicit operator HMODULE(long value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator long(HMODULE value) => (long)(value.Value);

            public static explicit operator HMODULE(nint value) => new HMODULE(unchecked((void*)(value)));

            public static implicit operator nint(HMODULE value) => (nint)(value.Value);

            public static explicit operator HMODULE(sbyte value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator sbyte(HMODULE value) => (sbyte)(value.Value);

            public static explicit operator HMODULE(ushort value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator ushort(HMODULE value) => (ushort)(value.Value);

            public static explicit operator HMODULE(uint value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator uint(HMODULE value) => (uint)(value.Value);

            public static explicit operator HMODULE(ulong value) => new HMODULE(unchecked((void*)(value)));

            public static explicit operator ulong(HMODULE value) => (ulong)(value.Value);

            public static explicit operator HMODULE(nuint value) => new HMODULE(unchecked((void*)(value)));

            public static implicit operator nuint(HMODULE value) => (nuint)(value.Value);

            public int CompareTo(object? obj)
            {
                if (obj is HMODULE other)
                {
                    return CompareTo(other);
                }

                return (obj is null) ? 1 : throw new ArgumentException("obj is not an instance of HMODULE.");
            }
            public int CompareTo(HMODULE other)
                => sizeof(nint) == 4
                    ? ((uint)(Value)).CompareTo((uint)(other.Value))
                    : ((ulong)(Value)).CompareTo((ulong)(other.Value));

            public override bool Equals(object? obj) => (obj is HMODULE other) && Equals(other);

            public bool Equals(HMODULE other) => ((nuint)(Value)).Equals((nuint)(other.Value));

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
