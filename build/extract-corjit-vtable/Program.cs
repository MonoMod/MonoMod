using ClangSharp;
using ClangSharp.Interop;

if (args is not [{ } dotnetRepoRoot, var layout, ..])
{
    await StdErr.WriteLineAsync("takes 1 arguments: dotnetRepoRoot [layout]");
    return 1;
}

string incPath;
switch (layout)
{
    case "coreclr":
        incPath = Path.Combine(dotnetRepoRoot, "src", "inc");
        break;

    case "runtime-5":
        incPath = Path.Combine(dotnetRepoRoot, "src", "coreclr", "src", "inc");
        break;

    case "runtime":
    case null:
        incPath = Path.Combine(dotnetRepoRoot, "src", "coreclr", "inc");
        break;
    default:
        await StdErr.WriteLineAsync($"unknown layout '{layout}'");
        return 2;
}

using var index = CXIndex.Create();
var coreclrInc = incPath;
var corjitScanH = Path.Combine(coreclrInc, "corjit-scan.h");
var cxtu = CXTranslationUnit.CreateFromSourceFile(index, corjitScanH,
    ["-x", "c++", "-std=c++17"],
    [CXUnsavedFile.Create(corjitScanH, """
    typedef __SIZE_TYPE__ size_t;
    typedef __INT8_TYPE__ int8_t;
    typedef __UINT8_TYPE__ uint8_t;
    typedef __INT16_TYPE__ int16_t;
    typedef __UINT16_TYPE__ uint16_t;
    typedef __INT32_TYPE__ int32_t;
    typedef __UINT32_TYPE__ uint32_t;
    typedef __INT64_TYPE__ int64_t;
    typedef __UINT64_TYPE__ uint64_t;
    typedef void *LPVOID;
    typedef unsigned long DWORD;
    typedef unsigned short WORD;
    typedef unsigned short USHORT;
    typedef unsigned char BYTE;
    typedef unsigned long ULONG;
    typedef long LONG;
    typedef unsigned int ULONG32;
    typedef int INT;
    typedef size_t SIZE_T;
    typedef wchar_t WCHAR, *PWCHAR;
    typedef int BOOL;
    typedef char const *LPCSTR;
    typedef char *LPCSTR;
    typedef wchar_t const *LPCWSTR;
    typedef wchar_t *LPCWSTR;
    typedef DWORD HRESULT:
    #include "corinfo.h"
    #include "corjit.h"
    """)]);

if (cxtu.NumDiagnostics > 0)
{
    await StdOut.WriteLineAsync($"Loaded sources with {cxtu.NumDiagnostics} diagnostics:");
    await StdOut.WriteLineAsync($"---");
    foreach (var diag in cxtu.DiagnosticSet)
    {
        var str = diag.Format(CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation
            | CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation
            | CXDiagnosticDisplayOptions.CXDiagnostic_DisplayCategoryId);
        await StdOut.WriteLineAsync(str.ToString());
    }
    await StdOut.WriteLineAsync($"---");
}

using var tu = TranslationUnit.GetOrCreate(cxtu);

foreach (var decl in tu.TranslationUnitDecl.Decls)
{
    if (decl.Kind is not CX_DeclKind.CX_DeclKind_CXXRecord) continue;
    var record = (CXXRecordDecl)decl.MostRecentDecl;
    if (record.Name is not "ICorJitInfo") continue;

    record.Extent.Start.GetSpellingLocation(out var file, out var line, out var column, out _);
    await StdOut.WriteLineAsync($"Found ICorJitInfo at {file.Name}:{line}:{column}");

    // we're now looking at ICorJitInfo, lets try to extract its vtable

    // need to collect its bases so we can enumerate them in order
    var toProcess = new Queue<CXXRecordDecl>();
    var baseStack = new Stack<CXXRecordDecl>();
    toProcess.Enqueue(record);
    while (toProcess.TryDequeue(out var cur))
    {
        baseStack.Push(cur);
        for (var i = cur.Bases.Count - 1; i >= 0; i--)
        {
            var @base = cur.Bases[i];
            toProcess.Enqueue((CXXRecordDecl)@base.Referenced.MostRecentDecl);
        }
    }

    // now that we have all bases in order, lets go through and enumerate them
    var vtblIdx = 0;
    while (baseStack.TryPop(out record))
    {
        record.Extent.Start.GetSpellingLocation(out file, out line, out column, out _);
        await StdOut.WriteLineAsync("//");
        await StdOut.WriteLineAsync($"// {Path.GetRelativePath(dotnetRepoRoot, file.Name.CString)}:{line}");
        await StdOut.WriteLineAsync($"// class {record.Name}");

        foreach (var method in record.Methods)
        {
            if (!method.IsVirtual) continue;

            var methodStr = $"{method.ReturnType.ToString()} {method.Name}(";
            foreach (var param in method.Parameters)
            {
                if (methodStr[^1] is not '(')
                {
                    methodStr += ", ";
                }
                methodStr += param.Type.ToString();
            }

            await StdOut.WriteLineAsync($"// {vtblIdx++:X2}: {methodStr})");
            if (method.Name is "allocMem")
            {
                await StdOut.WriteLineAsync($"public const int AllocMemIndex = 0x{vtblIdx - 1:X};");
            }
            if (method.Name is "getJitFlags")
            {
                await StdOut.WriteLineAsync($"public const int GetJitFlagsIndex = 0x{vtblIdx - 1:X};");
            }
        }
    }

    await StdOut.WriteLineAsync("");
    await StdOut.WriteLineAsync($"public const int TotalVtableCount = 0x{vtblIdx:X};");


    break;
}

await StdOut.WriteLineAsync("");
await StdOut.WriteLineAsync("(Done!)");

return 0;