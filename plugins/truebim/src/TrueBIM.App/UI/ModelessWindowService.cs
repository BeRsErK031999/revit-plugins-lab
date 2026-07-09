using System.Windows;
using System.Windows.Interop;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.UI;

internal static class ModelessWindowService
{
    private static readonly Dictionary<string, Window> ActiveWindows = new(StringComparer.Ordinal);

    public static bool Activate(string key, ITrueBimLogger? logger = null)
    {
        Guard.NotNullOrWhiteSpace(key, nameof(key));

        if (!ActiveWindows.TryGetValue(key, out Window? window) || !window.IsVisible)
        {
            ActiveWindows.Remove(key);
            return false;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        logger?.Info($"Modeless window '{key}' is already open. Existing window was activated.");
        return true;
    }

    public static void Show(
        string key,
        Window window,
        Window? owner,
        ITrueBimLogger? logger = null)
    {
        Show(key, window, ResolveOwnerHandle(owner), logger);
    }

    public static void Show(
        string key,
        Window window,
        IntPtr ownerHandle,
        ITrueBimLogger? logger = null)
    {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(window, nameof(window));

        if (Activate(key, logger))
        {
            return;
        }

        if (ownerHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(window).Owner = ownerHandle;
        }

        window.ShowInTaskbar = true;
        ActiveWindows[key] = window;
        window.Closed += (_, _) =>
        {
            if (ActiveWindows.TryGetValue(key, out Window? activeWindow)
                && ReferenceEquals(activeWindow, window))
            {
                ActiveWindows.Remove(key);
            }
        };

        window.Show();
    }

    private static IntPtr ResolveOwnerHandle(Window? owner)
    {
        return owner is null
            ? IntPtr.Zero
            : new WindowInteropHelper(owner).Handle;
    }
}
