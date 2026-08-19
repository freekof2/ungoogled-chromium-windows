using System.Security.Cryptography;
using System.Text;
using FpBrowserLauncher.Models;

namespace FpBrowserLauncher.Services;

public sealed class FingerprintGenerator
{
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.7922.71 Safari/537.36";

    private static readonly DeviceSample[] Samples =
    [
        new(
            UserAgent: DefaultUserAgent,
            WebGlVendor: "Google Inc. (NVIDIA)",
            WebGlRenderer: "ANGLE (NVIDIA, NVIDIA GeForce GTX 1660 Direct3D11 vs_5_0 ps_5_0)",
            Fonts: ["Arial", "Microsoft YaHei", "SimSun", "Calibri", "Segoe UI", "Times New Roman"],
            Resolutions: [(1920, 1080), (2560, 1440)],
            CpuCores: [6, 8, 12],
            MemoryGb: [8, 16, 32],
            Timezone: "Asia/Shanghai",
            Language: "zh-CN"),
        new(
            UserAgent: DefaultUserAgent,
            WebGlVendor: "Google Inc. (AMD)",
            WebGlRenderer: "ANGLE (AMD, AMD Radeon RX 580 Direct3D11 vs_5_0 ps_5_0)",
            Fonts: ["Arial", "Microsoft YaHei", "SimSun", "Calibri", "Segoe UI", "Verdana"],
            Resolutions: [(1920, 1080), (1600, 900)],
            CpuCores: [4, 6, 8],
            MemoryGb: [8, 16],
            Timezone: "Asia/Shanghai",
            Language: "zh-CN"),
        new(
            UserAgent: DefaultUserAgent,
            WebGlVendor: "Google Inc. (Intel)",
            WebGlRenderer: "ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0)",
            Fonts: ["Arial", "Microsoft YaHei", "SimSun", "Calibri", "Segoe UI", "Tahoma"],
            Resolutions: [(1366, 768), (1920, 1080)],
            CpuCores: [4, 8],
            MemoryGb: [8, 16],
            Timezone: "Asia/Shanghai",
            Language: "zh-CN")
    ];

    public FingerprintConfig Generate(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID 不能为空。", nameof(profileId));
        }

        var seed = DeriveSeed(profileId);
        var random = CreateDeterministicRandom(seed);
        var sample = Samples[random.Next(Samples.Length)];
        var resolution = sample.Resolutions[random.Next(sample.Resolutions.Length)];

        return new FingerprintConfig
        {
            ProfileId = profileId,
            ProfileSeed = seed,
            UserAgent = sample.UserAgent,
            WebRtc = new ModeValue { Mode = "proxy_udp" },
            Timezone = new ModeValue { Mode = "based_on_ip", Value = sample.Timezone },
            Geolocation = new GeolocationValue { Mode = "based_on_ip", PromptPolicy = "ask_every_time" },
            Language = new ModeValue { Mode = "based_on_ip", Value = sample.Language },
            Languages = [sample.Language],
            UiLanguage = new ModeValue { Mode = "based_on_language", Value = sample.Language },
            Resolution = new ResolutionValue { Mode = "based_on_ua", Width = resolution.Width, Height = resolution.Height },
            Fonts = new FontsValue { Mode = "custom", List = sample.Fonts.ToList() },
            WebGl = new WebGlValue { Mode = "custom", Vendor = sample.WebGlVendor, Renderer = sample.WebGlRenderer },
            WebGpu = new ModeValue { Mode = "based_on_webgl" },
            CpuCores = sample.CpuCores[random.Next(sample.CpuCores.Length)],
            DeviceMemoryGb = sample.MemoryGb[random.Next(sample.MemoryGb.Length)],
            DeviceName = CreateDeviceName(random),
            MacAddress = CreateMacAddress(random),
            DoNotTrack = "default",
            PortScanProtection = new PortScanProtection { Enabled = true },
            HardwareAcceleration = "default",
            TlsFingerprint = new ModeValue { Mode = "chrome_default" },
            ExtraFlags = ["--disable-notifications"]
        };
    }

    public static string DeriveSeed(string profileId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profileId.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static Random CreateDeterministicRandom(string seed)
    {
        var bytes = Convert.FromHexString(seed);
        var intSeed = BitConverter.ToInt32(bytes, 0);
        return new Random(intSeed);
    }

    private static string CreateDeviceName(Random random)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var suffix = new string(Enumerable.Range(0, 7).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        return $"DESKTOP-{suffix}";
    }

    private static string CreateMacAddress(Random random)
    {
        var bytes = new byte[6];
        random.NextBytes(bytes);
        bytes[0] = (byte)((bytes[0] & 0b11111110) | 0b00000010);
        return string.Join('-', bytes.Select(b => b.ToString("X2")));
    }

    private sealed record DeviceSample(
        string UserAgent,
        string WebGlVendor,
        string WebGlRenderer,
        string[] Fonts,
        (int Width, int Height)[] Resolutions,
        int[] CpuCores,
        int[] MemoryGb,
        string Timezone,
        string Language);
}
