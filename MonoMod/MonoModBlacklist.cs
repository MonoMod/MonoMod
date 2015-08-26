using System;

namespace MonoMod {
    /// <summary>
    /// Copy this class into your game / program and modify the blacklist items.
    /// </summary>
    internal static class MonoModBlacklist {
        private static string[] items = {
            "AssemblyName:FullName",
            "MonoMod:MonoMod.MonoMod.GlobalBlacklist",

            //Add your items here.
            //Format: <Assembly name, basically the name of the DLL file without .dll>:<Full name, including namespace and type>
            //Example: In game.exe, disable editing Score in the class / type GameCore.Player
            //Resulting output: "game:GameCore.Player.Score"
            //Example: In engine.dll, disable editing  the class / type GameEngine.Secrets
            //Resulting output: "engine:GameEngine.Secrets"
            "",

        };
    }
}

