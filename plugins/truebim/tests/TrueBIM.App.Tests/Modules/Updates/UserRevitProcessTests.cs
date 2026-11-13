using System.Diagnostics;
using System.Security.Principal;
using TrueBIM.Updates;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Updates;

public sealed class UserRevitProcessTests
{
    private const string OwnSid = "S-1-5-21-100-200-300-1001";
    private const string OtherSid = "S-1-5-21-100-200-300-1002";

    [Fact]
    public void IsolatesAccountsButIncludesAllSessionsOfTheSameProfile()
    {
        UserProcessIdentity[] snapshot =
        {
            new(1, 3, "Revit.exe", OwnSid),
            new(2, 7, "revit.EXE", OwnSid),
            new(3, 3, "Revit.exe", OtherSid),
            new(4, 9, "RevitCoreConsole.exe", OtherSid),
            new(5, 7, "RevitCoreConsole.exe", OwnSid),
            new(6, 3, "AutodeskService.exe", OwnSid),
            new(7, 0, "System", null)
        };
        Assert.Equal(new[] { 1, 2, 5 }, UserRevitProcesses.FilterForUser(snapshot, OwnSid, 3).Select(process => process.ProcessId));
        Assert.Equal(new[] { 3, 4 }, UserRevitProcesses.FilterForUser(snapshot, OtherSid, 9).Select(process => process.ProcessId));
    }

    [Fact]
    public void ForeignRevitAloneDoesNotBlockInstallation()
    {
        Assert.Empty(UserRevitProcesses.FilterForUser(new[] { new UserProcessIdentity(1, 8, "Revit.exe", OtherSid) }, OwnSid, 3));
    }

    [Fact]
    public void MissingCurrentUserFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => UserRevitProcesses.FilterForUser(Array.Empty<UserProcessIdentity>(), "", 3));
    }

    [Theory]
    [InlineData("Revit.exe", null, 3, true)]
    [InlineData("RevitCoreConsole.exe", "", 3, true)]
    [InlineData("Revit.exe", null, 7, false)]
    [InlineData("RevitCoreConsole.exe", "", 7, false)]
    [InlineData("Revit.exe", OwnSid, 7, true)]
    [InlineData("RevitCoreConsole.exe", OwnSid, 7, true)]
    [InlineData("Revit.exe", OtherSid, 3, false)]
    [InlineData("Revit.exe", OtherSid, 7, false)]
    [InlineData("Unrelated.exe", null, 3, false)]
    public void TerminalSessionPolicy(string name, string? owner, int session, bool blocks)
    {
        UserProcessIdentity process = new(100, session, name, owner);
        Assert.Equal(blocks ? 1 : 0, UserRevitProcesses.FilterForUser(new[] { process }, OwnSid, 3).Count);
    }

    [Fact]
    public void UnknownRemoteOwnerDoesNotHideOwnProcessesOrFailTheSnapshot()
    {
        UserProcessIdentity[] snapshot =
        {
            new(1, 7, "Revit.exe", null),
            new(2, 3, "Revit.exe", OwnSid),
            new(3, 9, "RevitCoreConsole.exe", OwnSid),
            new(4, 3, "RevitCoreConsole.exe", null),
            new(5, 4, "Revit.exe", OtherSid)
        };
        Assert.Equal(new[] { 2, 3, 4 }, UserRevitProcesses.FilterForUser(snapshot, OwnSid, 3).Select(process => process.ProcessId));
    }

    [Fact]
    public async Task NativeSnapshotFindsBothOwnProcessKindsWithOwnerAndSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrueBIM-owner-tests-" + Guid.NewGuid().ToString("N"));
        List<(Process Process, string Directory)> fixtures = new();
        try
        {
            foreach (string name in new[] { "Revit.exe", "RevitCoreConsole.exe" })
            {
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, name);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "UpdateFixture", "TrueBIM-Setup-99.0.0.exe"), path);
                Process process = Process.Start(new ProcessStartInfo(path, "--hold")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory })!;
                fixtures.Add((process, directory));
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(Path.Combine(directory, "holding.txt")) && DateTime.UtcNow < deadline) await Task.Delay(20);
                Assert.False(process.HasExited);
                Assert.True(File.Exists(Path.Combine(directory, "holding.txt")));
            }
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            IReadOnlyList<UserProcessIdentity> snapshot = UserRevitProcesses.ReadCurrentUser();
            foreach ((Process process, _) in fixtures)
            {
                UserProcessIdentity found = Assert.Single(snapshot, item => item.ProcessId == process.Id);
                Assert.Equal(identity.User!.Value, found.OwnerSid);
                Assert.Equal(process.SessionId, found.SessionId);
            }
        }
        finally
        {
            foreach ((Process process, string directory) in fixtures)
            {
                File.WriteAllText(Path.Combine(directory, "release.txt"), "done");
                if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(); }
                process.Dispose();
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
