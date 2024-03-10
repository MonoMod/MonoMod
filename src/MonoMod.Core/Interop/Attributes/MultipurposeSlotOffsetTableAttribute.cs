using System;

namespace MonoMod.Core.Interop.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class MultipurposeSlotOffsetTableAttribute : Attribute
    {
        public int Bits { get; }
        public Type HelperType { get; }

        public MultipurposeSlotOffsetTableAttribute(int bits, Type helperType)
        {
            Bits = bits;
            HelperType = helperType;
        }
    }
}
