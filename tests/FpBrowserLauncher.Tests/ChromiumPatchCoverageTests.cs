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

    [Fact]
    public void RegressionGuards_PreviousCompilerAndPatchFixesRemainPresent()
    {
        var patches = ReadFingerprintPatches();
        var patch05 = ReadPatch("05-noise-injection-framework.patch");
        var patch06 = ReadPatch("06-webgl-webgpu-metadata.patch");
        var patch11 = ReadPatch("11-udp-over-socks5.patch");
        var prepare = ReadRepositoryFile(Path.Combine(".github", "actions", "prepare", "action.yml"));
        var stage = ReadRepositoryFile(Path.Combine(".github", "actions", "stage", "index.js"));
        var stageDist = ReadRepositoryFile(Path.Combine(".github", "actions", "stage", "dist", "index.js"));
        var reusableBuild = ReadRepositoryFile(Path.Combine(".github", "workflows", "reusable-build.yml"));

        Assert.Contains("RawByteSpan()", patch05);
        Assert.DoesNotContain("image_data->data()->Data()", patch05);
        Assert.DoesNotContain("image_data->data()->length()", patch05);
        Assert.Contains("NotShared<DOMFloat32Array>", patch05);
        Assert.Contains("voice_list_.push_back", patch05);
        Assert.Contains("NoiseEnabled(\"speech_voices\")", patch05);
        Assert.DoesNotContain("voice_list_.clear();\n+  if (fp_config::NoiseEnabled(\"speech_voices\")", patch05);
        Assert.Contains("safe_size", patch05);
        Assert.Contains("ProxyHasCredentials", patch11);
        Assert.Contains("STATE_AUTH_WRITE", patch11);
        Assert.Contains("ERR_PROXY_CONNECTION_FAILED", patch11);
        Assert.DoesNotContain("fp_socks5_udp_client.h", patch11);
        Assert.Contains("base::as_byte_span", patches);
        Assert.DoesNotContain("FromUTF8(", patches);
        Assert.Contains("GPUAdapter", patch06);
        Assert.DoesNotContain("GPUAdapterInfo::setVendor", patch06);
        Assert.Contains("httplib2", prepare);
        Assert.Contains("PySocks", prepare);
        Assert.Contains("ignoreReturnCode", stage);
        Assert.Contains("ignoreReturnCode", stageDist);
        Assert.Contains("actions/cache@v5", reusableBuild);
    }

    private static string ReadFingerprintPatches()
    {
        var directory = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var patchDirectory = Path.Combine(directory.FullName, "patches", "extra", "fp-browser");
        return string.Join('\n', Directory.GetFiles(patchDirectory, "*.patch").Select(File.ReadAllText));
    }

    private static string ReadPatch(string fileName)
    {
        var directory = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        return File.ReadAllText(Path.Combine(directory.FullName, "patches", "extra", "fp-browser", fileName));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
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
