using System.Text.Json.Serialization;

namespace ScrcpyGui.Models;

public class ScrcpyConfig
{
    [JsonPropertyName("device")]
    public string Device { get; set; } = "";

    [JsonPropertyName("sessionMode")]
    public string SessionMode { get; set; } = "mirror";

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; } = 8;

    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 60;

    [JsonPropertyName("stayAwake")]
    public bool StayAwake { get; set; }

    [JsonPropertyName("turnOff")]
    public bool TurnOff { get; set; }

    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; set; } = true;

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; }

    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; }

    [JsonPropertyName("borderless")]
    public bool Borderless { get; set; }

    [JsonPropertyName("record")]
    public bool Record { get; set; }

    [JsonPropertyName("recordPath")]
    public string RecordPath { get; set; } = "";

    [JsonPropertyName("scrcpyPath")]
    public string? ScrcpyPath { get; set; }

    [JsonPropertyName("otgPure")]
    public bool OtgPure { get; set; }

    [JsonPropertyName("cameraFacing")]
    public string CameraFacing { get; set; } = "back";

    [JsonPropertyName("cameraId")]
    public string CameraId { get; set; } = "";

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = "h264";

    [JsonPropertyName("cameraAr")]
    public string CameraAr { get; set; } = "0";

    [JsonPropertyName("cameraHighSpeed")]
    public bool CameraHighSpeed { get; set; }

    [JsonPropertyName("vdWidth")]
    public int VdWidth { get; set; } = 1920;

    [JsonPropertyName("vdHeight")]
    public int VdHeight { get; set; } = 1080;

    [JsonPropertyName("vdDpi")]
    public int VdDpi { get; set; } = 420;

    [JsonPropertyName("rotation")]
    public string Rotation { get; set; } = "0";

    [JsonPropertyName("res")]
    public string Res { get; set; } = "0";

    [JsonPropertyName("aspectRatioLock")]
    public bool AspectRatioLock { get; set; } = true;

    [JsonPropertyName("hidKeyboard")]
    public bool HidKeyboard { get; set; }

    [JsonPropertyName("hidMouse")]
    public bool HidMouse { get; set; }

    public ScrcpyConfig Clone()
    {
        return (ScrcpyConfig)MemberwiseClone();
    }
}
