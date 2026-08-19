using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using FpBrowserLauncher.Models;
using FpBrowserLauncher.Services;

namespace FpBrowserLauncher.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly AppSettingsStore _settingsStore;
    private readonly ProfileStore _profileStore;
    private readonly ProcessTracker _processTracker;
    private readonly ChromiumLauncher _chromiumLauncher;

    private string _chromiumPath = string.Empty;
    private string _profileId = "p001";
    private string _userAgent = FingerprintGenerator.DefaultUserAgent;
    private string _displayName = "Profile 001";
    private string _proxyHost = "127.0.0.1";
    private string _proxyPortText = "1080";
    private string _proxyUsername = string.Empty;
    private string _proxyPassword = string.Empty;
    private string _proxyPublicIp = string.Empty;
    private string _webRtcMode = "proxy_udp";
    private string _timezoneMode = "based_on_ip";
    private string _timezoneValue = "Asia/Shanghai";
    private string _geolocationMode = "based_on_ip";
    private string _geolocationPromptPolicy = "ask_every_time";
    private string _geolocationLatitude = string.Empty;
    private string _geolocationLongitude = string.Empty;
    private string _geolocationAccuracy = "1000";
    private string _languageMode = "based_on_ip";
    private string _languagesText = "zh-CN";
    private string _uiLanguageMode = "based_on_language";
    private string _uiLanguageValue = "zh-CN";
    private string _resolutionMode = "based_on_ua";
    private string _resolutionWidth = "1920";
    private string _resolutionHeight = "1080";
    private string _fontsMode = "custom";
    private string _fontsText = "Arial, Microsoft YaHei, SimSun, Calibri";
    private bool _canvasNoise;
    private bool _webGlImageNoise;
    private bool _audioContextNoise = true;
    private bool _mediaDevicesNoise = true;
    private bool _clientRectsNoise = true;
    private bool _speechVoicesNoise = true;
    private string _webGlMode = "custom";
    private string _webGlVendor = "Google Inc. (Intel)";
    private string _webGlRenderer = "ANGLE (Intel, Intel(R) HD Graphics Direct3D11 vs_5_0 ps_5_0)";
    private string _webGpuMode = "based_on_webgl";
    private string _cpuCores = "8";
    private string _deviceMemoryGb = "8";
    private string _deviceName = "DESKTOP-PROFILE";
    private string _macAddress = "00-50-43-3C-49-4B";
    private string _doNotTrack = "default";
    private bool _portScanProtectionEnabled = true;
    private string _allowedPorts = string.Empty;
    private string _hardwareAcceleration = "default";
    private string _tlsFingerprintMode = "chrome_default";
    private string _extraFlagsText = "--disable-notifications";
    private string _statusMessage = "准备就绪。";
    private ProfileSummary? _selectedProfile;

    public MainWindowViewModel()
    {
        _settingsStore = new AppSettingsStore();
        var profilesRoot = Path.Combine(AppContext.BaseDirectory, "profiles");
        _profileStore = new ProfileStore(profilesRoot);
        _processTracker = new ProcessTracker();
        _chromiumLauncher = new ChromiumLauncher(
            _profileStore,
            new Socks5ProxyTester(),
            _processTracker,
            new LaunchSnapshotWriter(_profileStore));

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteProfileAsync, () => SelectedProfile is not null);
        LaunchProfileCommand = new AsyncRelayCommand(LaunchProfileAsync, () => SelectedProfile is not null || !string.IsNullOrWhiteSpace(ProfileId));
        StopProfileCommand = new RelayCommand(StopProfile, () => SelectedProfile is not null);
        RandomizeGeolocationCommand = new RelayCommand(RandomizeGeolocation);
        RandomizeResolutionCommand = new RelayCommand(RandomizeResolution);
        RandomizeFontsCommand = new RelayCommand(RandomizeFonts);
        RandomizeWebGlCommand = new RelayCommand(RandomizeWebGl);
        RandomizeCpuMemoryCommand = new RelayCommand(RandomizeCpuMemory);
        RandomizeDeviceNameCommand = new RelayCommand(RandomizeDeviceName);
        RandomizeMacAddressCommand = new RelayCommand(RandomizeMacAddress);

        _ = InitializeAsyncSafe();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProfileSummary> Profiles { get; } = [];
    public IReadOnlyList<SelectionOption> WebRtcModeOptions { get; } =
    [
        new("转发", "forward"),
        new("替换", "replace"),
        new("真实", "real"),
        new("禁用", "disabled"),
        new("代理 UDP（无直连回退）", "proxy_udp")
    ];

    public IReadOnlyList<SelectionOption> TimezoneModeOptions { get; } =
    [
        new("基于 IP", "based_on_ip"),
        new("真实", "real"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<string> TimezoneValueOptions { get; } =
    [
        "Asia/Shanghai",
        "America/New_York",
        "Europe/London",
        "Europe/Berlin",
        "Asia/Tokyo",
        "Etc/GMT+12"
    ];

    public IReadOnlyList<SelectionOption> GeolocationModeOptions { get; } =
    [
        new("基于 IP", "based_on_ip"),
        new("自定义", "custom"),
        new("禁止", "disabled")
    ];

    public IReadOnlyList<SelectionOption> GeolocationPromptPolicyOptions { get; } =
    [
        new("每次询问", "ask_every_time"),
        new("始终允许", "always_allow")
    ];

    public IReadOnlyList<SelectionOption> LanguageModeOptions { get; } =
    [
        new("基于 IP", "based_on_ip"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<SelectionOption> UiLanguageModeOptions { get; } =
    [
        new("基于语言", "based_on_language"),
        new("真实", "real"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<SelectionOption> ResolutionModeOptions { get; } =
    [
        new("基于 User-Agent", "based_on_ua"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<SelectionOption> FontsModeOptions { get; } =
    [
        new("默认", "default"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<SelectionOption> WebGlModeOptions { get; } =
    [
        new("真实", "real"),
        new("自定义", "custom")
    ];

    public IReadOnlyList<SelectionOption> WebGpuModeOptions { get; } =
    [
        new("基于 WebGL", "based_on_webgl"),
        new("真实", "real"),
        new("禁用", "disabled")
    ];

    public IReadOnlyList<string> CpuCoreOptions { get; } = ["2", "4", "6", "8", "12", "16", "20", "32"];
    public IReadOnlyList<string> MemoryOptions { get; } = ["4", "8", "16", "32", "64"];

    public IReadOnlyList<SelectionOption> DoNotTrackOptions { get; } =
    [
        new("默认", "default"),
        new("开启", "enabled"),
        new("关闭", "disabled")
    ];

    public IReadOnlyList<BoolSelectionOption> EnabledDisabledOptions { get; } =
    [
        new("启用", true),
        new("关闭", false)
    ];

    public IReadOnlyList<SelectionOption> HardwareAccelerationOptions { get; } =
    [
        new("默认", "default"),
        new("开启", "enabled"),
        new("关闭", "disabled")
    ];

    public IReadOnlyList<SelectionOption> TlsFingerprintModeOptions { get; } =
    [
        new("开启", "fixed_chrome"),
        new("关闭", "chrome_default")
    ];

    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public AsyncRelayCommand LaunchProfileCommand { get; }
    public RelayCommand StopProfileCommand { get; }
    public RelayCommand RandomizeGeolocationCommand { get; }
    public RelayCommand RandomizeResolutionCommand { get; }
    public RelayCommand RandomizeFontsCommand { get; }
    public RelayCommand RandomizeWebGlCommand { get; }
    public RelayCommand RandomizeCpuMemoryCommand { get; }
    public RelayCommand RandomizeDeviceNameCommand { get; }
    public RelayCommand RandomizeMacAddressCommand { get; }

    public string ChromiumPath
    {
        get => _chromiumPath;
        set => SetField(ref _chromiumPath, value);
    }

    public string UserAgent
    {
        get => _userAgent;
        set => SetField(ref _userAgent, value);
    }

    public string ProfileId
    {
        get => _profileId;
        set => SetField(ref _profileId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string ProxyHost
    {
        get => _proxyHost;
        set => SetField(ref _proxyHost, value);
    }

    public string ProxyPortText
    {
        get => _proxyPortText;
        set => SetField(ref _proxyPortText, value);
    }

    public string ProxyUsername
    {
        get => _proxyUsername;
        set => SetField(ref _proxyUsername, value);
    }

    public string ProxyPassword
    {
        get => _proxyPassword;
        set => SetField(ref _proxyPassword, value);
    }

    public string ProxyPublicIp { get => _proxyPublicIp; set => SetField(ref _proxyPublicIp, value); }
    public string WebRtcMode { get => _webRtcMode; set => SetField(ref _webRtcMode, value); }
    public string TimezoneMode { get => _timezoneMode; set => SetField(ref _timezoneMode, value); }
    public string TimezoneValue { get => _timezoneValue; set => SetField(ref _timezoneValue, value); }
    public string GeolocationMode { get => _geolocationMode; set => SetField(ref _geolocationMode, value); }
    public string GeolocationPromptPolicy { get => _geolocationPromptPolicy; set => SetField(ref _geolocationPromptPolicy, value); }
    public string GeolocationLatitude { get => _geolocationLatitude; set => SetField(ref _geolocationLatitude, value); }
    public string GeolocationLongitude { get => _geolocationLongitude; set => SetField(ref _geolocationLongitude, value); }
    public string GeolocationAccuracy { get => _geolocationAccuracy; set => SetField(ref _geolocationAccuracy, value); }
    public string LanguageMode { get => _languageMode; set => SetField(ref _languageMode, value); }
    public string LanguagesText { get => _languagesText; set => SetField(ref _languagesText, value); }
    public string UiLanguageMode { get => _uiLanguageMode; set => SetField(ref _uiLanguageMode, value); }
    public string UiLanguageValue { get => _uiLanguageValue; set => SetField(ref _uiLanguageValue, value); }
    public string ResolutionMode { get => _resolutionMode; set => SetField(ref _resolutionMode, value); }
    public string ResolutionWidth { get => _resolutionWidth; set => SetField(ref _resolutionWidth, value); }
    public string ResolutionHeight { get => _resolutionHeight; set => SetField(ref _resolutionHeight, value); }
    public string FontsMode { get => _fontsMode; set => SetField(ref _fontsMode, value); }
    public string FontsText { get => _fontsText; set => SetField(ref _fontsText, value); }
    public bool CanvasNoise { get => _canvasNoise; set => SetField(ref _canvasNoise, value); }
    public bool WebGlImageNoise { get => _webGlImageNoise; set => SetField(ref _webGlImageNoise, value); }
    public bool AudioContextNoise { get => _audioContextNoise; set => SetField(ref _audioContextNoise, value); }
    public bool MediaDevicesNoise { get => _mediaDevicesNoise; set => SetField(ref _mediaDevicesNoise, value); }
    public bool ClientRectsNoise { get => _clientRectsNoise; set => SetField(ref _clientRectsNoise, value); }
    public bool SpeechVoicesNoise { get => _speechVoicesNoise; set => SetField(ref _speechVoicesNoise, value); }
    public string WebGlMode { get => _webGlMode; set => SetField(ref _webGlMode, value); }
    public string WebGlVendor { get => _webGlVendor; set => SetField(ref _webGlVendor, value); }
    public string WebGlRenderer { get => _webGlRenderer; set => SetField(ref _webGlRenderer, value); }
    public string WebGpuMode { get => _webGpuMode; set => SetField(ref _webGpuMode, value); }
    public string CpuCores { get => _cpuCores; set => SetField(ref _cpuCores, value); }
    public string DeviceMemoryGb { get => _deviceMemoryGb; set => SetField(ref _deviceMemoryGb, value); }
    public string DeviceName { get => _deviceName; set => SetField(ref _deviceName, value); }
    public string MacAddress { get => _macAddress; set => SetField(ref _macAddress, value); }
    public string DoNotTrack { get => _doNotTrack; set => SetField(ref _doNotTrack, value); }
    public bool PortScanProtectionEnabled { get => _portScanProtectionEnabled; set => SetField(ref _portScanProtectionEnabled, value); }
    public string AllowedPorts { get => _allowedPorts; set => SetField(ref _allowedPorts, value); }
    public string HardwareAcceleration { get => _hardwareAcceleration; set => SetField(ref _hardwareAcceleration, value); }
    public string TlsFingerprintMode { get => _tlsFingerprintMode; set => SetField(ref _tlsFingerprintMode, value); }
    public string ExtraFlagsText { get => _extraFlagsText; set => SetField(ref _extraFlagsText, value); }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ProfileSummary? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetField(ref _selectedProfile, value))
            {
                return;
            }

            if (value is not null)
            {
                _ = LoadSelectedProfileAsync(value.ProfileId);
            }

            OnPropertyChanged(nameof(SelectedProfileDetails));
            DeleteProfileCommand.RaiseCanExecuteChanged();
            LaunchProfileCommand.RaiseCanExecuteChanged();
            StopProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedProfileDetails => SelectedProfile is null
        ? "未选择 Profile。"
        : $"目录：{SelectedProfile.ProfileDirectory}{Environment.NewLine}代理：{SelectedProfile.ProxySummary}{Environment.NewLine}指纹：{SelectedProfile.FingerprintSummary}";

    private async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        ChromiumPath = settings.ChromiumPath;
        await RefreshAsync();
    }

    private async Task InitializeAsyncSafe()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"初始化失败：{ex.Message}";
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(new AppSettings { ChromiumPath = ChromiumPath, ProfilesRootPath = "profiles" });
        StatusMessage = "设置已保存。";
    }

    private async Task RefreshAsync()
    {
        var running = _processTracker.RunningProcesses;
        var profiles = await _profileStore.ListAsync();
        Profiles.Clear();

        foreach (var profile in profiles)
        {
            if (running.TryGetValue(profile.ProfileId, out var pid))
            {
                profile.IsRunning = true;
                profile.ProcessId = pid;
            }

            Profiles.Add(profile);
        }

        StatusMessage = $"已加载 {Profiles.Count} 个 Profile。";
    }

    private async Task SaveProfileAsync()
    {
        if (!int.TryParse(ProxyPortText, out var port))
        {
            StatusMessage = "代理端口必须是数字。";
            return;
        }

        var proxy = new ProxyConfig
        {
            Type = "socks5",
            Host = ProxyHost.Trim(),
            Port = port,
            Username = ProxyUsername.Trim(),
            Password = ProxyPassword,
            PublicIp = ProxyPublicIp.Trim()
        };

        try
        {
            await _profileStore.CreateOrUpdateAsync(ProfileId, DisplayName, proxy, ApplyFingerprintSettings);
            StatusMessage = $"Profile {ProfileId} 已保存并生成 fingerprint.json。";
            await RefreshAsync();
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (_processTracker.GetProcessId(SelectedProfile.ProfileId) is not null)
        {
            StatusMessage = "Profile 正在运行，请先停止再删除。";
            return;
        }

        _profileStore.Delete(SelectedProfile.ProfileId);
        SelectedProfile = null;
        await RefreshAsync();
        StatusMessage = "Profile 已删除。";
    }

    private async Task LaunchProfileAsync()
    {
        var profileId = SelectedProfile?.ProfileId ?? ProfileId;
        var result = await _chromiumLauncher.LaunchAsync(profileId, ChromiumPath);
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private void StopProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        StatusMessage = _processTracker.Stop(SelectedProfile.ProfileId) ? "已停止进程。" : "该 Profile 没有运行中的进程。";
        _ = RefreshAsync();
    }

    private async Task LoadSelectedProfileAsync(string profileId)
    {
        var metadata = await _profileStore.LoadMetadataAsync(profileId);
        var proxy = await _profileStore.LoadProxyAsync(profileId);
        var fingerprint = await _profileStore.LoadFingerprintAsync(profileId);

        ProfileId = profileId;
        DisplayName = metadata?.DisplayName ?? profileId;
        if (proxy is not null)
        {
            ProxyHost = proxy.Host;
            ProxyPortText = proxy.Port.ToString();
            ProxyUsername = proxy.Username;
            ProxyPassword = proxy.Password;
            ProxyPublicIp = proxy.PublicIp;
        }

        if (fingerprint is not null)
        {
            UserAgent = fingerprint.UserAgent;
            LoadFingerprintSettings(fingerprint);
        }

        OnPropertyChanged(nameof(SelectedProfileDetails));
    }

    private void ApplyFingerprintSettings(FingerprintConfig fingerprint)
    {
        var languages = SplitCsv(LanguagesText);
        var webRtcMode = NormalizeOption(WebRtcMode, WebRtcModeOptions, "proxy_udp");
        var timezoneMode = NormalizeOption(TimezoneMode, TimezoneModeOptions, "based_on_ip");
        var geolocationMode = NormalizeOption(GeolocationMode, GeolocationModeOptions, "based_on_ip");
        var geolocationPromptPolicy = NormalizeOption(GeolocationPromptPolicy, GeolocationPromptPolicyOptions, "ask_every_time");
        var languageMode = NormalizeOption(LanguageMode, LanguageModeOptions, "based_on_ip");
        var uiLanguageMode = NormalizeOption(UiLanguageMode, UiLanguageModeOptions, "based_on_language");
        var resolutionMode = NormalizeOption(ResolutionMode, ResolutionModeOptions, "based_on_ua");
        var fontsMode = NormalizeOption(FontsMode, FontsModeOptions, "custom");
        var webGlMode = NormalizeOption(WebGlMode, WebGlModeOptions, "custom");
        var webGpuMode = NormalizeOption(WebGpuMode, WebGpuModeOptions, "based_on_webgl");
        var languageValue = languages.FirstOrDefault() ?? EmptyToNull(LanguagesText);
        var uiLanguageValue = uiLanguageMode switch
        {
            "real" => null,
            "based_on_language" => languageValue,
            _ => EmptyToNull(UiLanguageValue)
        };

        fingerprint.UserAgent = string.IsNullOrWhiteSpace(UserAgent)
            ? fingerprint.UserAgent
            : UserAgent.Trim();
        fingerprint.WebRtc = new ModeValue { Mode = webRtcMode };
        fingerprint.Timezone = new ModeValue { Mode = timezoneMode, Value = IsMode(timezoneMode, "real") ? null : EmptyToNull(TimezoneValue) };
        fingerprint.Geolocation = new GeolocationValue
        {
            Mode = geolocationMode,
            PromptPolicy = geolocationPromptPolicy,
            Latitude = IsMode(geolocationMode, "custom") ? ParseNullableDouble(GeolocationLatitude) : null,
            Longitude = IsMode(geolocationMode, "custom") ? ParseNullableDouble(GeolocationLongitude) : null,
            Accuracy = IsMode(geolocationMode, "custom") ? ParseNullableDouble(GeolocationAccuracy) : null
        };
        fingerprint.Language = new ModeValue { Mode = languageMode, Value = languageValue };
        fingerprint.Languages = languages;
        fingerprint.UiLanguage = new ModeValue { Mode = uiLanguageMode, Value = uiLanguageValue };
        fingerprint.Resolution = new ResolutionValue { Mode = resolutionMode, Width = ParseIntOrDefault(ResolutionWidth, 1920), Height = ParseIntOrDefault(ResolutionHeight, 1080) };
        fingerprint.Fonts = new FontsValue { Mode = fontsMode, List = IsMode(fontsMode, "custom") ? SplitCsv(FontsText) : [] };
        fingerprint.NoiseToggles = new NoiseToggles { Canvas = CanvasNoise, WebGlImage = WebGlImageNoise, AudioContext = AudioContextNoise, MediaDevices = MediaDevicesNoise, ClientRects = ClientRectsNoise, SpeechVoices = SpeechVoicesNoise };
        fingerprint.WebGl = new WebGlValue { Mode = webGlMode, Vendor = IsMode(webGlMode, "custom") ? WebGlVendor.Trim() : string.Empty, Renderer = IsMode(webGlMode, "custom") ? WebGlRenderer.Trim() : string.Empty };
        fingerprint.WebGpu = new ModeValue { Mode = webGpuMode };
        fingerprint.CpuCores = ParseIntOrDefault(CpuCores, 8);
        fingerprint.DeviceMemoryGb = ParseIntOrDefault(DeviceMemoryGb, 8);
        fingerprint.DeviceName = DeviceName.Trim();
        fingerprint.MacAddress = MacAddress.Trim();
        fingerprint.DoNotTrack = NormalizeOption(DoNotTrack, DoNotTrackOptions, "default");
        fingerprint.PortScanProtection = new PortScanProtection { Enabled = PortScanProtectionEnabled, AllowedPorts = SplitCsv(AllowedPorts).Select(port => ParseIntOrDefault(port, -1)).Where(port => port > 0).ToList() };
        fingerprint.HardwareAcceleration = NormalizeOption(HardwareAcceleration, HardwareAccelerationOptions, "default");
        fingerprint.TlsFingerprint = new ModeValue { Mode = NormalizeOption(TlsFingerprintMode, TlsFingerprintModeOptions, "chrome_default") };
        fingerprint.ExtraFlags = SplitLines(ExtraFlagsText);
    }

    private void LoadFingerprintSettings(FingerprintConfig fingerprint)
    {
        WebRtcMode = fingerprint.WebRtc.Mode;
        TimezoneMode = fingerprint.Timezone.Mode;
        TimezoneValue = fingerprint.Timezone.Value ?? string.Empty;
        GeolocationMode = fingerprint.Geolocation.Mode;
        GeolocationPromptPolicy = fingerprint.Geolocation.PromptPolicy;
        GeolocationLatitude = fingerprint.Geolocation.Latitude?.ToString() ?? string.Empty;
        GeolocationLongitude = fingerprint.Geolocation.Longitude?.ToString() ?? string.Empty;
        GeolocationAccuracy = fingerprint.Geolocation.Accuracy?.ToString() ?? string.Empty;
        LanguageMode = fingerprint.Language.Mode;
        var languageList = fingerprint.Languages.Count > 0
            ? fingerprint.Languages
            : new List<string> { fingerprint.Language.Value ?? string.Empty };
        LanguagesText = string.Join(", ", languageList);
        UiLanguageMode = fingerprint.UiLanguage.Mode;
        UiLanguageValue = fingerprint.UiLanguage.Value ?? string.Empty;
        ResolutionMode = fingerprint.Resolution.Mode;
        ResolutionWidth = fingerprint.Resolution.Width.ToString();
        ResolutionHeight = fingerprint.Resolution.Height.ToString();
        FontsMode = fingerprint.Fonts.Mode;
        FontsText = string.Join(", ", fingerprint.Fonts.List);
        CanvasNoise = fingerprint.NoiseToggles.Canvas;
        WebGlImageNoise = fingerprint.NoiseToggles.WebGlImage;
        AudioContextNoise = fingerprint.NoiseToggles.AudioContext;
        MediaDevicesNoise = fingerprint.NoiseToggles.MediaDevices;
        ClientRectsNoise = fingerprint.NoiseToggles.ClientRects;
        SpeechVoicesNoise = fingerprint.NoiseToggles.SpeechVoices;
        WebGlMode = fingerprint.WebGl.Mode;
        WebGlVendor = fingerprint.WebGl.Vendor;
        WebGlRenderer = fingerprint.WebGl.Renderer;
        WebGpuMode = fingerprint.WebGpu.Mode;
        CpuCores = fingerprint.CpuCores.ToString();
        DeviceMemoryGb = fingerprint.DeviceMemoryGb.ToString();
        DeviceName = fingerprint.DeviceName;
        MacAddress = fingerprint.MacAddress;
        DoNotTrack = fingerprint.DoNotTrack;
        PortScanProtectionEnabled = fingerprint.PortScanProtection.Enabled;
        AllowedPorts = string.Join(", ", fingerprint.PortScanProtection.AllowedPorts);
        HardwareAcceleration = fingerprint.HardwareAcceleration;
        TlsFingerprintMode = fingerprint.TlsFingerprint.Mode;
        ExtraFlagsText = string.Join(Environment.NewLine, fingerprint.ExtraFlags);
    }

    private void RandomizeGeolocation()
    {
        var geo = FingerprintRandomizer.NextGeolocation();
        GeolocationMode = "custom";
        GeolocationLatitude = geo.Latitude.ToString("F6");
        GeolocationLongitude = geo.Longitude.ToString("F6");
        GeolocationAccuracy = geo.Accuracy.ToString();
    }

    private void RandomizeResolution()
    {
        var resolution = FingerprintRandomizer.NextResolution();
        ResolutionMode = resolution.Mode;
        ResolutionWidth = resolution.Width.ToString();
        ResolutionHeight = resolution.Height.ToString();
    }

    private void RandomizeFonts()
    {
        FontsMode = "custom";
        FontsText = string.Join(", ", FingerprintRandomizer.NextFonts());
    }

    private void RandomizeWebGl()
    {
        var webGl = FingerprintRandomizer.NextWebGl();
        WebGlMode = webGl.Mode;
        WebGlVendor = webGl.Vendor;
        WebGlRenderer = webGl.Renderer;
    }

    private void RandomizeCpuMemory()
    {
        var hardware = FingerprintRandomizer.NextCpuAndMemory();
        CpuCores = hardware.CpuCores.ToString();
        DeviceMemoryGb = hardware.MemoryGb.ToString();
    }

    private void RandomizeDeviceName()
    {
        DeviceName = FingerprintRandomizer.NextDeviceName();
    }

    private void RandomizeMacAddress()
    {
        MacAddress = FingerprintRandomizer.NextMacAddress();
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseIntOrDefault(string value, int defaultValue) => int.TryParse(value.Trim(), out var parsed) ? parsed : defaultValue;

    private static double? ParseNullableDouble(string value) => double.TryParse(value.Trim(), out var parsed) ? parsed : null;

    private static List<string> SplitCsv(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> SplitLines(string value) => value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string NormalizeOption(string value, IReadOnlyList<SelectionOption> options, string fallback)
    {
        var trimmed = value.Trim();
        return options.FirstOrDefault(option => string.Equals(option.Value, trimmed, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;
    }

    private static bool IsMode(string value, string mode) => string.Equals(value, mode, StringComparison.OrdinalIgnoreCase);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SelectionOption(string Label, string Value);

public sealed record BoolSelectionOption(string Label, bool Value);
