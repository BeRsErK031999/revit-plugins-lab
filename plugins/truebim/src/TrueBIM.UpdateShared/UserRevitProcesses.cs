using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TrueBIM.Updates;

public sealed class UserProcessIdentity
{
    public UserProcessIdentity(int processId, int sessionId, string name, string? ownerSid)
    {
        ProcessId = processId;
        SessionId = sessionId;
        Name = name;
        OwnerSid = ownerSid;
    }

    public int ProcessId { get; }
    public int SessionId { get; }
    public string Name { get; }
    public string? OwnerSid { get; }
}

public static class UserRevitProcesses
{
    public static IReadOnlyList<UserProcessIdentity> ReadCurrentUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("Не удалось определить пользователя Windows.");
        List<UserProcessIdentity> snapshot = ReadWindowsProcesses();
        using Process current = Process.GetCurrentProcess();
        if (!snapshot.Any(process => process.ProcessId == current.Id && process.OwnerSid == sid))
            throw new IOException("Windows не вернула текущего пользователя в списке процессов. Повторите проверку.");
        return FilterForUser(snapshot, sid, current.SessionId);
    }

    // One profile contains the add-in files for all sessions of the same SID.
    // Known owners are compared across all sessions. An unavailable owner blocks
    // only in this session; the installer also checks the target profile's files
    // before deleting/copying anything, covering inaccessible processes elsewhere.
    public static IReadOnlyList<UserProcessIdentity> FilterForUser(IEnumerable<UserProcessIdentity> processes, string currentSid, int currentSessionId)
    {
        if (string.IsNullOrWhiteSpace(currentSid)) throw new InvalidOperationException("Не удалось определить пользователя Windows.");
        List<UserProcessIdentity> result = new();
        foreach (UserProcessIdentity process in processes)
        {
            if (!string.Equals(process.Name, "Revit.exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(process.Name, "RevitCoreConsole.exe", StringComparison.OrdinalIgnoreCase)) continue;
            bool blocks = string.IsNullOrWhiteSpace(process.OwnerSid)
                ? process.SessionId == currentSessionId
                : string.Equals(process.OwnerSid, currentSid, StringComparison.OrdinalIgnoreCase);
            if (blocks) result.Add(process);
        }
        return result;
    }

    private static List<UserProcessIdentity> ReadWindowsProcesses()
    {
        // WTS exposes the owner SID with the process snapshot. Opening arbitrary
        // processes/tokens would fail for other terminal users and elevated processes.
        if (!WTSEnumerateProcessesW(IntPtr.Zero, 0, 1, out IntPtr buffer, out int count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось проверить процессы пользователя Windows.");
        try
        {
            List<UserProcessIdentity> result = new();
            int size = Marshal.SizeOf(typeof(WtsProcessInfo));
            for (int i = 0; i < count; i++)
            {
                WtsProcessInfo process = Marshal.PtrToStructure<WtsProcessInfo>(IntPtr.Add(buffer, checked(i * size)));
                string name = Marshal.PtrToStringUni(process.ProcessName) ?? "";
                string? owner = process.UserSid == IntPtr.Zero ? null : new SecurityIdentifier(process.UserSid).Value;
                result.Add(new UserProcessIdentity(process.ProcessId, process.SessionId, name, owner));
            }
            return result;
        }
        finally { WTSFreeMemory(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsProcessInfo
    {
        public int SessionId;
        public int ProcessId;
        public IntPtr ProcessName;
        public IntPtr UserSid;
    }

    [DllImport("wtsapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateProcessesW(IntPtr server, int reserved, int version, out IntPtr buffer, out int count);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr buffer);
}
