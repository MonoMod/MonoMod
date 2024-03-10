using Mono.Cecil;

namespace MonoMod.Utils
{
    public interface ICallSiteGenerator
    {

        CallSite ToCallSite(ModuleDefinition module);

    }
}
