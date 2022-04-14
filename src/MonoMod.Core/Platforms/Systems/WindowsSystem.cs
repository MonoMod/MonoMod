using MonoMod.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms.Systems {
    internal class WindowsSystem : ISystem {
        public OSKind Target => OSKind.Windows;

        public SystemFeature Features => SystemFeature.RWXPages;
    }
}
