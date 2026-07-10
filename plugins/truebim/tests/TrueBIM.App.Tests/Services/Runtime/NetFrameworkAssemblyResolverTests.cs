using System.Reflection;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using Xunit;

namespace TrueBIM.App.Tests.Services.Runtime;

public sealed class NetFrameworkAssemblyResolverTests
{
    [Fact]
    public void FindBundledAssemblyPath_UsesBundledDependencyForVersionMismatch()
    {
        using TempDirectory temp = new();
        string requestingAssemblyPath = CreateFile(temp.Path, "System.Memory.dll");
        string bundledAssemblyPath = CreateFile(temp.Path, "System.Runtime.CompilerServices.Unsafe.dll");

        string? result = FindBundledAssemblyPath(
            "System.Runtime.CompilerServices.Unsafe, Version=4.0.4.1, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            requestingAssemblyPath,
            temp.Path);

        Assert.Equal(bundledAssemblyPath, result);
    }

    [Fact]
    public void FindBundledAssemblyPath_IgnoresRequestsFromOtherAddIns()
    {
        using TempDirectory trueBimDirectory = new();
        using TempDirectory otherAddInDirectory = new();
        CreateFile(trueBimDirectory.Path, "System.Runtime.CompilerServices.Unsafe.dll");
        string requestingAssemblyPath = CreateFile(otherAddInDirectory.Path, "System.Memory.dll");

        string? result = FindBundledAssemblyPath(
            "System.Runtime.CompilerServices.Unsafe, Version=4.0.4.1, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            requestingAssemblyPath,
            trueBimDirectory.Path);

        Assert.Null(result);
    }

    [Fact]
    public void FindBundledAssemblyPath_IgnoresNonBundledAssemblies()
    {
        using TempDirectory temp = new();
        string requestingAssemblyPath = CreateFile(temp.Path, "TrueBIM.App.dll");
        CreateFile(temp.Path, "Unknown.Dependency.dll");

        string? result = FindBundledAssemblyPath(
            "Unknown.Dependency, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            requestingAssemblyPath,
            temp.Path);

        Assert.Null(result);
    }

    [Fact]
    public void FindBundledAssemblyPath_IgnoresMissingBundledAssembly()
    {
        using TempDirectory temp = new();
        string requestingAssemblyPath = CreateFile(temp.Path, "System.Memory.dll");

        string? result = FindBundledAssemblyPath(
            "System.Runtime.CompilerServices.Unsafe, Version=4.0.4.1, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            requestingAssemblyPath,
            temp.Path);

        Assert.Null(result);
    }

    private static string? FindBundledAssemblyPath(
        string requestedAssemblyName,
        string requestingAssemblyPath,
        string addInDirectory)
    {
        Type resolverType = typeof(OpeningViewProfile).Assembly.GetType(
            "TrueBIM.App.Services.Runtime.NetFrameworkAssemblyResolver",
            throwOnError: true)!;
        MethodInfo resolverMethod = resolverType.GetMethod(
            "FindBundledAssemblyPath",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (string?)resolverMethod.Invoke(
            null,
            [requestedAssemblyName, requestingAssemblyPath, addInDirectory]);
    }

    private static string CreateFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-resolver-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
