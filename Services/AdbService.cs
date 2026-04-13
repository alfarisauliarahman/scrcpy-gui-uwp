using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScrcpyGui.Services;

/// <summary>
/// Handles all ADB operations — ported from Rust commands.rs
/// </summary>
public class AdbService
{
    private const int CREATE_NO_WINDOW = 0x08000000;

    public string? CustomPath { get; set; }

    /// <summary>
    /// Resolves the full path to a binary (adb or scrcpy), checking custom path, scrcpy-bin folder, then system PATH.
    /// </summary>
    public string GetBinaryPath(string binaryName)
    {
        var exeExt = ".exe";
        var binaryFilename = $"{binaryName}{exeExt}";

        // 1. Check custom folder
        if (!string.IsNullOrWhiteSpace(CustomPath))
        {
            var fullPath = Path.Combine(CustomPath, binaryFilename);
            if (File.Exists(fullPath)) return fullPath;
        }

        // 2. Check scrcpy-bin next to executable
        var exeDir = AppContext.BaseDirectory;
        var localBin = Path.Combine(exeDir, "scrcpy-bin", binaryFilename);
        if (File.Exists(localBin)) return localBin;

        // 3. Check current directory scrcpy-bin
        var cwdBin = Path.Combine(Directory.GetCurrentDirectory(), "scrcpy-bin", binaryFilename);
        if (File.Exists(cwdBin)) return cwdBin;

        // 4. Fallback to system PATH
        return binaryName;
    }

    private ProcessStartInfo CreateProcessInfo(string program, string arguments = "")
    {
        return new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private async Task<(bool success, string stdout, string stderr)> RunCommandAsync(string program, string arguments, int timeoutMs = 10000)
    {
        try
        {
            var psi = CreateProcessInfo(program, arguments);
            using var process = Process.Start(psi);
            if (process == null) return (false, "", "Failed to start process");

            using var cts = new CancellationTokenSource(timeoutMs);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return (process.ExitCode == 0, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            return (false, "", "Command timed out");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    /// <summary>
    /// Check if scrcpy binary exists and is runnable.
    /// </summary>
    public async Task<(bool found, string message)> CheckScrcpyAsync()
    {
        var exePath = GetBinaryPath("scrcpy");
        var (success, stdout, _) = await RunCommandAsync(exePath, "--version");
        return success
            ? (true, "Scrcpy Ready")
            : (false, "Scrcpy not found");
    }

    /// <summary>
    /// Get list of connected ADB devices.
    /// </summary>
    public async Task<List<string>> GetDevicesAsync()
    {
        var adbPath = GetBinaryPath("adb");
        var (success, stdout, _) = await RunCommandAsync(adbPath, "devices");

        if (!success) return new List<string>();

        return stdout.Split('\n')
            .Skip(1) // Skip "List of devices attached"
            .Where(l => l.Contains("\tdevice"))
            .Select(l => l.Split('\t').FirstOrDefault()?.Trim() ?? "")
            .Where(s => !string.IsNullOrEmpty(s) && !s.Contains("._tcp") && !s.Contains("._udp"))
            .ToList();
    }

    /// <summary>
    /// Connect to a device via IP.
    /// </summary>
    public async Task<(bool success, string message)> ConnectAsync(string ip, Action<string>? onLog = null)
    {
        var adbPath = GetBinaryPath("adb");
        onLog?.Invoke($"[SYSTEM] Attempting wireless connection to {ip}...");

        var (success, stdout, stderr) = await RunCommandAsync(adbPath, $"connect {ip}", 5000);

        if (!string.IsNullOrEmpty(stdout)) onLog?.Invoke($"[ADB] {stdout.Trim()}");
        if (!string.IsNullOrEmpty(stderr)) onLog?.Invoke($"[ADB ERROR] {stderr.Trim()}");

        var outText = stdout.Trim();
        var connected = success && !outText.Contains("cannot connect") && !outText.Contains("failed");

        if (!connected)
        {
            // Retry: disconnect first then reconnect
            onLog?.Invoke("[SYSTEM] Connection failed, retrying with cleanup...");
            await RunCommandAsync(adbPath, $"disconnect {ip}", 3000);
            await Task.Delay(500);

            (success, stdout, stderr) = await RunCommandAsync(adbPath, $"connect {ip}", 5000);
            outText = stdout.Trim();
            if (!string.IsNullOrEmpty(stdout)) onLog?.Invoke($"[ADB] {outText}");
            connected = success && !outText.Contains("cannot connect") && !outText.Contains("failed");
        }

        return (connected, string.IsNullOrEmpty(outText) ? stderr.Trim() : outText);
    }

    /// <summary>
    /// Pair with a device (Android 11+).
    /// </summary>
    public async Task<(bool success, string message)> PairAsync(string ip, string code, Action<string>? onLog = null)
    {
        var adbPath = GetBinaryPath("adb");
        onLog?.Invoke($"[SYSTEM] Pairing with {ip}...");

        var (success, stdout, stderr) = await RunCommandAsync(adbPath, $"pair {ip} {code}");

        if (!string.IsNullOrEmpty(stdout)) onLog?.Invoke($"[ADB] {stdout.Trim()}");
        if (!string.IsNullOrEmpty(stderr)) onLog?.Invoke($"[ADB ERROR] {stderr.Trim()}");

        var paired = success && (stdout.Contains("Successfully paired") || stderr.Contains("Successfully paired"));
        return (paired, string.IsNullOrEmpty(stdout.Trim()) ? stderr.Trim() : stdout.Trim());
    }

    /// <summary>
    /// Run ADB shell command on a device.
    /// </summary>
    public async Task<(bool success, string output)> ShellAsync(string device, string command)
    {
        var adbPath = GetBinaryPath("adb");
        var (success, stdout, stderr) = await RunCommandAsync(adbPath, $"-s {device} shell {command}");
        return (success, stdout);
    }

    /// <summary>
    /// Kill ADB server and force-terminate adb processes.
    /// </summary>
    public async Task KillAdbAsync(Action<string>? onLog = null)
    {
        onLog?.Invoke("[SYSTEM] Terminating ADB stack...");
        var adbPath = GetBinaryPath("adb");

        await RunCommandAsync(adbPath, "kill-server", 5000);

        // Force kill
        try
        {
            var psi = CreateProcessInfo("taskkill", "/F /IM adb.exe /T");
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch { /* ignore */ }

        onLog?.Invoke("[SYSTEM] ADB Stack Terminated.");
    }

    /// <summary>
    /// Push a file to the device's Download folder.
    /// </summary>
    public async Task<(bool success, string message)> PushFileAsync(string device, string filePath)
    {
        var adbPath = GetBinaryPath("adb");
        var (success, _, stderr) = await RunCommandAsync(adbPath, $"-s {device} push \"{filePath}\" /sdcard/Download/", 60000);
        return success
            ? (true, "File pushed to Downloads")
            : (false, $"Transfer failed: {stderr.Trim()}");
    }

    /// <summary>
    /// Install an APK on the device.
    /// </summary>
    public async Task<(bool success, string message)> InstallApkAsync(string device, string filePath)
    {
        var adbPath = GetBinaryPath("adb");
        var (success, stdout, stderr) = await RunCommandAsync(adbPath, $"-s {device} install \"{filePath}\"", 120000);
        return success
            ? (true, stdout.Trim())
            : (false, stderr.Trim());
    }

    /// <summary>
    /// Run a terminal command (adb or scrcpy) with auto device injection.
    /// </summary>
    public async Task<(bool success, string binary, string stdout, string stderr)> RunTerminalCommandAsync(string? device, string cmd)
    {
        var parts = SplitArgs(cmd);
        if (parts.Count == 0) return (false, "", "", "No command provided");

        var first = parts[0].ToLowerInvariant();
        var isScrcpy = first == "scrcpy";
        var isAdb = first == "adb";
        var binaryName = isScrcpy ? "scrcpy" : "adb";
        var exePath = GetBinaryPath(binaryName);

        if (isAdb || isScrcpy) parts.RemoveAt(0);

        var args = new List<string>();
        var hasSerial = parts.Contains("-s") || parts.Contains("--serial");

        if (!hasSerial && !string.IsNullOrEmpty(device))
        {
            var isGlobal = binaryName == "adb" && parts.Count > 0 &&
                (parts[0] == "devices" || parts[0] == "connect" || parts[0] == "pair");
            if (!isGlobal)
            {
                args.Add("-s");
                args.Add(device);
            }
        }

        args.AddRange(parts);
        var argString = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        var (success, stdout, stderr) = await RunCommandAsync(exePath, argString, 30000);
        return (success, binaryName, stdout, stderr);
    }

    /// <summary>
    /// List scrcpy options (e.g., --list-cameras).
    /// </summary>
    public async Task<(bool success, string output)> ListScrcpyOptionsAsync(string device, string arg)
    {
        var exePath = GetBinaryPath("scrcpy");
        var (success, stdout, stderr) = await RunCommandAsync(exePath, $"-s {device} {arg}");
        return (success, stdout + stderr);
    }

    private static List<string> SplitArgs(string s)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (var c in s)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
            }
            else { current.Append(c); }
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args;
    }
}
