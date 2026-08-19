using FpBrowserLauncher.Services;
using Xunit;

namespace FpBrowserLauncher.Tests;

public sealed class FingerprintGeneratorTests
{
    [Fact]
    public void Generate_WithSameProfileId_ReturnsStableFingerprint()
    {
        var generator = new FingerprintGenerator();

        var first = generator.Generate("p001");
        var second = generator.Generate("p001");

        Assert.Equal(first.ProfileSeed, second.ProfileSeed);
        Assert.Equal(first.UserAgent, second.UserAgent);
        Assert.Equal(first.Resolution.Width, second.Resolution.Width);
        Assert.Equal(first.Resolution.Height, second.Resolution.Height);
        Assert.Equal(first.WebGl.Vendor, second.WebGl.Vendor);
        Assert.Equal(first.WebGl.Renderer, second.WebGl.Renderer);
        Assert.Equal(first.CpuCores, second.CpuCores);
        Assert.Equal(first.DeviceMemoryGb, second.DeviceMemoryGb);
    }

    [Fact]
    public void Generate_UsesChromium151CompatibleDefaultUserAgent()
    {
        var fingerprint = new FingerprintGenerator().Generate("p001");

        Assert.Equal(FingerprintGenerator.DefaultUserAgent, fingerprint.UserAgent);
        Assert.Contains("Chrome/151.0.7922.71", fingerprint.UserAgent);
    }

    [Fact]
    public void DeriveSeed_NormalizesProfileId()
    {
        Assert.Equal(FingerprintGenerator.DeriveSeed("p001"), FingerprintGenerator.DeriveSeed(" P001 "));
    }
}
