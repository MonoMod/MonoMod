namespace MonoMod.Core.Platforms.Runtimes
{
    internal sealed class FxCLR2Runtime : FxBaseRuntime
    {

        private readonly ISystem system;
        public FxCLR2Runtime(ISystem system) : base()
        {
            this.system = system;

            if (AbiCore is null)
            {
                // TODO: where is the generic context passed on CLR 2?
                AbiCore = system.DefaultAbi;
            }
        }
        /*
        public override RuntimeFeature Features => base.Features & ~RuntimeFeature.DisableInlining;
        // TODO: figure out how to disable inlining on CLR 2
        public override void DisableInlining(MethodBase method) {
            throw new PlatformNotSupportedException();
        }
        */
    }
}
