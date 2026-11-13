using System.Threading;
using System.Reflection;
using System.IO;

// No models, UI or child processes. Optional DLL load exercises a real .NET 4.8
// image mapping; only the test's ready marker is written.
internal static class ProcessFixture
{
    private static void Main(string[] args)
    {
        if (args.Length == 2)
        {
            Assembly.LoadFile(Path.GetFullPath(args[0]));
            File.WriteAllText(args[1], "ready");
        }
        Thread.Sleep(600000);
    }
}
