using System.Reflection;

namespace MonoMod.Core {
    public interface IDetourFactory {
        CoreDetour CreateDetour(MethodBase source, MethodBase dest);
    }
}
