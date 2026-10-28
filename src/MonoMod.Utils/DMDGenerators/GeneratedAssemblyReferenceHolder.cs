using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMod.Utils.DMDGenerators
{
    internal sealed class GeneratedAssemblyReferenceHolder
    {
        private readonly List<object> references = new();
        private readonly Dictionary<string, object> staticFieldSets = new();

        public int RecordedFieldSets => staticFieldSets.Count;

        public void AddReference(object obj)
        {
            references.Add(obj);
        }

        public void AddStaticField(string name, object obj)
        {
            staticFieldSets.Add(name, obj);
        }

        public bool NeedsReferencesField => references.Count > 0;

        public void SetReferencesFieldName(string name)
        {
            staticFieldSets.Add(name, references);
        }

        public void SetFields(Type owningType)
        {
            foreach (var kvp in staticFieldSets)
            {
                var fld = owningType.GetField(kvp.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Helpers.Assert(fld is not null);
                fld.SetValue(null, kvp.Value);
            }
        }
    }
}
