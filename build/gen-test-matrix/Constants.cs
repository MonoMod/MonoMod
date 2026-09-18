namespace GenTestMatrix;

internal static class Constants
{
    public const int MaxJobCountPerMatrix = 256;

    public static class NuGetSource
    {
        public const string NugetOrg = "nuget.org";
        public const string DotnetTools = "dotnet-tools";
    }

    public static class Tmpl
    {
        public const string RID = nameof(RID);
        public const string TFM = nameof(TFM);
        public const string DllPre = nameof(DllPre);
        public const string DllPost = nameof(DllPost);
    }

    public static class Mono
    {
        public const string NonCoreTFM = "net472";

        public static class Package
        {
            public const string NameTmpl = $"Microsoft.NETCore.App.Runtime.Mono.{{{Tmpl.RID}}}";
            public const string LibPathTmpl = $"runtimes/{{{Tmpl.RID}}}/lib/{{{Tmpl.TFM}}}/";
            public const string DllPathTmpl = $"runtimes/{{{Tmpl.RID}}}/native/{{{Tmpl.DllPre}}}coreclr{{{Tmpl.DllPost}}}";
        }
    }
}
