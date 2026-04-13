using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScrcpyGui.Models;
using ScrcpyGui.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ScrcpyGui.ViewModels;

/// <summary>
/// Main ViewModel — ported from useScrcpy.ts hook and App.tsx logic
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AdbService _adbService;
    private readonly ScrcpyService _scrcpyService;
    private readonly DownloadService _downloadService;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _dispatcher;

    public MainViewModel()
    {
        _adbService = new AdbService();
        _scrcpyService = new ScrcpyService(_adbService);
        _downloadService = new DownloadService();
        _settingsService = new SettingsService();
        _dispatcher = Application.Current.Dispatcher;

        // Wire up download events
        _downloadService.OnLog += msg => AddLog(msg);
        _downloadService.OnProgress += p => DownloadProgress = p;
        _downloadService.OnComplete += path =>
        {
            IsDownloading = false;
            AddLog("[SYSTEM] Download Complete");
            _ = CheckScrcpyAsync();
            _ = RefreshDevicesAsync(true);
        };
        _downloadService.OnError += err =>
        {
            IsDownloading = false;
            AddLog($"[ERROR] {err}");
        };

        // Load saved settings
        LoadSettings();
    }

    // ===== Observable Properties =====

    [ObservableProperty] private ObservableCollection<string> _devices = new();
    [ObservableProperty] private ObservableCollection<string> _logs = new();
    [ObservableProperty] private ObservableCollection<string> _runningDevices = new();
    [ObservableProperty] private ObservableCollection<string> _historyDevices = new();
    [ObservableProperty] private ObservableCollection<CameraInfo> _detectedCameras = new();

    [ObservableProperty] private string _activeDevice = "";
    [ObservableProperty] private string _appVersion = "3.4.4";
    [ObservableProperty] private string _currentTheme = "ultraviolet";

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private bool _isAutoConnect = true;
    [ObservableProperty] private bool _isOnboardingOpen;
    [ObservableProperty] private bool _scrcpyFound;
    [ObservableProperty] private string _scrcpyStatusMessage = "Checking...";

    [ObservableProperty] private ScrcpyConfig _config = new();

    public bool SessionRunning => RunningDevices.Contains(ActiveDevice);

    // ===== Alert State =====
    [ObservableProperty] private bool _isAlertOpen;
    [ObservableProperty] private string _alertTitle = "";
    [ObservableProperty] private string _alertMessage = "";
    [ObservableProperty] private string _alertKind = "info";

    // ===== Initialization =====

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        Config = settings.Config;
        CurrentTheme = settings.Theme;
        IsAutoConnect = settings.AutoConnect;
        HistoryDevices = new ObservableCollection<string>(settings.HistoryDevices);

        if (!string.IsNullOrEmpty(Config.ScrcpyPath))
            _adbService.CustomPath = Config.ScrcpyPath;

        if (!settings.OnboardingDone)
            IsOnboardingOpen = true;
    }

    public void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            Config = Config,
            Theme = CurrentTheme,
            AutoConnect = IsAutoConnect,
            HistoryDevices = HistoryDevices.ToList(),
            OnboardingDone = !IsOnboardingOpen || ScrcpyFound
        });
    }

    public async Task InitializeAsync()
    {
        await CheckScrcpyAsync();
        await RefreshDevicesAsync(true);
    }

    // ===== Device Management =====

    [RelayCommand]
    public async Task RefreshDevicesAsync(bool silent = false)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        try
        {
            var newDevices = await _adbService.GetDevicesAsync();
            var prevDevices = Devices.ToList();

            var added = newDevices.Except(prevDevices).ToList();
            var removed = prevDevices.Except(newDevices).ToList();

            foreach (var d in added)
                AddLog($"[SYSTEM] New device discovered: {d}");
            foreach (var d in removed)
                AddLog($"[SYSTEM] Device disconnected: {d}");

            _dispatcher.Invoke(() =>
            {
                Devices.Clear();
                foreach (var d in newDevices) Devices.Add(d);
            });

            if (!silent && added.Count == 0 && removed.Count == 0)
                AddLog($"[SYSTEM] Discovery active: {newDevices.Count} device(s) found.");

            if (newDevices.Count > 0 && string.IsNullOrEmpty(ActiveDevice))
                ActiveDevice = newDevices[0];
        }
        catch (Exception ex)
        {
            AddLog($"[SYSTEM] Error refreshing devices: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task KillAdbAsync()
    {
        await _adbService.KillAdbAsync(msg => AddLog(msg));
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    public async Task ConnectDeviceAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        IsRefreshing = true;

        try
        {
            var (success, message) = await _adbService.ConnectAsync(ip, msg => AddLog(msg));

            if (success)
            {
                AddLog($"[SYSTEM] CONNECTED TO {ip} SUCCESSFULLY.");
                AddToHistory(ip);
                await Task.Delay(1000);
                await RefreshDevicesAsync(true);
            }
            else
            {
                AddLog($"[SYSTEM] Connection failed: {message}");
                if (message.Contains("failed to connect") || message.Contains("cannot connect"))
                    AddLog("[TIP] Port might be stale. Try \"Kill ADB\" to refresh discovery.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"[ERROR] Connection error: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task PairDeviceAsync((string ip, string code) args)
    {
        if (string.IsNullOrWhiteSpace(args.ip) || string.IsNullOrWhiteSpace(args.code)) return;

        var (success, message) = await _adbService.PairAsync(args.ip, args.code, msg => AddLog(msg));

        if (success)
        {
            AddLog($"[SYSTEM] Successfully paired with {args.ip}");
            var ipOnly = args.ip.Split(':')[0];
            var connectTarget = $"{ipOnly}:5555";
            await ConnectDeviceAsync(connectTarget);
        }
        else
        {
            AddLog($"[SYSTEM] Pairing failed: {message}");
            if (message.Contains("protocol fault"))
                AddLog("[TIP] Protocol fault usually means the ADB server is stuck. Try \"Kill ADB\" in the sidebar.");
        }
    }

    // ===== Scrcpy Session =====

    [RelayCommand]
    public async Task StartSessionAsync()
    {
        if (string.IsNullOrEmpty(ActiveDevice))
        {
            ShowAlert("No Device Selected",
                "Please select a device from the sidebar. If you just connected, click 'Refresh'.",
                "warning");
            return;
        }

        Config.Device = ActiveDevice;
        await _scrcpyService.RunAsync(Config, msg => AddLog(msg), (device, running) =>
        {
            _dispatcher.Invoke(() =>
            {
                if (running)
                {
                    if (!RunningDevices.Contains(device))
                        RunningDevices.Add(device);
                }
                else
                {
                    RunningDevices.Remove(device);
                }
                OnPropertyChanged(nameof(SessionRunning));
            });
        });
        OnPropertyChanged(nameof(SessionRunning));
    }

    [RelayCommand]
    public async Task StopSessionAsync()
    {
        if (string.IsNullOrEmpty(ActiveDevice)) return;
        await _scrcpyService.StopAsync(ActiveDevice);
        _dispatcher.Invoke(() =>
        {
            RunningDevices.Remove(ActiveDevice);
            OnPropertyChanged(nameof(SessionRunning));
        });
    }

    // ===== Scrcpy Binary Management =====

    [RelayCommand]
    public async Task CheckScrcpyAsync()
    {
        var (found, message) = await _adbService.CheckScrcpyAsync();
        ScrcpyFound = found;
        ScrcpyStatusMessage = message;

        if (!found)
            IsOnboardingOpen = true;
    }

    [RelayCommand]
    public async Task DownloadScrcpyAsync()
    {
        IsDownloading = true;
        DownloadProgress = 0;
        await _downloadService.DownloadScrcpyAsync();
    }

    public void SetCustomPath(string path)
    {
        Config.ScrcpyPath = path;
        _adbService.CustomPath = path;
        AddLog($"[SYSTEM] Custom scrcpy path set to: {path}");
        _ = CheckScrcpyAsync();
        SaveSettings();
    }

    public void ResetCustomPath()
    {
        Config.ScrcpyPath = null;
        _adbService.CustomPath = null;
        AddLog("[SYSTEM] Custom scrcpy path cleared. Using system default.");
        _ = CheckScrcpyAsync();
        SaveSettings();
    }

    // ===== File Operations =====

    [RelayCommand]
    public async Task PushFileAsync(string filePath)
    {
        if (string.IsNullOrEmpty(ActiveDevice)) return;

        if (filePath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"[SYSTEM] Installing APK on {ActiveDevice}: {filePath}...");
            var (success, message) = await _adbService.InstallApkAsync(ActiveDevice, filePath);
            AddLog($"[ADB] {message}");
        }
        else
        {
            AddLog($"[SYSTEM] Pushing file to {ActiveDevice}: {filePath}...");
            var (success, message) = await _adbService.PushFileAsync(ActiveDevice, filePath);
            AddLog($"[ADB] {message}");
        }
    }

    // ===== Terminal =====

    [RelayCommand]
    public async Task RunTerminalCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var lower = command.Trim().ToLower();
        var prefix = (lower.StartsWith("scrcpy") || lower.StartsWith("adb")) ? "" : "adb ";
        AddLog($"> {prefix}{command}");

        var (success, binary, stdout, stderr) = await _adbService.RunTerminalCommandAsync(ActiveDevice, command);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            foreach (var line in stdout.Trim().Split('\n'))
                AddLog(line);
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            foreach (var line in stderr.Trim().Split('\n'))
                AddLog($"[{binary.ToUpperInvariant()}] {line}");
        }
    }

    // ===== Camera List =====

    [RelayCommand]
    public async Task ListCamerasAsync()
    {
        if (string.IsNullOrEmpty(ActiveDevice)) return;

        AddLog("Running scrcpy --list-cameras...");
        var (success, output) = await _adbService.ListScrcpyOptionsAsync(ActiveDevice, "--list-cameras");

        if (!string.IsNullOrEmpty(output))
        {
            foreach (var line in output.Split('\n'))
                AddLog(line);

            // Parse cameras
            var cameras = new ObservableCollection<CameraInfo>();
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                var newMatch = Regex.Match(trimmed, @"--camera-id=(\w+)\s*\((.*?)\)");
                var oldMatch = Regex.Match(trimmed, @"^(?:-\s*)?\[(\w+)\]\s*\((.*?)\)\s*(.*)");

                if (newMatch.Success)
                    cameras.Add(new CameraInfo(newMatch.Groups[1].Value, $"{newMatch.Groups[1].Value}: {newMatch.Groups[2].Value}"));
                else if (oldMatch.Success)
                    cameras.Add(new CameraInfo(oldMatch.Groups[1].Value, $"{oldMatch.Groups[1].Value}: {oldMatch.Groups[3].Value.Trim()} ({oldMatch.Groups[2].Value})"));
            }

            if (cameras.Count > 0)
                DetectedCameras = cameras;
            else
                AddLog("[SYSTEM] No cameras parsed from output.");
        }
    }

    [RelayCommand]
    public async Task ListScrcpyOptionsAsync(string arg)
    {
        if (string.IsNullOrEmpty(ActiveDevice)) return;

        AddLog($"Running scrcpy {arg}...");
        var (success, output) = await _adbService.ListScrcpyOptionsAsync(ActiveDevice, arg);
        if (!string.IsNullOrEmpty(output))
        {
            foreach (var line in output.Split('\n'))
                AddLog(line);
        }
    }

    // ===== Diagnostic Report =====

    [RelayCommand]
    public async Task ExportReportAsync()
    {
        try
        {
            var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            downloadsPath = Path.Combine(downloadsPath, "Downloads");

            var data = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                config = Config,
                logs = Logs.ToList(),
                theme = CurrentTheme
            };

            var fileName = $"scrcpy-gui-logs-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json";
            var filePath = Path.Combine(downloadsPath, fileName);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            AddLog($"[SYSTEM] Diagnostic report saved to Downloads: {fileName}");
        }
        catch (Exception ex)
        {
            AddLog($"[ERROR] Export failed: {ex.Message}");
        }
    }

    // ===== Helpers =====

    public void AddLog(string message)
    {
        _dispatcher.Invoke(() =>
        {
            // Keep max 200 log entries
            while (Logs.Count > 200)
                Logs.RemoveAt(0);
            Logs.Add(message);
        });
    }

    [RelayCommand]
    public void ClearLogs() => _dispatcher.Invoke(() => Logs.Clear());

    public void ShowAlert(string title, string message, string kind = "info")
    {
        AlertTitle = title;
        AlertMessage = message;
        AlertKind = kind;
        IsAlertOpen = true;
    }

    public void CloseAlert() => IsAlertOpen = false;

    public void CompleteOnboarding()
    {
        IsOnboardingOpen = false;
        SaveSettings();
    }

    private void AddToHistory(string ip)
    {
        if (!ip.Contains(':')) return;
        _dispatcher.Invoke(() =>
        {
            HistoryDevices.Remove(ip);
            HistoryDevices.Insert(0, ip);
            while (HistoryDevices.Count > 10) HistoryDevices.RemoveAt(HistoryDevices.Count - 1);
        });
        SaveSettings();
    }

    [RelayCommand]
    public void ClearHistory()
    {
        _dispatcher.Invoke(() => HistoryDevices.Clear());
        SaveSettings();
    }

    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        SaveSettings();
    }

    partial void OnActiveDeviceChanged(string value)
    {
        Config.Device = value;
        DetectedCameras.Clear();
        OnPropertyChanged(nameof(SessionRunning));
        SaveSettings();
    }

    public void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(Config));
        SaveSettings();
    }

    partial void OnConfigChanged(ScrcpyConfig value) => SaveSettings();
}

public record CameraInfo(string Id, string Name);
