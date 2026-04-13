using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScrcpyGui.Models;

namespace ScrcpyGui.Services;

/// <summary>
/// Manages scrcpy process lifecycle — ported from Rust commands.rs
/// </summary>
public class ScrcpyService
{
    private readonly ConcurrentDictionary<string, Process> _runningProcesses = new();
    private readonly AdbService _adbService;

    public ScrcpyService(AdbService adbService)
    {
        _adbService = adbService;
    }

    public IReadOnlyCollection<string> RunningDevices => _runningProcesses.Keys.ToList();

    /// <summary>
    /// Build scrcpy command-line arguments from config — ported from build_scrcpy_args in Rust.
    /// </summary>
    public List<string> BuildArgs(ScrcpyConfig config, string? videoDir = null)
    {
        var args = new List<string>();

        if (!string.IsNullOrEmpty(config.Device))
        {
            args.Add("-s");
            args.Add(config.Device);
        }

        var codec = string.IsNullOrEmpty(config.Codec) ? "h264" : config.Codec;
        args.Add($"--video-codec={codec}");

        var otgPure = config.OtgPure;
        var hidKeyboard = config.HidKeyboard;
        var hidMouse = config.HidMouse;

        if (config.SessionMode == "mirror" && (hidKeyboard || hidMouse) && otgPure)
        {
            if (config.Device.Contains('.') || config.Device.Contains(':'))
            {
                args.Add("--no-video");
                args.Add("--no-audio");
                args.Add("--keyboard=uhid");
                args.Add("--mouse=uhid");
            }
            else
            {
                args.Add("--otg");
            }
        }
        else
        {
            if (hidKeyboard) args.Add("--keyboard=uhid");
            if (hidMouse) args.Add("--mouse=uhid");

            if (config.Bitrate > 0)
            {
                args.Add("--video-bit-rate");
                args.Add($"{config.Bitrate}M");
            }

            if (!config.AudioEnabled) args.Add("--no-audio");
            if (config.AlwaysOnTop) args.Add("--always-on-top");
            if (config.Fullscreen) args.Add("--fullscreen");
            if (config.Borderless) args.Add("--window-borderless");

            if (config.Rotation != "0")
            {
                args.Add("--orientation");
                args.Add(config.Rotation);
            }

            var canControl = config.SessionMode != "camera";
            if (canControl)
            {
                if (config.StayAwake) args.Add("--stay-awake");
                if (config.TurnOff) { args.Add("--turn-screen-off"); args.Add("--no-power-on"); }
            }

            if (config.SessionMode == "camera")
            {
                args.Add("--video-source=camera");
                if (!string.IsNullOrEmpty(config.CameraId))
                    args.Add($"--camera-id={config.CameraId}");
                else if (!string.IsNullOrEmpty(config.CameraFacing))
                    args.Add($"--camera-facing={config.CameraFacing}");

                if (config.CameraAr != "0")
                    args.Add($"--camera-ar={config.CameraAr}");

                if (config.CameraHighSpeed)
                    args.Add("--camera-high-speed");
            }
            else if (config.SessionMode == "desktop")
            {
                var w = config.VdWidth > 0 ? config.VdWidth : 1920;
                var h = config.VdHeight > 0 ? config.VdHeight : 1080;
                var dpi = config.VdDpi > 0 ? config.VdDpi : 420;
                args.Add($"--new-display={w}x{h}/{dpi}");
                args.Add("--video-buffer=100");
            }

            if (config.Fps > 0)
            {
                args.Add(config.SessionMode == "camera" ? "--camera-fps" : "--max-fps");
                args.Add(config.Fps.ToString());
            }
            else if (config.SessionMode == "camera" && config.CameraHighSpeed)
            {
                args.Add("--camera-fps");
                args.Add("60");
            }

            if (config.Res != "0" && !string.IsNullOrEmpty(config.Res))
            {
                args.Add("--max-size");
                args.Add(config.Res);
            }

            if (config.Record)
            {
                var path = config.RecordPath;
                if (string.IsNullOrWhiteSpace(path))
                    path = videoDir ?? ".";

                var filename = $"scrcpy_{config.Device.Replace(":", "-")}_{DateTime.Now:ddMMyy HH-mm-ss,ff}.mkv";
                var fullPath = Path.Combine(path, filename);
                args.Add($"--record={fullPath}");
            }
        }

        return args;
    }

    /// <summary>
    /// Launch a scrcpy session for a device.
    /// </summary>
    public async Task RunAsync(ScrcpyConfig config, Action<string> onLog, Action<string, bool> onStatusChange)
    {
        var videoDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var args = BuildArgs(config, videoDir);
        var exePath = _adbService.GetBinaryPath("scrcpy");
        var adbExePath = _adbService.GetBinaryPath("adb");

        var modeLabel = config.SessionMode switch
        {
            "camera" => "Camera Mode",
            "desktop" => "Desktop Mode",
            _ => "Screen Mirroring"
        };

        var resLabel = config.Res == "0" ? "Original" : config.Res;
        var bitrateLabel = $"{(config.Bitrate > 0 ? config.Bitrate : 8)}Mbps";
        var fpsLabel = $"{(config.Fps > 0 ? config.Fps : 60)}fps";

        onLog($"[SYSTEM] Starting {modeLabel} session...");
        onLog($"[SYSTEM] Target: {config.Device} | Config: {resLabel} @ {bitrateLabel}, {fpsLabel}");

        if (config.Record)
        {
            var path = string.IsNullOrEmpty(config.RecordPath) ? "Videos" : config.RecordPath;
            onLog($"[SYSTEM] Recording enabled -> output to {path}");
        }

        onLog($"[SYSTEM] Using scrcpy: {exePath}");
        onLog($"[SYSTEM] Using adb: {adbExePath}");
        onLog($"> scrcpy {string.Join(" ", args)}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = string.Join(" ", args),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.Environment["ADB"] = adbExePath;

        // Set SCRCPY_SERVER_PATH if applicable
        if (exePath != "scrcpy")
        {
            var serverPath = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "scrcpy-server");
            if (File.Exists(serverPath))
                psi.Environment["SCRCPY_SERVER_PATH"] = serverPath;
        }

        var process = Process.Start(psi);
        if (process == null)
        {
            onLog("[ERROR] Failed to start scrcpy process");
            return;
        }

        _runningProcesses[config.Device] = process;
        onStatusChange(config.Device, true);

        // Stream stdout
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null) onLog(line);
                }
            }
            catch { /* process ended */ }
        });

        // Stream stderr
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null) onLog(line);
                }
            }
            catch { /* process ended */ }
        });

        // Monitor for exit
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync();
            onLog($"[SYSTEM] Scrcpy process exited with status: {process.ExitCode}");
            _runningProcesses.TryRemove(config.Device, out _);
            onStatusChange(config.Device, false);
        });
    }

    /// <summary>
    /// Stop a scrcpy session for a device.
    /// </summary>
    public async Task StopAsync(string device)
    {
        if (_runningProcesses.TryRemove(device, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    // Use taskkill for graceful termination
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {process.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var killer = Process.Start(psi);
                    if (killer != null) await killer.WaitForExitAsync();

                    await Task.Delay(500);

                    if (!process.HasExited)
                        process.Kill();
                }
            }
            catch { /* ignore */ }
            finally
            {
                process.Dispose();
            }
        }
    }

    public bool IsDeviceRunning(string device) => _runningProcesses.ContainsKey(device);
}
