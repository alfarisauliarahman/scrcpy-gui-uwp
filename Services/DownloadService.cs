using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScrcpyGui.Services;

/// <summary>
/// Auto-downloads scrcpy + ADB from GitHub releases — ported from download_scrcpy in Rust.
/// </summary>
public class DownloadService
{
    private static readonly HttpClient _client = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "ScrcpyGui-Downloader" } }
    };

    public event Action<string>? OnLog;
    public event Action<int>? OnProgress;
    public event Action<string>? OnComplete;
    public event Action<string>? OnError;

    public async Task DownloadScrcpyAsync()
    {
        try
        {
            var archTag = Environment.Is64BitOperatingSystem ? "win64" : "win32";
            var extension = ".zip";

            OnLog?.Invoke($"[SYSTEM] Detecting platform: windows ({archTag})");

            // Try GitHub API first
            var downloadUrl = "";
            var filename = "";

            try
            {
                var apiUrl = "https://api.github.com/repos/Genymobile/scrcpy/releases/latest";
                var response = await _client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var assets = doc.RootElement.GetProperty("assets");

                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Contains(archTag) && name.EndsWith(extension))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            filename = name;
                            break;
                        }
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    OnLog?.Invoke("[SYSTEM] API rate limited, attempting fallback discovery...");
                }
            }
            catch
            {
                OnLog?.Invoke("[SYSTEM] API request failed, using fallback...");
            }

            // Fallback: follow redirect to get latest tag
            if (string.IsNullOrEmpty(downloadUrl))
            {
                var redirectResponse = await _client.GetAsync("https://github.com/Genymobile/scrcpy/releases/latest");
                var finalUrl = redirectResponse.RequestMessage?.RequestUri?.ToString() ?? "";
                var tag = finalUrl.Split('/')[^1];

                if (tag.StartsWith('v'))
                {
                    filename = $"scrcpy-{archTag}-{tag}{extension}";
                    downloadUrl = $"https://github.com/Genymobile/scrcpy/releases/download/{tag}/{filename}";
                    OnLog?.Invoke($"[SYSTEM] Discovered latest tag via fallback: {tag}");
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                OnError?.Invoke($"Could not find {archTag} binary.");
                return;
            }

            OnLog?.Invoke($"[SYSTEM] Found asset: {filename}");

            // Determine paths
            var appDir = AppContext.BaseDirectory;
            var tempArchive = Path.Combine(appDir, $"scrcpy_temp{extension}");
            var extractPath = Path.Combine(appDir, "scrcpy-bin");

            // Download with progress
            using var downloadResponse = await _client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            var totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
            OnLog?.Invoke($"[SYSTEM] Downloading: {totalSize / 1024 / 1024} MB");

            using (var fileStream = new FileStream(tempArchive, FileMode.Create, FileAccess.Write))
            {
                using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                var buffer = new byte[8192];
                long downloaded = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;
                    if (totalSize > 0)
                    {
                        var percent = (int)(downloaded * 100 / totalSize);
                        OnProgress?.Invoke(percent);
                    }
                }
            }

            OnLog?.Invoke("[SYSTEM] Download finished. Starting extraction...");

            // Clean up existing
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            var tempExtractDir = Path.Combine(appDir, "temp_extract");
            if (Directory.Exists(tempExtractDir))
                Directory.Delete(tempExtractDir, true);

            // Extract
            ZipFile.ExtractToDirectory(tempArchive, tempExtractDir);

            // Move: scrcpy archives usually have a single root folder
            var entries = Directory.GetDirectories(tempExtractDir);
            if (entries.Length > 0)
            {
                Directory.Move(entries[0], extractPath);
            }
            else
            {
                Directory.Move(tempExtractDir, extractPath);
            }

            // Cleanup
            if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
            if (File.Exists(tempArchive)) File.Delete(tempArchive);

            OnLog?.Invoke("[SYSTEM] Extraction complete. Scrcpy is ready!");
            OnComplete?.Invoke(extractPath);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Download failed: {ex.Message}");
        }
    }
}
