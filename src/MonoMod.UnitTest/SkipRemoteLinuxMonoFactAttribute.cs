using MonoMod.Utils;
using System;
using Xunit;

namespace MonoMod.UnitTest
{
    public sealed class SkipRemoteLinuxMonoFactAttribute : FactAttribute
    {
        public SkipRemoteLinuxMonoFactAttribute(params string[] names)
        {
            Names = names;

            // FIXME: Some tests like to go horribly wrong on Azure Linux mono.

            if (Environment.GetEnvironmentVariable("AGENT_OS") == "Linux" &&
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY")) &&
                PlatformDetection.Runtime is RuntimeKind.Mono)
            {
                Skip = "Not supported on Azure Linux mono.";
                return;
            }
        }

        public string[] Names { get; }
    }
}

/*
2020-04-20T19:56:11.6209455Z ##[section]Starting: Test: monoslow: net452
2020-04-20T19:56:11.6216388Z ==============================================================================
2020-04-20T19:56:11.6216744Z Task         : Command line
2020-04-20T19:56:11.6217116Z Description  : Run a command line script using Bash on Linux and macOS and cmd.exe on Windows
2020-04-20T19:56:11.6217465Z Version      : 2.164.0
2020-04-20T19:56:11.6217719Z Author       : Microsoft Corporation
2020-04-20T19:56:11.6218120Z Help         : https://docs.microsoft.com/azure/devops/pipelines/tasks/utility/command-line
2020-04-20T19:56:11.6218575Z ==============================================================================
2020-04-20T19:56:11.7589611Z Generating script.
2020-04-20T19:56:11.7607982Z Script contents:
2020-04-20T19:56:11.7609469Z mono --debug ~/.nuget/packages/xunit.runner.console/2.4.1/tools/net452/xunit.console.exe MonoMod.UnitTest/bin/Release/net452/MonoMod.UnitTest.dll -xml testresults.monoslow.net452.xml -parallel none -appdomains denied -verbose
2020-04-20T19:56:11.7610625Z ========================== Starting Command Output ===========================
2020-04-20T19:56:11.7635557Z [command]/bin/bash --noprofile --norc /home/vsts/work/_temp/eb36b77f-9a20-4204-accb-f677b2d4ad94.sh
2020-04-20T19:56:11.9734648Z xUnit.net Console Runner v2.4.1 (64-bit Desktop .NET 4.5.2, runtime: 4.0.30319.42000)
2020-04-20T19:56:12.8859459Z   Discovering: MonoMod.UnitTest
2020-04-20T19:56:12.9447712Z   Discovered:  MonoMod.UnitTest
2020-04-20T19:56:12.9461937Z   Starting:    MonoMod.UnitTest
2020-04-20T19:56:13.0093053Z     MonoMod.UnitTest.ModInteropTest.TestModInterop [STARTING]
2020-04-20T19:56:13.0520199Z     MonoMod.UnitTest.ModInteropTest.TestModInterop [FINISHED] Time: 0.0219814s
2020-04-20T19:56:13.0539111Z     MonoMod.UnitTest.DynamicMethodDefinitionTest.TestDMD [STARTING]
2020-04-20T19:56:13.0566876Z Hello from ExampleMethod!
2020-04-20T19:56:13.0570383Z 
2020-04-20T19:56:13.2246491Z Hello from DynamicMethodDefinition!
2020-04-20T19:56:13.2246770Z 
2020-04-20T19:56:13.3108817Z Hello from DynamicMethodDefinition!
2020-04-20T19:56:13.3109596Z 
2020-04-20T19:56:13.3121393Z Hello from ExampleMethod!
2020-04-20T19:56:13.3121920Z 
2020-04-20T19:56:13.3123736Z     MonoMod.UnitTest.DynamicMethodDefinitionTest.TestDMD [FINISHED] Time: 0.2595968s
2020-04-20T19:56:13.3128175Z     MonoMod.UnitTest.DetourEmptyTest.TestDetoursEmpty [STARTING]
2020-04-20T19:56:13.3198560Z     MonoMod.UnitTest.DetourEmptyTest.TestDetoursEmpty [FINISHED] Time: 0.006678s
2020-04-20T19:56:13.3200637Z     MonoMod.UnitTest.DetourExtTest.TestDetoursExt [STARTING]
2020-04-20T19:56:13.3267086Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.3268254Z TestStaticMethod(2, 3): 12
2020-04-20T19:56:13.3281033Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.3281659Z TestStaticMethod(2, 3): 12
2020-04-20T19:56:13.3283864Z     MonoMod.UnitTest.DetourExtTest.TestDetoursExt [FINISHED] Time: 0.0080436s
2020-04-20T19:56:13.3284686Z     MonoMod.UnitTest.DetourMemoryTest.TestDetourMemory [STARTING]
2020-04-20T19:56:13.3294352Z     MonoMod.UnitTest.DetourMemoryTest.TestDetourMemory [FINISHED] Time: 0.000895s
2020-04-20T19:56:13.3296928Z     MonoMod.UnitTest.DetourOrderTest.TestDetoursOrder [STARTING]
2020-04-20T19:56:13.3537476Z     MonoMod.UnitTest.DetourOrderTest.TestDetoursOrder [FINISHED] Time: 0.0235615s
2020-04-20T19:56:13.3538569Z     MonoMod.UnitTest.DetourRedoTest.TestDetoursRedo [STARTING]
2020-04-20T19:56:13.3636417Z     MonoMod.UnitTest.DetourRedoTest.TestDetoursRedo [FINISHED] Time: 0.0092546s
2020-04-20T19:56:13.3636961Z     MonoMod.UnitTest.DetourTest.TestDetours [STARTING]
2020-04-20T19:56:13.3650027Z Detours: none
2020-04-20T19:56:13.3654900Z instance.TestMethod(2, 3): 5
2020-04-20T19:56:13.3655212Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.3655449Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.3655657Z 2 3 6
2020-04-20T19:56:13.3655809Z 1 2 2
2020-04-20T19:56:13.3655938Z 
2020-04-20T19:56:13.3831417Z Detours: A
2020-04-20T19:56:13.3831771Z instance.TestMethod(2, 3): 42
2020-04-20T19:56:13.3832051Z TestStaticMethod(2, 3): 12
2020-04-20T19:56:13.3832694Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.3832907Z Detour A
2020-04-20T19:56:13.3833157Z Testing trampoline, should invoke orig, TestVoidMethod(2, 3)
2020-04-20T19:56:13.3835912Z 2 3 12
2020-04-20T19:56:13.3836123Z Detour A
2020-04-20T19:56:13.3836244Z 
2020-04-20T19:56:13.3925312Z Detours: A + B
2020-04-20T19:56:13.3925680Z instance.TestMethod(2, 3): 120
2020-04-20T19:56:13.3925964Z TestStaticMethod(2, 3): 8
2020-04-20T19:56:13.3926202Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.3926419Z Detour B
2020-04-20T19:56:13.3926701Z Testing trampoline, should invoke hook A, TestVoidMethod(2, 3)
2020-04-20T19:56:13.3926974Z Detour A
2020-04-20T19:56:13.3927095Z 
2020-04-20T19:56:13.3939690Z Detours: B
2020-04-20T19:56:13.3939998Z instance.TestMethod(2, 3): 120
2020-04-20T19:56:13.3940259Z TestStaticMethod(2, 3): 8
2020-04-20T19:56:13.3940514Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.3940712Z Detour B
2020-04-20T19:56:13.3940978Z Testing trampoline, should invoke orig, TestVoidMethod(2, 3)
2020-04-20T19:56:13.3941266Z 2 3 8
2020-04-20T19:56:13.3941425Z Detour B
2020-04-20T19:56:13.3941543Z 
2020-04-20T19:56:13.3941722Z Detours: none
2020-04-20T19:56:13.3941955Z instance.TestMethod(2, 3): 5
2020-04-20T19:56:13.3942205Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.3942455Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.3942646Z 2 3 6
2020-04-20T19:56:13.3942811Z 1 2 2
2020-04-20T19:56:13.3942925Z 
2020-04-20T19:56:13.3949966Z     MonoMod.UnitTest.DetourTest.TestDetours [FINISHED] Time: 0.0296888s
2020-04-20T19:56:13.3950802Z     MonoMod.UnitTest.DisableInliningTest.TestDisableInlining [STARTING]
2020-04-20T19:56:13.3951356Z     MonoMod.UnitTest.DisableInliningTest.TestDisableInlining [SKIP]
2020-04-20T19:56:13.3951780Z       Unfinished
2020-04-20T19:56:13.3954373Z     MonoMod.UnitTest.DisableInliningTest.TestDisableInlining [FINISHED] Time: 0s
2020-04-20T19:56:13.3980960Z     MonoMod.UnitTest.HarmonyBridgeTest.TestHarmonyBridge [STARTING]
2020-04-20T19:56:13.6077070Z     MonoMod.UnitTest.HarmonyBridgeTest.TestHarmonyBridge [FINISHED] Time: 0.2131972s
2020-04-20T19:56:13.6077708Z     MonoMod.UnitTest.HookEndpointManagerTest.TestHookEndpointManager [STARTING]
2020-04-20T19:56:13.6249562Z     MonoMod.UnitTest.HookEndpointManagerTest.TestHookEndpointManager [FINISHED] Time: 0.0163782s
2020-04-20T19:56:13.6250276Z     MonoMod.UnitTest.HookTest.TestHooks [STARTING]
2020-04-20T19:56:13.6261971Z Hooks: none
2020-04-20T19:56:13.6262276Z instance.TestMethod(2, 3): 5
2020-04-20T19:56:13.6262534Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.6262817Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.6263012Z 2 3 6
2020-04-20T19:56:13.6263178Z 1 2 2
2020-04-20T19:56:13.6263291Z 
2020-04-20T19:56:13.6344954Z Hooks: A
2020-04-20T19:56:13.6345263Z instance.TestMethod(2, 3): 42
2020-04-20T19:56:13.6345565Z TestStaticMethod(2, 3): 12
2020-04-20T19:56:13.6345822Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.6346019Z Hook A
2020-04-20T19:56:13.6346152Z 
2020-04-20T19:56:13.6457784Z Hooks: A + B
2020-04-20T19:56:13.6458157Z instance.TestMethod(2, 3): 84
2020-04-20T19:56:13.6458415Z TestStaticMethod(2, 3): 14
2020-04-20T19:56:13.6458672Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.6458881Z Hook B
2020-04-20T19:56:13.6459037Z Hook A
2020-04-20T19:56:13.6459152Z 
2020-04-20T19:56:13.6459324Z Hooks: B
2020-04-20T19:56:13.6459548Z instance.TestMethod(2, 3): 47
2020-04-20T19:56:13.6459801Z TestStaticMethod(2, 3): 8
2020-04-20T19:56:13.6460048Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.6460241Z Hook B
2020-04-20T19:56:13.6460408Z 2 3 8
2020-04-20T19:56:13.6460916Z Hook B
2020-04-20T19:56:13.6461072Z 1 2 4
2020-04-20T19:56:13.6461185Z 
2020-04-20T19:56:13.6461359Z Detours: none
2020-04-20T19:56:13.6461591Z instance.TestMethod(2, 3): 5
2020-04-20T19:56:13.6461842Z TestStaticMethod(2, 3): 6
2020-04-20T19:56:13.6462087Z TestVoidMethod(2, 3):
2020-04-20T19:56:13.6462276Z 2 3 6
2020-04-20T19:56:13.6462439Z 1 2 2
2020-04-20T19:56:13.6462550Z 
2020-04-20T19:56:13.6462837Z     MonoMod.UnitTest.HookTest.TestHooks [FINISHED] Time: 0.0214132s
2020-04-20T19:56:13.6465690Z     MonoMod.UnitTest.ILHookTest.TestILHooks [STARTING]
2020-04-20T19:56:13.6504776Z     MonoMod.UnitTest.ILHookTest.TestILHooks [FINISHED] Time: 0.0035326s
2020-04-20T19:56:13.6505332Z     MonoMod.UnitTest.NativeDetourTest.TestNativeDetours [STARTING]
2020-04-20T19:56:13.6522393Z     MonoMod.UnitTest.NativeDetourTest.TestNativeDetours [FINISHED] Time: 0.0015948s
2020-04-20T19:56:13.6551148Z     MonoMod.UnitTest.MultiHookUnitTestAutomaticRegistration+OnIL.OnThenIL [STARTING]
2020-04-20T19:56:13.6583719Z 
2020-04-20T19:56:13.6585165Z =================================================================
2020-04-20T19:56:13.6585501Z 	Native Crash Reporting
2020-04-20T19:56:13.6585789Z =================================================================
2020-04-20T19:56:13.6586159Z Got a SIGSEGV while executing native code. This usually indicates
2020-04-20T19:56:13.6586532Z a fatal error in the mono runtime or one of the native libraries 
2020-04-20T19:56:13.6586820Z used by your application.
2020-04-20T19:56:13.6587135Z =================================================================
2020-04-20T19:56:13.6726008Z 
2020-04-20T19:56:13.6726563Z =================================================================
2020-04-20T19:56:13.6727037Z 	Native stacktrace:
2020-04-20T19:56:13.6727349Z =================================================================
2020-04-20T19:56:13.6728285Z 	0x5631e82d7265 - mono : (null)
2020-04-20T19:56:13.6728727Z 	0x5631e82d75fc - mono : (null)
2020-04-20T19:56:13.6729164Z 	0x5631e8282a21 - mono : (null)
2020-04-20T19:56:13.6729640Z 	0x5631e82d0bfb - mono : (null)
2020-04-20T19:56:13.6730021Z 	0x41944879 - Unknown
2020-04-20T19:56:13.6730186Z 
2020-04-20T19:56:13.6730435Z =================================================================
2020-04-20T19:56:13.6730733Z 	Telemetry Dumper:
2020-04-20T19:56:13.6731019Z =================================================================
2020-04-20T19:56:13.6737349Z Pkilling 0x7f9a1b9e7700 from 0x7f9a1fbb8780
2020-04-20T19:56:13.6741969Z Could not exec mono-hang-watchdog, expected on path '/etc/../bin/mono-hang-watchdog' (errno 2)
2020-04-20T19:56:13.6743422Z Pkilling 0x7f9a18b22700 from 0x7f9a1fbb8780
2020-04-20T19:56:13.6748231Z Pkilling 0x7f9a18f24700 from 0x7f9a1fbb8780
2020-04-20T19:56:13.6754187Z Pkilling 0x7f9a18d23700 from 0x7f9a1fbb8780
2020-04-20T19:56:13.6754861Z Pkilling 0x7f9a19125700 from 0x7f9a1fbb8780
2020-04-20T19:56:13.6948354Z Entering thread summarizer pause from 0x7f9a1fbb8780
2020-04-20T19:56:13.6952919Z Finished thread summarizer pause from 0x7f9a1fbb8780.
2020-04-20T19:56:13.7380547Z 
2020-04-20T19:56:13.7381347Z Waiting for dumping threads to resume
2020-04-20T19:56:14.7396281Z 
2020-04-20T19:56:14.7396840Z =================================================================
2020-04-20T19:56:14.7397178Z 	External Debugger Dump:
2020-04-20T19:56:14.7397489Z =================================================================
2020-04-20T19:56:14.7397911Z mono_gdb_render_native_backtraces not supported on this platform, unable to find gdb or lldb
2020-04-20T19:56:14.7419905Z 
2020-04-20T19:56:14.7421762Z =================================================================
2020-04-20T19:56:14.7422110Z 	Basic Fault Address Reporting
2020-04-20T19:56:14.7422429Z =================================================================
2020-04-20T19:56:14.7422922Z Memory around native instruction pointer (0x41944879):0x41944869  00 00 00 00 00 00 00 ff 25 00 00 00 00 a0 98 e0  ........%.......
2020-04-20T19:56:14.7423461Z 0x41944879  19 9a 7f 00 00 08 00 cf 0e 10 9a 7f 00 00 e8 f4  ................
2020-04-20T19:56:14.7424118Z 0x41944889  75 70 fe 08 30 9b 0c eb 31 56 00 00 00 00 00 00  up..0...1V......
2020-04-20T19:56:14.7424520Z 0x41944899  00 00 00 00 00 00 00 48 83 ec 28 4c 89 34 24 4c  .......H..(L.4$L
2020-04-20T19:56:14.7424772Z 
2020-04-20T19:56:14.7425020Z =================================================================
2020-04-20T19:56:14.7425326Z 	Managed Stacktrace:
2020-04-20T19:56:14.7425622Z =================================================================
2020-04-20T19:56:14.7426028Z 	  at <unknown> <0xffffffff>
2020-04-20T19:56:14.7426363Z 	  at System.Reflection.RuntimeMethodInfo:InternalInvoke <0x000db>
2020-04-20T19:56:14.7426755Z 	  at System.Reflection.RuntimeMethodInfo:Invoke <0x00114>
2020-04-20T19:56:14.7427128Z 	  at System.Reflection.MethodBase:Invoke <0x00047>
2020-04-20T19:56:14.7427738Z 	  at Xunit.Sdk.TestInvoker`1:CallTestMethod <0x00052>
2020-04-20T19:56:14.7428119Z 	  at <<InvokeTestMethodAsync>b__1>d:MoveNext <0x003f2>
2020-04-20T19:56:14.7428984Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder:Start <0x000df>
2020-04-20T19:56:14.7429431Z 	  at <>c__DisplayClass48_1:<InvokeTestMethodAsync>b__1 <0x0015b>
2020-04-20T19:56:14.7429800Z 	  at <AggregateAsync>d__4:MoveNext <0x000e2>
2020-04-20T19:56:14.7430199Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder:Start <0x000db>
2020-04-20T19:56:14.7430603Z 	  at Xunit.Sdk.ExecutionTimer:AggregateAsync <0x0018a>
2020-04-20T19:56:14.7430997Z 	  at <>c__DisplayClass48_1:<InvokeTestMethodAsync>b__0 <0x0016b>
2020-04-20T19:56:14.7431331Z 	  at <RunAsync>d__9:MoveNext <0x00095>
2020-04-20T19:56:14.7431716Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder:Start <0x000d3>
2020-04-20T19:56:14.7432135Z 	  at Xunit.Sdk.ExceptionAggregator:RunAsync <0x00182>
2020-04-20T19:56:14.7432487Z 	  at <InvokeTestMethodAsync>d__48:MoveNext <0x00353>
2020-04-20T19:56:14.7432907Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e7>
2020-04-20T19:56:14.7433333Z 	  at Xunit.Sdk.TestInvoker`1:InvokeTestMethodAsync <0x0019b>
2020-04-20T19:56:14.7433729Z 	  at Xunit.Sdk.XunitTestInvoker:InvokeTestMethodAsync <0x00113>
2020-04-20T19:56:14.7434094Z 	  at <<RunAsync>b__47_0>d:MoveNext <0x00772>
2020-04-20T19:56:14.7434482Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000ef>
2020-04-20T19:56:14.7434906Z 	  at Xunit.Sdk.TestInvoker`1:<RunAsync>b__47_0 <0x00163>
2020-04-20T19:56:14.7435251Z 	  at <RunAsync>d__10`1:MoveNext <0x0009c>
2020-04-20T19:56:14.7435640Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000d3>
2020-04-20T19:56:14.7436062Z 	  at Xunit.Sdk.ExceptionAggregator:RunAsync <0x00192>
2020-04-20T19:56:14.7436402Z 	  at Xunit.Sdk.TestInvoker`1:RunAsync <0x000f7>
2020-04-20T19:56:14.7436772Z 	  at Xunit.Sdk.XunitTestRunner:InvokeTestMethodAsync <0x000eb>
2020-04-20T19:56:14.7437135Z 	  at <InvokeTestAsync>d__4:MoveNext <0x001f5>
2020-04-20T19:56:14.7437526Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e3>
2020-04-20T19:56:14.7437956Z 	  at Xunit.Sdk.XunitTestRunner:InvokeTestAsync <0x0017a>
2020-04-20T19:56:14.7438307Z 	  at <>c__DisplayClass43_0:<RunAsync>b__0 <0x00038>
2020-04-20T19:56:14.7438640Z 	  at <RunAsync>d__10`1:MoveNext <0x000a4>
2020-04-20T19:56:14.7439033Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000df>
2020-04-20T19:56:14.7439437Z 	  at Xunit.Sdk.ExceptionAggregator:RunAsync <0x001cb>
2020-04-20T19:56:14.7439775Z 	  at <RunAsync>d__43:MoveNext <0x0054b>
2020-04-20T19:56:14.7440165Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000ef>
2020-04-20T19:56:14.7440561Z 	  at Xunit.Sdk.TestRunner`1:RunAsync <0x0018f>
2020-04-20T19:56:14.7440923Z 	  at Xunit.Sdk.XunitTestCaseRunner:RunTestAsync <0x000d7>
2020-04-20T19:56:14.7441252Z 	  at <RunAsync>d__19:MoveNext <0x003aa>
2020-04-20T19:56:14.7441644Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e7>
2020-04-20T19:56:14.7442059Z 	  at Xunit.Sdk.TestCaseRunner`1:RunAsync <0x00197>
2020-04-20T19:56:14.7442525Z 	  at Xunit.Sdk.XunitTestCase:RunAsync <0x000cf>
2020-04-20T19:56:14.7442900Z 	  at Xunit.Sdk.XunitTestMethodRunner:RunTestCaseAsync <0x000b2>
2020-04-20T19:56:14.7443259Z 	  at <RunTestCasesAsync>d__32:MoveNext <0x001cf>
2020-04-20T19:56:14.7443669Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000ef>
2020-04-20T19:56:14.7444093Z 	  at Xunit.Sdk.TestMethodRunner`1:RunTestCasesAsync <0x0018f>
2020-04-20T19:56:14.7444501Z 	  at <RunAsync>d__31:MoveNext <0x001d0>
2020-04-20T19:56:14.7444892Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000df>
2020-04-20T19:56:14.7445309Z 	  at Xunit.Sdk.TestMethodRunner`1:RunAsync <0x0018b>
2020-04-20T19:56:14.7445678Z 	  at Xunit.Sdk.XunitTestClassRunner:RunTestMethodAsync <0x00103>
2020-04-20T19:56:14.7446059Z 	  at <RunTestMethodsAsync>d__38:MoveNext <0x00a48>
2020-04-20T19:56:14.7446457Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x00107>
2020-04-20T19:56:14.7446895Z 	  at Xunit.Sdk.TestClassRunner`1:RunTestMethodsAsync <0x00197>
2020-04-20T19:56:14.7447242Z 	  at <RunAsync>d__37:MoveNext <0x003cd>
2020-04-20T19:56:14.7447621Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e7>
2020-04-20T19:56:14.7448036Z 	  at Xunit.Sdk.TestClassRunner`1:RunAsync <0x00197>
2020-04-20T19:56:14.7448411Z 	  at Xunit.Sdk.XunitTestCollectionRunner:RunTestClassAsync <0x0010b>
2020-04-20T19:56:14.7448804Z 	  at <RunTestClassesAsync>d__28:MoveNext <0x00460>
2020-04-20T19:56:14.7449217Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000ef>
2020-04-20T19:56:14.7449646Z 	  at Xunit.Sdk.TestCollectionRunner`1:RunTestClassesAsync <0x0018f>
2020-04-20T19:56:14.7450010Z 	  at <RunAsync>d__27:MoveNext <0x003ca>
2020-04-20T19:56:14.7450401Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e7>
2020-04-20T19:56:14.7450811Z 	  at Xunit.Sdk.TestCollectionRunner`1:RunAsync <0x00197>
2020-04-20T19:56:14.7451221Z 	  at Xunit.Sdk.XunitTestAssemblyRunner:RunTestCollectionAsync <0x000df>
2020-04-20T19:56:14.7451610Z 	  at <RunTestCollectionsAsync>d__42:MoveNext <0x00248>
2020-04-20T19:56:14.7452031Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000e7>
2020-04-20T19:56:14.7452477Z 	  at Xunit.Sdk.TestAssemblyRunner`1:RunTestCollectionsAsync <0x001ef>
2020-04-20T19:56:14.7452862Z 	  at Xunit.Sdk.XunitTestAssemblyRunner:<>n__0 <0x0003f>
2020-04-20T19:56:14.7453240Z 	  at <RunTestCollectionsAsync>d__14:MoveNext <0x0028f>
2020-04-20T19:56:14.7453663Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000db>
2020-04-20T19:56:14.7454099Z 	  at Xunit.Sdk.XunitTestAssemblyRunner:RunTestCollectionsAsync <0x001aa>
2020-04-20T19:56:14.7454469Z 	  at <RunAsync>d__41:MoveNext <0x0063c>
2020-04-20T19:56:14.7454847Z 	  at System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1:Start <0x000df>
2020-04-20T19:56:14.7455266Z 	  at Xunit.Sdk.TestAssemblyRunner`1:RunAsync <0x0019f>
2020-04-20T19:56:14.7455613Z 	  at <RunTestCases>d__8:MoveNext <0x0015b>
2020-04-20T19:56:14.7455992Z 	  at System.Runtime.CompilerServices.AsyncVoidMethodBuilder:Start <0x000cb>
2020-04-20T19:56:14.7456429Z 	  at Xunit.Sdk.XunitTestFrameworkExecutor:RunTestCases <0x0021a>
2020-04-20T19:56:14.7456812Z 	  at Xunit.Sdk.TestFrameworkExecutor`1:RunTests <0x000b5>
2020-04-20T19:56:14.7457150Z 	  at Xunit.Xunit2:RunTests <0x00070>
2020-04-20T19:56:14.7457478Z 	  at Xunit.XunitFrontController:RunTests <0x0005d>
2020-04-20T19:56:14.7457815Z 	  at TestFrameworkExtensions:RunTests <0x00060>
2020-04-20T19:56:14.7458192Z 	  at Xunit.ConsoleClient.ConsoleRunner:ExecuteAssembly <0x00f87>
2020-04-20T19:56:14.7458593Z 	  at Xunit.ConsoleClient.ConsoleRunner:RunProject <0x00843>
2020-04-20T19:56:14.7458969Z 	  at Xunit.ConsoleClient.ConsoleRunner:EntryPoint <0x00817>
2020-04-20T19:56:14.7459339Z 	  at Xunit.ConsoleClient.Program:Main <0x00107>
2020-04-20T19:56:14.7459665Z 	  at <Module>:runtime_invoke_int_object <0x00091>
2020-04-20T19:56:14.7460100Z =================================================================
2020-04-20T19:56:14.9694228Z /home/vsts/work/_temp/eb36b77f-9a20-4204-accb-f677b2d4ad94.sh: line 1:  3900 Aborted                 (core dumped) mono --debug ~/.nuget/packages/xunit.runner.console/2.4.1/tools/net452/xunit.console.exe MonoMod.UnitTest/bin/Release/net452/MonoMod.UnitTest.dll -xml testresults.monoslow.net452.xml -parallel none -appdomains denied -verbose
2020-04-20T19:56:14.9717897Z 
2020-04-20T19:56:14.9800877Z ##[error]Bash exited with code '134'.
2020-04-20T19:56:14.9816327Z ##[section]Finishing: Test: monoslow: net452
*/
