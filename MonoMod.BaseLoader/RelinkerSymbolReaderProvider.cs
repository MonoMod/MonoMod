using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using System.IO;
using MonoMod;

namespace MonoMod.BaseLoader {
    public class RelinkerSymbolReaderProvider : ISymbolReaderProvider {

        public DebugSymbolFormat Format;

        public ISymbolReader GetSymbolReader(ModuleDefinition module, Stream symbolStream) {
            switch (Format) {
                case DebugSymbolFormat.MDB:
                    return new MdbReaderProvider().GetSymbolReader(module, symbolStream);

                case DebugSymbolFormat.PDB:
#if !LEGACY
                    if (IsPortablePdb(symbolStream))
                        return new PortablePdbReaderProvider().GetSymbolReader(module, symbolStream);
                    return new NativePdbReaderProvider().GetSymbolReader(module, symbolStream);
#else
                    return new PdbReaderProvider().GetSymbolReader(module, symbolStream);
#endif

                default:
                    return null;
            }
        }

        public ISymbolReader GetSymbolReader(ModuleDefinition module, string fileName) {
            return null;
        }

        public static bool IsPortablePdb(Stream stream) {
            long start = stream.Position;
            if (stream.Length - start < 4)
                return false;
            try {
                byte[] buffer = new byte[4];
                stream.Read(buffer, 0, 4);
                return BitConverter.ToUInt32(buffer, 0) == 0x424a5342;
            } finally {
                stream.Seek(start, SeekOrigin.Begin);
            }
        }

    }
}
