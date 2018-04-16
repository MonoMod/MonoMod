using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;

namespace MonoMod.BaseLoader {
    public static class YamlHelper {

        public static Deserializer Deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        public static Serializer Serializer = new SerializerBuilder().EmitDefaults().Build();

    }
}
