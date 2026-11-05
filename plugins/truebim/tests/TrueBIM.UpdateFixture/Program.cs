using System;
using System.IO;
using System.Linq;
using System.Threading;

// Harmless executable for update handoff tests; never included in the installer.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--hold"))
        {
            File.WriteAllText("holding.txt", "ready");
            DateTime deadline = DateTime.UtcNow.AddMinutes(3);
            while (!File.Exists("release.txt") && DateTime.UtcNow < deadline) Thread.Sleep(100);
            return 0;
        }
        File.WriteAllLines("launched.txt", args);
        return File.Exists("fail-install.txt") ? 8 : 0;
    }
}
