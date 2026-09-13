using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Compiles generated source in memory so a test can assert that it builds, and then
/// drive the resulting type through reflection. Generating text that does not compile is
/// the failure mode that matters most here, so it is checked directly rather than by eye.
/// </summary>
internal static class GeneratedCodeCompiler
{
    private static readonly string[] ReferenceAssemblies =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Console",
        "System.Collections",
        "System.Linq",
        "System.Data.Common",
        "System.ComponentModel.Primitives",
        "System.ComponentModel.TypeConverter",
        "netstandard",
    ];

    internal static (Assembly? Assembly, IReadOnlyList<string> Errors) Compile(
        string source,
        string assemblyName,
        params Type[] extraReferenceTypes)
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddByLocation(string? location)
        {
            if (!string.IsNullOrEmpty(location) && seen.Add(location))
                references.Add(MetadataReference.CreateFromFile(location));
        }

        // Pull in the whole trusted-platform set: cheaper than curating a list, and the
        // generated file can legitimately touch anything in the BCL.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
                AddByLocation(path);
        }
        else
        {
            foreach (var name in ReferenceAssemblies)
            {
                try
                {
                    AddByLocation(Assembly.Load(name).Location);
                }
                catch (Exception)
                {
                    // A reference that cannot be loaded simply is not added; the compile
                    // below reports the resulting error with better context than we could.
                }
            }
        }

        foreach (var type in extraReferenceTypes)
            AddByLocation(type.Assembly.Location);

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} ({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}")
            .ToList();

        if (!result.Success)
            return (null, errors);

        stream.Position = 0;
        var assembly = new AssemblyLoadContext(assemblyName, isCollectible: false)
            .LoadFromStream(stream);

        return (assembly, errors);
    }
}
