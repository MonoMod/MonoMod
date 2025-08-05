extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Github
{
    public class Issue221 : TestBase
    {
        public Issue221(ITestOutputHelper helper) : base(helper)
        {
        }

        private class Player
        {
            private float RunMaxSpeed
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get
                {
                    return 40;
                }
            }
            public void Update()
            {
                Console.WriteLine("Before RunMaxSpeed");
                if (RunMaxSpeed <= 40)
                {
                    Console.WriteLine("Run");
                }
                Console.WriteLine("After RunMaxSpeed");
            }
        }

        [Fact]
        public void GlueAbiFixupHookingPrivateGetPropertyDoesNotThrow()
        {
            var prop = typeof(Player).GetProperty("RunMaxSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            using var runMaxSpeed = new Hook(prop.GetGetMethod(true), RunMaxSpeed_patch);
            var player = new Player();
            player.Update();
        }

        private static float RunMaxSpeed_patch(Func<Player, float> orig, Player player)
        {
            Console.WriteLine("Go through");
            return orig(player);
        }
    }
}
