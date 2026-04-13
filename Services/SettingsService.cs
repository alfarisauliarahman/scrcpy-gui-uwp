using System;
using System.IO;
using System.Text.Json;
using ScrcpyGui.Models;

namespace ScrcpyGui.Services;

/// <summary>
/// Persists app settings to a JSON file (replaces localStorage from the Tauri version).
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrcpyGui");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}

public class AppSettings
{
    public ScrcpyConfig Config { get; set; } = new();
    public string Theme { get; set; } = "ultraviolet";
    public bool AutoConnect { get; set; } = true;
    public List<string> HistoryDevices { get; set; } = new();
    public bool OnboardingDone { get; set; }
}
