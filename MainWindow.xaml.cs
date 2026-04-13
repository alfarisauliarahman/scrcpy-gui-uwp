using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using ScrcpyGui.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ScrcpyGui;

public partial class MainWindow : FluentWindow
{
    private MainViewModel VM => (MainViewModel)DataContext;
    private bool _isDarkTheme = true;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += (s, e) => VM.SaveSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-scroll logs
        if (VM.Logs is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            };
        }

        LoadConfigToUI();
        UpdateSessionModeUI();
        UpdateSessionRunningUI();

        _isDarkTheme = VM.CurrentTheme == "dark";
        ApplyTheme();

        VM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(VM.SessionRunning))
                Dispatcher.Invoke(() => UpdateSessionRunningUI());
        };

        await VM.InitializeAsync();
    }

    private void LoadConfigToUI()
    {
        var config = VM.Config;

        BitrateSlider.Value = config.Bitrate > 0 ? config.Bitrate : 8;
        BitrateLabel.Text = $"{(int)BitrateSlider.Value} Mbps";

        SelectComboByTag(ResolutionCombo, config.Res ?? "0");
        SelectComboByTag(FpsCombo, config.Fps.ToString());
        SelectComboByTag(RotationCombo, config.Rotation ?? "0");
        SelectComboByTag(CodecCombo, config.Codec ?? "h264");
        SelectComboByTag(CameraFacingCombo, config.CameraFacing ?? "back");
        SelectComboByTag(CameraArCombo, config.CameraAr ?? "0");

        ChkStayAwake.IsChecked = config.StayAwake;
        ChkScreenOff.IsChecked = config.TurnOff;
        ChkAudio.IsChecked = config.AudioEnabled;
        ChkAlwaysOnTop.IsChecked = config.AlwaysOnTop;
        ChkFullscreen.IsChecked = config.Fullscreen;
        ChkBorderless.IsChecked = config.Borderless;
        ChkRecord.IsChecked = config.Record;

        ChkHidKeyboard.IsChecked = config.HidKeyboard;
        ChkHidMouse.IsChecked = config.HidMouse;
        ChkOtgPure.IsChecked = config.OtgPure;
        ChkCameraHighSpeed.IsChecked = config.CameraHighSpeed;

        VdWidthSlider.Value = config.VdWidth > 0 ? config.VdWidth : 1920;
        VdHeightSlider.Value = config.VdHeight > 0 ? config.VdHeight : 1080;
        VdDpiSlider.Value = config.VdDpi > 0 ? config.VdDpi : 420;
        VdWidthLabel.Text = $"{(int)VdWidthSlider.Value}px";
        VdHeightLabel.Text = $"{(int)VdHeightSlider.Value}px";
        VdDpiLabel.Text = $"{(int)VdDpiSlider.Value}";
    }

    private void SelectComboByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // ===== Theme =====
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        VM.SetTheme(_isDarkTheme ? "dark" : "light");
    }

    private void ApplyTheme()
    {
        ApplicationThemeManager.Apply(_isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
        ThemeToggleBtn.Icon = new SymbolIcon(_isDarkTheme ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24);
    }

    // ===== Scrcpy Path =====
    private void SetPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select scrcpy folder" };
        if (dialog.ShowDialog() == true)
            VM.SetCustomPath(dialog.FolderName);
    }

    private void ResetPath_Click(object sender, RoutedEventArgs e) => VM.ResetCustomPath();

    // ===== Device Actions =====
    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await VM.RefreshDevicesAsync();
    private async void KillAdb_Click(object sender, RoutedEventArgs e) => await VM.KillAdbAsync();

    private async void ConnectDevice_Click(object sender, RoutedEventArgs e)
    {
        var ip = ConnectIpBox.Text.Trim();
        if (!string.IsNullOrEmpty(ip))
            await VM.ConnectDeviceAsync(ip);
    }

    private async void PairDevice_Click(object sender, RoutedEventArgs e)
    {
        var ip = PairIpBox.Text.Trim();
        var code = PairCodeBox.Text.Trim();
        if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(code))
            await VM.PairDeviceAsync((ip, code));
    }

    private async void HistoryDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Content is string ip)
        {
            ConnectIpBox.Text = ip;
            await VM.ConnectDeviceAsync(ip);
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e) => VM.ClearHistory();

    // ===== USB / Wireless Tabs =====
    private void TabUsb_Click(object sender, RoutedEventArgs e)
    {
        UsbPanel.Visibility = Visibility.Visible;
        WirelessPanel.Visibility = Visibility.Collapsed;
        TabUsbBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        TabWirelessBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    private void TabWireless_Click(object sender, RoutedEventArgs e)
    {
        UsbPanel.Visibility = Visibility.Collapsed;
        WirelessPanel.Visibility = Visibility.Visible;
        TabUsbBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        TabWirelessBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
    }

    // ===== Session Mode =====
    private void SessionMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string mode)
        {
            VM.Config.SessionMode = mode;
            VM.NotifyConfigChanged();
            UpdateSessionModeUI();
        }
    }

    private void UpdateSessionModeUI()
    {
        var mode = VM.Config.SessionMode;

        // Visual toggle via Appearance
        BtnMirror.Appearance = mode == "mirror" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
        BtnCamera.Appearance = mode == "camera" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
        BtnDesktop.Appearance = mode == "desktop" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;

        // Show/hide mode-specific panels
        HidPanel.Visibility = mode == "mirror" ? Visibility.Visible : Visibility.Collapsed;
        CameraPanel.Visibility = mode == "camera" ? Visibility.Visible : Visibility.Collapsed;
        DesktopPanel.Visibility = mode == "desktop" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Engine Config =====
    private void Resolution_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ResolutionCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.Res = item.Tag?.ToString() ?? "0";
    }

    private void Fps_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FpsCombo?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var fps))
            VM.Config.Fps = fps;
    }

    private void Rotation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RotationCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.Rotation = item.Tag?.ToString() ?? "0";
    }

    private void Codec_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CodecCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.Codec = item.Tag?.ToString() ?? "h264";
    }

    private void Bitrate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BitrateLabel != null)
        {
            var val = (int)BitrateSlider.Value;
            BitrateLabel.Text = $"{val} Mbps";
            VM.Config.Bitrate = val;
        }
    }

    // ===== HID =====
    private void HidKeyboard_Changed(object sender, RoutedEventArgs e) => VM.Config.HidKeyboard = ChkHidKeyboard.IsChecked ?? false;
    private void HidMouse_Changed(object sender, RoutedEventArgs e) => VM.Config.HidMouse = ChkHidMouse.IsChecked ?? false;
    private void OtgPure_Changed(object sender, RoutedEventArgs e) => VM.Config.OtgPure = ChkOtgPure.IsChecked ?? false;

    // ===== Camera Mode =====
    private void CameraFacing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CameraFacingCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.CameraFacing = item.Tag?.ToString() ?? "back";
    }

    private void CameraId_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CameraIdCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.CameraId = item.Tag?.ToString() ?? "";
    }

    private void CameraAr_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CameraArCombo?.SelectedItem is ComboBoxItem item)
            VM.Config.CameraAr = item.Tag?.ToString() ?? "0";
    }

    private void CameraHighSpeed_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkCameraHighSpeed != null)
            VM.Config.CameraHighSpeed = ChkCameraHighSpeed.IsChecked ?? false;
    }

    private async void ListCameras_Click(object sender, RoutedEventArgs e)
    {
        await VM.ListCamerasAsync();

        // Populate camera ID combo
        Dispatcher.Invoke(() =>
        {
            CameraIdCombo.Items.Clear();
            CameraIdCombo.Items.Add(new ComboBoxItem { Content = "Auto Select", Tag = "" });
            foreach (var cam in VM.DetectedCameras)
            {
                CameraIdCombo.Items.Add(new ComboBoxItem { Content = cam.Name, Tag = cam.Id });
            }
            CameraIdCombo.SelectedIndex = 0;
        });
    }

    // ===== Desktop Mode =====
    private void VdPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VdWidthSlider == null || VdHeightSlider == null) return;
        if (VdPresetCombo?.SelectedItem is ComboBoxItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "1080p":
                    VdWidthSlider.Value = 1920; VdHeightSlider.Value = 1080;
                    break;
                case "1440p":
                    VdWidthSlider.Value = 2560; VdHeightSlider.Value = 1440;
                    break;
                case "4k":
                    VdWidthSlider.Value = 3840; VdHeightSlider.Value = 2160;
                    break;
                case "ultrawide":
                    VdWidthSlider.Value = 2560; VdHeightSlider.Value = 1080;
                    break;
            }
        }
    }

    private bool _isUpdatingVd = false;

    private void VdWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VdWidthLabel == null || _isUpdatingVd) return;

        var val = (int)VdWidthSlider.Value;
        VdWidthLabel.Text = $"{val}px";

        if (ChkAspectRatioLock?.IsChecked == true && VM.Config.VdWidth > 0)
        {
            var ratio = (double)VM.Config.VdHeight / VM.Config.VdWidth;
            var newH = (int)(val * ratio);
            if (newH >= 360 && newH <= 2160)
            {
                _isUpdatingVd = true;
                VdHeightSlider.Value = newH;
                _isUpdatingVd = false;
            }
        }
        VM.Config.VdWidth = val;
    }

    private void VdHeight_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VdHeightLabel == null || _isUpdatingVd) return;

        var val = (int)VdHeightSlider.Value;
        VdHeightLabel.Text = $"{val}px";

        if (ChkAspectRatioLock?.IsChecked == true && VM.Config.VdHeight > 0)
        {
            var ratio = (double)VM.Config.VdWidth / VM.Config.VdHeight;
            var newW = (int)(val * ratio);
            if (newW >= 480 && newW <= 3840)
            {
                _isUpdatingVd = true;
                VdWidthSlider.Value = newW;
                _isUpdatingVd = false;
            }
        }
        VM.Config.VdHeight = val;
        VM.Config.VdHeight = val;
    }

    private void VdDpi_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VdDpiLabel == null) return;
        var val = (int)VdDpiSlider.Value;
        VdDpiLabel.Text = $"{val}";
        VM.Config.VdDpi = val;
    }

    // ===== Session Toggles =====
    private void SessionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkStayAwake == null || ChkScreenOff == null || ChkAudio == null ||
            ChkAlwaysOnTop == null || ChkFullscreen == null || ChkBorderless == null || ChkRecord == null)
            return;

        VM.Config.StayAwake = ChkStayAwake.IsChecked ?? false;
        VM.Config.TurnOff = ChkScreenOff.IsChecked ?? false;
        VM.Config.AudioEnabled = ChkAudio.IsChecked ?? false;
        VM.Config.AlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;
        VM.Config.Fullscreen = ChkFullscreen.IsChecked ?? false;
        VM.Config.Borderless = ChkBorderless.IsChecked ?? false;
        VM.Config.Record = ChkRecord.IsChecked ?? false;
    }

    private void ChangeRecordPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Recording Folder" };
        if (dialog.ShowDialog() == true)
        {
            VM.Config.RecordPath = dialog.FolderName;
            VM.NotifyConfigChanged();
            VM.SaveSettings();
        }
    }

    // ===== Start / Stop =====
    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        VM.SaveSettings();
        await VM.StartSessionAsync();
        UpdateSessionRunningUI();
    }

    private async void StopSession_Click(object sender, RoutedEventArgs e)
    {
        await VM.StopSessionAsync();
        UpdateSessionRunningUI();
    }

    private void UpdateSessionRunningUI()
    {
        var running = VM.SessionRunning;
        BtnStart.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        BtnStop.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Terminal =====
    private async void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var cmd = TerminalInput.Text.Trim();
            if (!string.IsNullOrEmpty(cmd))
            {
                TerminalInput.Text = "";
                await VM.RunTerminalCommandAsync(cmd);
            }
        }
    }

    // ===== Logs =====
    private async void ExportReport_Click(object sender, RoutedEventArgs e) => await VM.ExportReportAsync();
    private void ClearLogs_Click(object sender, RoutedEventArgs e) => VM.ClearLogs();

    // ===== File Push =====
    private void FilePush_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "All Files (*.*)|*.*|Android App (*.apk)|*.apk"
        };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                _ = VM.PushFileAsync(file);
        }
    }

    private void FileDrop_Handler(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
                _ = VM.PushFileAsync(file);
        }
    }

    // ===== Onboarding =====
    private async void DownloadScrcpy_Click(object sender, RoutedEventArgs e) => await VM.DownloadScrcpyAsync();
    private void CloseOnboarding_Click(object sender, RoutedEventArgs e) => VM.IsOnboardingOpen = false;
    private void CompleteOnboarding_Click(object sender, RoutedEventArgs e) => VM.CompleteOnboarding();
}

// ===== Value Converters =====

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class BoolToRefreshTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Syncing..." : "Refresh";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}