using MonoMod.Core.Utils;
using System;

var platArch = PlatformDetection.Architecture;
var platOs = PlatformDetection.OS;
var platRuntime = PlatformDetection.Runtime;
var platRuntimeVer = PlatformDetection.RuntimeVersion;

Console.WriteLine($"Running on {platOs} {platArch} {platRuntime} {platRuntimeVer}");
