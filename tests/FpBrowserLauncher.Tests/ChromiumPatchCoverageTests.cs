using System.IO;
using Xunit;

namespace FpBrowserLauncher.Tests;

public sealed class ChromiumPatchCoverageTests
{
    [Fact]
    public void FingerprintPatchesConsumeLauncherFingerprintSettings()
    {
        var patches = ReadFingerprintPatches();

        Assert.Contains("webrtc.mode", patches);
        Assert.Contains("timezone.mode", patches);
        Assert.Contains("timezone.value", patches);
        Assert.Contains("geolocation.mode", patches);
        Assert.Contains("geolocation.prompt_policy", patches);
        Assert.Contains("geolocation.latitude", patches);
        Assert.Contains("geolocation.longitude", patches);
        Assert.Contains("language.mode", patches);
        Assert.Contains("ui_language.mode", patches);
        Assert.Contains("ui_language.value", patches);
        Assert.Contains("resolution.mode", patches);
        Assert.Contains("fonts.mode", patches);
        Assert.Contains("webgl.mode", patches);
        Assert.Contains("webgpu.mode", patches);
        Assert.Contains("noise_toggles", patches);
        Assert.Contains("cpu_cores", patches);
        Assert.Contains("device_memory_gb", patches);
        Assert.Contains("device_name", patches);
        Assert.Contains("mac_address", patches);
        Assert.Contains("do_not_track", patches);
        Assert.Contains("port_scan_protection", patches);
        Assert.Contains("disabled", patches);
        Assert.Contains("hardware_acceleration", patches);
        Assert.Contains("tls_fingerprint.mode", patches);
    }

    private static string ReadFingerprintPatches()
    {
        var directory = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var patchDirectory = Path.Combine(directory.FullName, "patches", "extra", "fp-browser");
        return string.Join('\n', Directory.GetFiles(patchDirectory, "*.patch").Select(File.ReadAllText));
    }

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "patches", "extra", "fp-browser")))
            {
                return current;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing patches/extra/fp-browser.");
    }
}
