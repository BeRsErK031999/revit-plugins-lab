using System.IO;
using System.Reflection;

namespace TrueBIM.App.Services.Runtime;

internal static class NetFrameworkAssemblyResolver
{
    private static readonly HashSet<string> BundledAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACadSharp",
        "Microsoft.Bcl.AsyncInterfaces",
        "Microsoft.Bcl.HashCode",
        "System.Buffers",
        "System.Memory",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encodings.Web",
        "System.Text.Json",
        "System.Threading.Tasks.Extensions",
        "System.ValueTuple",
        "UglyToad.PdfPig",
        "UglyToad.PdfPig.Core",
        "UglyToad.PdfPig.DocumentLayoutAnalysis",
        "UglyToad.PdfPig.Fonts",
        "UglyToad.PdfPig.Package",
        "UglyToad.PdfPig.Tokenization",
        "UglyToad.PdfPig.Tokens"
    };
    private static readonly HashSet<string> LegacyUnsafeRequestSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Memory",
        "System.Threading.Tasks.Extensions"
    };

#if NETFRAMEWORK
    private static readonly object SyncRoot = new();
    private static ResolveEventHandler? resolveHandler;
#endif

    public static void Register()
    {
#if NETFRAMEWORK
        lock (SyncRoot)
        {
            if (resolveHandler is not null)
            {
                return;
            }

            string? addInDirectory = Path.GetDirectoryName(typeof(NetFrameworkAssemblyResolver).Assembly.Location);
            if (string.IsNullOrWhiteSpace(addInDirectory))
            {
                return;
            }

            resolveHandler = (_, args) => Resolve(args, addInDirectory);
            AppDomain.CurrentDomain.AssemblyResolve += resolveHandler;
        }
#endif
    }

    public static void Unregister()
    {
#if NETFRAMEWORK
        lock (SyncRoot)
        {
            if (resolveHandler is null)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve -= resolveHandler;
            resolveHandler = null;
        }
#endif
    }

    internal static string? FindBundledAssemblyPath(
        string requestedAssemblyName,
        string? requestingAssemblyPath,
        string addInDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedAssemblyName)
            || string.IsNullOrWhiteSpace(addInDirectory))
        {
            return null;
        }

        try
        {
            AssemblyName requestedName = new(requestedAssemblyName);
            if (string.IsNullOrWhiteSpace(requestedName.Name)
                || !BundledAssemblyNames.Contains(requestedName.Name))
            {
                return null;
            }

            string normalizedAddInDirectory = NormalizeDirectory(addInDirectory);
            if (!IsTrustedRequest(requestingAssemblyPath, normalizedAddInDirectory))
            {
                return null;
            }

            string candidatePath = Path.Combine(normalizedAddInDirectory, requestedName.Name + ".dll");
            return File.Exists(candidatePath) ? candidatePath : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or NotSupportedException)
        {
            return null;
        }
    }

#if NETFRAMEWORK
    private static Assembly? Resolve(ResolveEventArgs args, string addInDirectory)
    {
        string? requestingAssemblyPath = GetAssemblyLocation(args.RequestingAssembly)
            ?? FindLegacyUnsafeRequesterPath(args.Name, addInDirectory);
        string? candidatePath = FindBundledAssemblyPath(
            args.Name,
            requestingAssemblyPath,
            addInDirectory);
        if (candidatePath is null)
        {
            return null;
        }

        try
        {
            return Assembly.LoadFrom(candidatePath);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
            or FileLoadException
            or FileNotFoundException)
        {
            return null;
        }
    }

    private static string? FindLegacyUnsafeRequesterPath(string requestedAssemblyName, string addInDirectory)
    {
        try
        {
            AssemblyName requestedName = new(requestedAssemblyName);
            if (!string.Equals(
                    requestedName.Name,
                    "System.Runtime.CompilerServices.Unsafe",
                    StringComparison.OrdinalIgnoreCase)
                || requestedName.Version != new Version(4, 0, 4, 1))
            {
                return null;
            }

            string normalizedAddInDirectory = NormalizeDirectory(addInDirectory);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!LegacyUnsafeRequestSources.Contains(assembly.GetName().Name ?? string.Empty))
                {
                    continue;
                }

                string? assemblyPath = GetAssemblyLocation(assembly);
                string? assemblyDirectory = string.IsNullOrWhiteSpace(assemblyPath)
                    ? null
                    : Path.GetDirectoryName(assemblyPath);
                if (assemblyDirectory is not null
                    && string.Equals(
                        NormalizeDirectory(assemblyDirectory),
                        normalizedAddInDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return assemblyPath;
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or NotSupportedException)
        {
        }

        return null;
    }

    private static string? GetAssemblyLocation(Assembly? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        try
        {
            return assembly.Location;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
#endif

    private static string NormalizeDirectory(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsTrustedRequest(string? requestingAssemblyPath, string normalizedAddInDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestingAssemblyPath))
        {
            return true;
        }

        string fullRequestingPath = Path.GetFullPath(requestingAssemblyPath);
        string? requestingDirectory = Path.GetDirectoryName(fullRequestingPath);
        if (!string.IsNullOrWhiteSpace(requestingDirectory)
            && string.Equals(
                NormalizeDirectory(requestingDirectory),
                normalizedAddInDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string requestingAssemblyName = Path.GetFileNameWithoutExtension(fullRequestingPath);
        return BundledAssemblyNames.Contains(requestingAssemblyName);
    }
}
