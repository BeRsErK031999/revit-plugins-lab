using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace ModelSheetPublisher
{
    // AcMgd document-management extensions are only available in the full AutoCAD host.
    // Keep these outside the plotting method so single-document Core Console tests can run.
    internal static class HostDocuments
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Document Open(string path) => AcApp.DocumentManager.Open(path, true);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Close(Document document) => document.CloseAndDiscard();
    }
}
