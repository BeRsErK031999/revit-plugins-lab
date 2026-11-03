using System.Runtime.InteropServices;

namespace TrueBIM.App.Modules.Print.Services;

// Some Windows PDF drivers ask for a path when Revit leaves DOCINFO.lpszOutput empty.
// Supply the private spool path only on the calling thread, during this one SubmitPrint.
internal sealed class DriverOutputFile : IDisposable
{
    private const string Runtime = "TrueBIM.PrintOutput.x64.dll";
    private readonly IntPtr handle = Marshal.AllocHGlobal(IntPtr.Size);
    private readonly IntPtr output;
    private readonly StartDocument handler;

    public DriverOutputFile(string path)
    {
        output = Marshal.StringToHGlobalUni(path);
        handler = Start;
        Marshal.WriteIntPtr(handle, IntPtr.Zero);
        try
        {
            var entry = GetProcAddress(GetModuleHandle("gdi32.dll"), "StartDocW");
            if (entry == IntPtr.Zero) throw new InvalidOperationException("В Windows недоступна печать GDI.");
            Check(LhInstallHook(entry, Marshal.GetFunctionPointerForDelegate(handler), IntPtr.Zero, handle));
            Check(LhSetInclusiveACL(new[] { GetCurrentThreadId() }, 1, handle));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private int Start(IntPtr dc, ref DocumentInfo info)
    {
        var fileJob = info;
        fileJob.Output = output;
        // EasyHook forwards reentrant calls from the handler to the original GDI entry point.
        return StartDocW(dc, ref fileJob);
    }

    public void Dispose()
    {
        if (Marshal.ReadIntPtr(handle) != IntPtr.Zero)
        {
            LhUninstallHook(handle);
            LhWaitForPendingRemovals();
        }
        Marshal.FreeHGlobal(output);
        Marshal.FreeHGlobal(handle);
        GC.KeepAlive(handler);
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new InvalidOperationException($"Не удалось передать драйверу имя файла (0x{status:X8}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DocumentInfo
    {
        public int Size;
        public IntPtr Name, Output, DataType;
        public int Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate int StartDocument(IntPtr dc, ref DocumentInfo info);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int StartDocW(IntPtr dc, ref DocumentInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport(Runtime), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static extern int LhInstallHook(IntPtr entry, IntPtr handler, IntPtr context, IntPtr handle);
    [DllImport(Runtime), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static extern int LhSetInclusiveACL(uint[] threads, int count, IntPtr handle);
    [DllImport(Runtime), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static extern int LhUninstallHook(IntPtr handle);
    [DllImport(Runtime), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static extern int LhWaitForPendingRemovals();
}
