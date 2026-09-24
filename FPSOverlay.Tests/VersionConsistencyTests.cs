using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace FPSOverlay.Tests;

public class VersionConsistencyTests
{
    [Fact]
    public void AppInfo_Version_Matches_Csproj()
    {
        string root = FindRepoRoot();
        string csproj = Path.Combine(root, "FPSOverlay.csproj");
        Assert.True(File.Exists(csproj), "FPSOverlay.csproj not found");

        var doc = XDocument.Load(csproj);
        string? csprojVersion = doc.Descendants("Version").FirstOrDefault()?.Value?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(csprojVersion));
        Assert.Equal(AppInfo.Version, csprojVersion);
    }

    [Fact]
    public void Installer_Version_Matches_AppInfo()
    {
        string iss = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer.iss"));
        Assert.Contains($"#define MyAppVersion     \"{AppInfo.Version}\"", iss);
    }

    [Fact]
    public void Assembly_InformationalVersion_StartsWith_AppInfo()
    {
        var asm = typeof(AppInfo).Assembly;
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString();
        Assert.NotNull(info);
        Assert.StartsWith(AppInfo.Version, info!);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FPSOverlay.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root with FPSOverlay.csproj");
    }
}
