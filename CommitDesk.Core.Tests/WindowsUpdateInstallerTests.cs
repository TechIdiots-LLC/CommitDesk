using CommitDesk.Core.Updates;

namespace CommitDesk.Core.Tests;

/// <summary>
/// Signature checks against real signed files. Run with GLD_SIG_DIR pointing at a folder holding app.exe (a signed copy
/// of the app), setup.exe (an installer signed with the same certificate) and tampered.exe (that installer with a byte
/// changed); they are skipped otherwise.
/// </summary>
public class WindowsUpdateInstallerTests
{
    static string? Dir => Environment.GetEnvironmentVariable("GLD_SIG_DIR");
    static string F(string name) => Path.Combine(Dir!, name);

    [Fact]
    public void AcceptsInstallerSignedWithTheAppsOwnCertificate()
    {
        if (Dir is null || !OperatingSystem.IsWindows()) return;
        WindowsUpdateInstaller.VerifyPublisher(F("setup.exe"), F("app.exe"));
    }

    [Fact]
    public void RejectsTamperedInstaller()
    {
        if (Dir is null || !OperatingSystem.IsWindows()) return;
        var ex = Assert.Throws<InvalidOperationException>(() => WindowsUpdateInstaller.VerifyPublisher(F("tampered.exe"), F("app.exe")));
        Assert.Contains("isn't validly signed", ex.Message);
    }

    [Fact]
    public void RejectsUnsignedInstaller()
    {
        if (Dir is null || !OperatingSystem.IsWindows()) return;
        var unsigned = Path.Combine(Path.GetTempPath(), "gld-unsigned-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.WriteAllBytes(unsigned, File.ReadAllBytes(F("setup.exe"))[..4096]);
        try
        {
            Assert.Throws<InvalidOperationException>(() => WindowsUpdateInstaller.VerifyPublisher(unsigned, F("app.exe")));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }

    [Fact]
    public void RejectsTrustedInstallerFromAnotherPublisher()
    {
        if (Dir is null || !OperatingSystem.IsWindows()) return;
        // dotnet.exe is Authenticode-signed by Microsoft: fully trusted, but not this app's publisher.
        var other = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "dotnet.exe");
        if (!File.Exists(other)) other = @"C:\Program Files\dotnet\dotnet.exe";
        var ex = Assert.Throws<InvalidOperationException>(() => WindowsUpdateInstaller.VerifyPublisher(other, F("app.exe")));
        Assert.Contains("different publisher", ex.Message);
    }

    [Fact]
    public void UnsignedAppSkipsTheCheck()
    {
        if (Dir is null || !OperatingSystem.IsWindows()) return;
        var unsignedApp = Path.Combine(Path.GetTempPath(), "gld-app-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.WriteAllBytes(unsignedApp, [0x4D, 0x5A]);
        try
        {
            WindowsUpdateInstaller.VerifyPublisher(F("tampered.exe"), unsignedApp);   // no exception
        }
        finally
        {
            File.Delete(unsignedApp);
        }
    }
}
