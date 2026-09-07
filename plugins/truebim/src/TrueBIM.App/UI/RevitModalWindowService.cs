using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrueBIM.App.UI;

internal static class RevitModalWindowService
{
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public static bool? ShowDialog(Window window, IntPtr revitWindowHandle)
    {
        Guard.NotNull(window, nameof(window));

        if (revitWindowHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(window).Owner = revitWindowHandle;
        }

        try
        {
            return window.ShowDialog();
        }
        finally
        {
            RestoreRevitWindow(revitWindowHandle);
        }
    }

    private static void RestoreRevitWindow(IntPtr revitWindowHandle)
    {
        if (revitWindowHandle == IntPtr.Zero || !IsWindow(revitWindowHandle))
        {
            return;
        }

        if (IsIconic(revitWindowHandle))
        {
            ShowWindow(revitWindowHandle, SwRestore);
        }
        else if (!IsWindowVisible(revitWindowHandle))
        {
            ShowWindow(revitWindowHandle, SwShow);
        }

        SetForegroundWindow(revitWindowHandle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
