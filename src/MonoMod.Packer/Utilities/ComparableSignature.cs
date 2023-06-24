using AsmResolver.DotNet;

namespace MonoMod.Packer.Utilities {
    internal abstract class ComparableSignature {

        private readonly TypeEntityMap Map;

        protected ComparableSignature(TypeEntityMap map) {
            Map = map;
        }

        public static object? CreateComparableInstance(TypeEntityMap map, IMemberDescriptor descriptor) {
            // an IMemberDescriptor can be several things:
            switch (descriptor) {
                case TypeDefinition typeDef:
                    // a type definition
                    // we always want to get whatever entity is correct from the map
                    return map.GetEntity(typeDef)?.GetUnified();

                case TypeReference typeRef:
                    // a type reference
                    // we always want to get the entity, just like with typeDef
                    return map.GetEntity(typeRef)?.GetUnified();

                case FieldDefinition fieldDef:
                    // a field definition
                    // always forward to map
                    return map.TryLookupField(fieldDef)?.GetUnified();

                case MethodDefinition methodDef:
                    // a method def
                    // always forward to map
                    return map.TryLookupMethod(methodDef)?.GetUnified();

                    // TODO: try to resolve references to non-generics into types to be unified?

                default:
                    // TODO: is this correct?
                    return descriptor;
            }
        }
    }
}
