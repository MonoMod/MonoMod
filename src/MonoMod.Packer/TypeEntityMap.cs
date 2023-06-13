using AsmResolver;
using AsmResolver.DotNet;
using System.Collections.Generic;
using MonoMod.Utils;
using System.Collections.Concurrent;
using System;

namespace MonoMod.Packer {

    internal readonly record struct NullableUtf8String(Utf8String? Value) {
        public static implicit operator NullableUtf8String(Utf8String? val) => new(val);
    }

    internal sealed class TypeEntityMap {
        private readonly Dictionary<TypeDefinition, TypeEntity> entityMap = new();
        private readonly Dictionary<NullableUtf8String, Dictionary<NullableUtf8String, List<TypeEntity>>> entitiesByName = new();
        private readonly ConcurrentDictionary<TypeEntity, TypeDefinition> remappedDefs = new();
        private readonly ConcurrentDictionary<TypeDefinition, List<TypeEntity>> fromRemapped = new();

        private TypeEntityMap() { }

        public static TypeEntityMap CreateForAllTypes(IEnumerable<ModuleDefinition> modules) {
            var map = new TypeEntityMap();

            foreach (var module in modules) {
                foreach (var type in module.TopLevelTypes) {
                    map.InitType(type);
                }
            }

            return map;
        }

        private void InitType(TypeDefinition def) {
            var entity = new TypeEntity(this, def);
            entityMap.Add(def, entity);

            if (def.DeclaringType is null) {
                if (!entitiesByName.TryGetValue(def.Namespace, out var nsDict)) {
                    entitiesByName.Add(def.Namespace, nsDict = new());
                }

                if (!nsDict.TryGetValue(def.Name, out var withName)) {
                    nsDict.Add(def.Name, withName = new());
                }

                withName.Add(entity);
            }

            // visit child types
            foreach (var type in def.NestedTypes) {
                InitType(type);
            }
        }

        public TypeEntity Lookup(TypeDefinition def) => entityMap[def];
        public IReadOnlyList<TypeEntity> WithSameNameAs(TypeEntity entity) {
            Helpers.DAssert(entity.Definition.DeclaringType is null);
            return entitiesByName[entity.Definition.Namespace][entity.Definition.Name];
        }

        public void MarkMappedDef(TypeDefinition source, TypeDefinition target) {
            MarkMappedDef(Lookup(source), target);
        }

        public void MarkMappedDef(TypeEntity entity, TypeDefinition target) {
            remappedDefs.AddOrUpdate(entity, target, (x, y) => throw new InvalidOperationException("entity was already mapped???"));
            var list = fromRemapped.GetOrAdd(target, static _ => new());
            lock (list) {
                list.Add(entity);
            }
        }

        public IEnumerable<IReadOnlyList<TypeEntity>> EnumerateEntitySets() {
            foreach (var v1 in entitiesByName.Values) {
                foreach (var v2 in v1.Values) {
                    yield return v2;
                }
            }
        }
    }
}
