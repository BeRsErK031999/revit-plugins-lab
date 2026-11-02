using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ModelSheetPublisher.Bootstrap))]
namespace ModelSheetPublisher
{
    public sealed class Bootstrap : IExtensionApplication
    {
        private static readonly string Folder = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
        private static bool initialized;
        [ThreadStatic] private static int pdfDepth;
        internal static IDisposable PdfScope() { pdfDepth++; return new Scope(); }
        private sealed class Scope : IDisposable { public void Dispose() { pdfDepth--; } }
        public void Initialize()
        {
            EnsureInitialized();
            if (!IsCoreConsole) StartRibbon();
        }
        private static bool IsCoreConsole => System.Diagnostics.Process.GetCurrentProcess().ProcessName.Equals("accoreconsole", StringComparison.OrdinalIgnoreCase);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void StartRibbon() => RibbonIntegration.Start();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void StopRibbon() => RibbonIntegration.Stop();
        internal static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            if (Environment.GetEnvironmentVariable("MSP_DIAGNOSTICS") == "1") File.AppendAllText(Path.Combine(Path.GetTempPath(), "msp-trace.txt"), "BOOTSTRAP " + Folder + "\n");
        }
        public void Terminate() { if (!IsCoreConsole) StopRibbon(); AppDomain.CurrentDomain.AssemblyResolve -= Resolve; }
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (Environment.GetEnvironmentVariable("MSP_DIAGNOSTICS") == "1") File.AppendAllText(Path.Combine(Path.GetTempPath(), "msp-trace.txt"), "RESOLVE " + args.Name + " from " + args.RequestingAssembly?.Location + "\n");
            // AutoCAD's executable owns app.config. Redirect only requests originating in our bundle,
            // never another add-in or the host, to the shipped compatible dependency patch version.
            if (args.RequestingAssembly == null)
            {
                // Core Console omits RequestingAssembly. Restrict its fallback to our synchronous PDF operation.
                if (pdfDepth == 0) return null;
            }
            else if (string.IsNullOrEmpty(args.RequestingAssembly.Location)
                || !string.Equals(Path.GetDirectoryName(args.RequestingAssembly.Location), Folder, StringComparison.OrdinalIgnoreCase)) return null;
            var requested = new AssemblyName(args.Name);
            if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
            string path = Path.Combine(Folder, requested.Name + ".dll");
            if (!File.Exists(path)) return null;
            var available = AssemblyName.GetAssemblyName(path);
            if (!available.GetPublicKeyToken().SequenceEqual(requested.GetPublicKeyToken()) || available.Version < requested.Version) return null;
            return Assembly.LoadFrom(path);
        }
    }
}
