// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System
{
    internal static partial class SpanHelpers
    {
        public static unsafe int SequenceCompareTo(ref char first, int firstLength, ref char second, int secondLength)
        {
            Debug.Assert(firstLength >= 0);
            Debug.Assert(secondLength >= 0);

            int lengthDelta = firstLength - secondLength;

            if (Unsafe.AreSame(ref first, ref second))
                goto Equal;

            nint minLength = (nint)((firstLength < secondLength) ? firstLength : secondLength);
            nint i = 0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations

            if ((byte*)minLength >= (byte*)(sizeof(UIntPtr) / sizeof(char)))
            {

                while ((byte*)minLength >= (byte*)(i + sizeof(UIntPtr) / sizeof(char)))
                {
                    if (Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref first, i))) !=
                        Unsafe.ReadUnaligned<UIntPtr>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref second, i))))
                    {
                        break;
                    }
                    i += sizeof(UIntPtr) / sizeof(char);
                }
            }

            if (sizeof(UIntPtr) > sizeof(int) && (byte*)minLength >= (byte*)(i + sizeof(int) / sizeof(char)))
            {
                if (Unsafe.ReadUnaligned<int>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref first, i))) ==
                    Unsafe.ReadUnaligned<int>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref second, i))))
                {
                    i += sizeof(int) / sizeof(char);
                }
            }

            while ((byte*)i < (byte*)minLength)
            {
                int result = Unsafe.Add(ref first, i).CompareTo(Unsafe.Add(ref second, i));
                if (result != 0)
                    return result;
                i += 1;
            }

            Equal:
            return lengthDelta;
        }

        public static unsafe int IndexOf(ref char searchSpace, char value, int length)
        {
            Debug.Assert(length >= 0);

            fixed (char* pChars = &searchSpace)
            {
                char* pCh = pChars;
                char* pEndCh = pCh + length;

                while (length >= 4)
                {
                    length -= 4;

                    if (*pCh == value)
                        goto Found;
                    if (*(pCh + 1) == value)
                        goto Found1;
                    if (*(pCh + 2) == value)
                        goto Found2;
                    if (*(pCh + 3) == value)
                        goto Found3;

                    pCh += 4;
                }

                while (length > 0)
                {
                    length -= 1;

                    if (*pCh == value)
                        goto Found;

                    pCh += 1;
                }
                return -1;
                Found3:
                pCh++;
                Found2:
                pCh++;
                Found1:
                pCh++;
                Found:
                return (int)(pCh - pChars);
            }
        }

        public static unsafe int LastIndexOf(ref char searchSpace, char value, int length)
        {
            Debug.Assert(length >= 0);

            fixed (char* pChars = &searchSpace)
            {
                char* pCh = pChars + length;
                char* pEndCh = pChars;
                while (length >= 4)
                {
                    length -= 4;
                    pCh -= 4;

                    if (*(pCh + 3) == value)
                        goto Found3;
                    if (*(pCh + 2) == value)
                        goto Found2;
                    if (*(pCh + 1) == value)
                        goto Found1;
                    if (*pCh == value)
                        goto Found;
                }

                while (length > 0)
                {
                    length -= 1;
                    pCh -= 1;

                    if (*pCh == value)
                        goto Found;
                }
                return -1;
                Found:
                return (int)(pCh - pEndCh);
                Found1:
                return (int)(pCh - pEndCh) + 1;
                Found2:
                return (int)(pCh - pEndCh) + 2;
                Found3:
                return (int)(pCh - pEndCh) + 3;
            }
        }
    }
}