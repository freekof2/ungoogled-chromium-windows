using System.IO;
using FpBrowserLauncher.Models;
using FpBrowserLauncher.Services;
using Xunit;

namespace FpBrowserLauncher.Tests;

public sealed class ChromiumLauncherTests
{
    [Fact]
    public void FilterConflictingFlags_RemovesProtectedFlags()
    {
        var filtered = ChromiumLauncher.FilterConflictingFlags([
            "--user-data-dir=C:/leak",
            "--fp-config=C:/bad.json",
            "--proxy-server=socks5://127.0.0.1:1080",
            "--disable-notifications",
            " --lang=zh-CN "
        ]).ToList();

        Assert.DoesNotContain(filtered, flag => flag.StartsWith("--user-data-dir", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, flag => flag.StartsWith("--fp-config", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, flag => flag.StartsWith("--proxy-server", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("--disable-notifications", filtered);
        Assert.Contains("--lang=zh-CN", filtered);
    }

    [Fact]
    public void BuildCommandLineArguments_AddsProtectedProfileArgumentsFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "fp-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProfileStore(root);
            var launcher = new ChromiumLauncher(store, new AlwaysAliveProxyTester(), new ProcessTracker(), new LaunchSnapshotWriter(store));
            var fingerprint = new FingerprintGenerator().Generate("p001");
            fingerprint.ExtraFlags = ["--disable-notifications", "--fp-config=C:/bad.json"];

            var args = launcher.BuildCommandLineArguments("p001", fingerprint);

            Assert.StartsWith("--user-data-dir=", args[0]);
            Assert.StartsWith("--fp-config=", args[1]);
            Assert.Contains("--no-first-run", args);
            Assert.Contains("--disable-background-networking", args);
            Assert.Contains("--disable-notifications", args);
            Assert.DoesNotContain(args.Skip(2), arg => arg.StartsWith("--fp-config=C:/bad.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCommandLineArguments_DoesNotMapRealUiLanguageToLangFlag()
    {
        var root = Path.Combine(Path.GetTempPath(), "fp-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProfileStore(root);
            var launcher = new ChromiumLauncher(store, new AlwaysAliveProxyTester(), new ProcessTracker(), new LaunchSnapshotWriter(store));
            var fingerprint = new FingerprintGenerator().Generate("p001");
            fingerprint.UiLanguage = new ModeValue { Mode = "real", Value = "en-US" };

            var args = launcher.BuildCommandLineArguments("p001", fingerprint);

            Assert.DoesNotContain(args, arg => arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCommandLineArguments_MapsFingerprintSettingsToChromiumFlags()
    {
        var root = Path.Combine(Path.GetTempPath(), "fp-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProfileStore(root);
            var launcher = new ChromiumLauncher(store, new AlwaysAliveProxyTester(), new ProcessTracker(), new LaunchSnapshotWriter(store));
            var fingerprint = new FingerprintGenerator().Generate("p001");
            fingerprint.UiLanguage = new ModeValue { Mode = "custom", Value = "en-US" };
            fingerprint.Resolution = new ResolutionValue { Mode = "custom", Width = 1440, Height = 900 };
            fingerprint.HardwareAcceleration = "disabled";
            fingerprint.WebRtc = new ModeValue { Mode = "proxy_udp" };
            fingerprint.DoNotTrack = "enabled";
            fingerprint.ExtraFlags = ["--disable-notifications"];

            var args = launcher.BuildCommandLineArguments("p001", fingerprint);

            Assert.Contains("--lang=en-US", args);
            Assert.Contains("--window-size=1440,900", args);
            Assert.Contains("--disable-gpu", args);
            Assert.Contains("--force-webrtc-ip-handling-policy=disable_non_proxied_udp", args);
            Assert.Contains("--enable-do-not-track", args);
            Assert.Contains("--disable-notifications", args);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
