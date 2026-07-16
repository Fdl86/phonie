using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Phonie.Models;
using Phonie.Services;

namespace Phonie;

public partial class MainWindow : Window
{
    private readonly SimConnectService simConnectService = new();
    private readonly AudioService audioService = new();
    private readonly SettingsService settingsService = new();
    private readonly GlobalKeyboardHook keyboardHook = new();
    private readonly DispatcherTimer audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private AppSettings settings;
    private bool suppressSettingsEvents;
    private bool awaitingPttKey;
    private bool pttHeld;
    private bool closing;
    private string? lastRecordingPath;

    public MainWindow()
    {
        this.settings = this.settingsService.Load();
        ThemeService.Apply(this.settings.Theme);

        this.InitializeComponent();

        this.simConnectService.StatusChanged += this.SimConnectService_OnStatusChanged;
        this.simConnectService.SnapshotReceived += this.SimConnectService_OnSnapshotReceived;
        this.simConnectService.LogMessage += this.Service_OnLogMessage;

        this.audioService.LogMessage += this.Service_OnLogMessage;
        this.audioService.RecordingStateChanged += this.AudioService_OnRecordingStateChanged;
        this.audioService.RecordingCompleted += this.AudioService_OnRecordingCompleted;

        this.keyboardHook.KeyPressed += this.KeyboardHook_OnKeyPressed;
        this.keyboardHook.KeyReleased += this.KeyboardHook_OnKeyReleased;
        this.audioMeterTimer.Tick += this.AudioMeterTimer_OnTick;

        this.Loaded += this.MainWindow_OnLoaded;
        this.Closing += this.MainWindow_OnClosing;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SelectThemeInUi();
        this.RefreshAudioDevices();
        this.UpdatePttKeyText();

        try
        {
            this.keyboardHook.Start();
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT global prêt sur {KeyName(this.settings.PttVirtualKey)}.");
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT global indisponible : {exception.Message}");
        }

        this.audioMeterTimer.Start();
        this.simConnectService.Start();
    }

    private async void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        this.closing = true;
        e.Cancel = true;
        this.audioMeterTimer.Stop();
        this.keyboardHook.Dispose();
        this.audioService.Dispose();
        await this.simConnectService.DisposeAsync();
        this.Close();
    }

    private async void ReconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Reconnexion manuelle demandée.");
        await this.simConnectService.RequestReconnectAsync();
    }

    private void RefreshAudioButton_OnClick(object sender, RoutedEventArgs e) => this.RefreshAudioDevices();

    private void PlayLastRecordingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var output = this.OutputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        if (!this.audioService.PlayFile(output?.Id, this.lastRecordingPath))
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Aucun enregistrement PTT lisible.");
        }
    }

    private void ChoosePttButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.awaitingPttKey = true;
        this.ChoosePttButton.Content = "Appuyez sur une touche…";
        this.PttStateText.Text = "Échap pour annuler";
    }

    private void ClearLogButton_OnClick(object sender, RoutedEventArgs e) => this.LogBox.Clear();

    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        this.settings.Theme = theme;
        ThemeService.Apply(theme);
        this.SaveSettings();
    }

    private void InputDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.InputDeviceComboBox.SelectedItem is not AudioDeviceInfo selectedDevice)
        {
            return;
        }

        this.settings.InputDeviceId = selectedDevice.Id;
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Microphone : {selectedDevice.Name}");
    }

    private void OutputDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.OutputDeviceComboBox.SelectedItem is not AudioDeviceInfo selectedDevice)
        {
            return;
        }

        this.settings.OutputDeviceId = selectedDevice.Id;
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Sortie radio : {selectedDevice.Name}");
    }

    private void RefreshAudioDevices()
    {
        this.suppressSettingsEvents = true;
        try
        {
            var inputs = this.audioService.GetInputDevices();
            var outputs = this.audioService.GetOutputDevices();

            this.InputDeviceComboBox.ItemsSource = inputs;
            this.OutputDeviceComboBox.ItemsSource = outputs;

            var inputId = DeviceExists(inputs, this.settings.InputDeviceId)
                ? this.settings.InputDeviceId
                : this.audioService.GetDefaultInputDeviceId();
            var outputId = DeviceExists(outputs, this.settings.OutputDeviceId)
                ? this.settings.OutputDeviceId
                : this.audioService.GetDefaultOutputDeviceId();

            this.InputDeviceComboBox.SelectedItem = inputs.FirstOrDefault(device => device.Id == inputId) ?? inputs.FirstOrDefault();
            this.OutputDeviceComboBox.SelectedItem = outputs.FirstOrDefault(device => device.Id == outputId) ?? outputs.FirstOrDefault();

            this.settings.InputDeviceId = (this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
            this.settings.OutputDeviceId = (this.OutputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
            this.SaveSettings();

            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Audio prêt : {inputs.Count} entrée(s), {outputs.Count} sortie(s).");
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void KeyboardHook_OnKeyPressed(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (this.awaitingPttKey)
            {
                if (virtualKey == 0x1B)
                {
                    this.awaitingPttKey = false;
                    this.ChoosePttButton.Content = "Changer la touche";
                    this.PttStateText.Text = "Relâché";
                    return;
                }

                this.settings.PttVirtualKey = virtualKey;
                this.awaitingPttKey = false;
                this.ChoosePttButton.Content = "Changer la touche";
                this.UpdatePttKeyText();
                this.PttStateText.Text = "Relâché";
                this.SaveSettings();
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Touche PTT définie : {KeyName(virtualKey)}.");
                return;
            }

            if (virtualKey != this.settings.PttVirtualKey || this.pttHeld)
            {
                return;
            }

            this.pttHeld = true;
            var input = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
            if (!this.audioService.StartRecording(input?.Id))
            {
                this.pttHeld = false;
                this.PttStateText.Text = "Micro indisponible";
            }
        });
    }

    private void KeyboardHook_OnKeyReleased(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (virtualKey != this.settings.PttVirtualKey || !this.pttHeld)
            {
                return;
            }

            this.pttHeld = false;
            this.audioService.StopRecording();
        });
    }

    private void AudioService_OnRecordingStateChanged(object? sender, bool recording)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.PttPanel.Background = (Brush)this.FindResource(recording ? "PttActiveBackground" : "PttIdleBackground");
            this.PttStateText.Text = recording ? "ÉMISSION — parlez" : "Relâché";
            this.PttStateText.Foreground = (Brush)this.FindResource(recording ? "Success" : "SecondaryText");
        });
    }

    private void AudioService_OnRecordingCompleted(object? sender, AudioRecordingResult result)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.lastRecordingPath = result.FilePath;
            this.PlayLastRecordingButton.IsEnabled = result.FileSizeBytes > 44;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT enregistré : {result.Duration.TotalSeconds:F1} s · {result.FileSizeBytes / 1024.0:F0} Ko.");
        });
    }

    private void AudioMeterTimer_OnTick(object? sender, EventArgs e)
    {
        var selectedInput = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        this.MicrophoneLevelBar.Value = this.audioService.GetInputPeak(selectedInput?.Id) * 100;
    }

    private void SimConnectService_OnStatusChanged(object? sender, ConnectionStatus status)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.ConnectionText.Text = status.Message;
            this.StatusDot.Fill = status.State switch
            {
                ConnectionState.Connected => this.ResourceBrush("Success"),
                ConnectionState.Error => this.ResourceBrush("Danger"),
                ConnectionState.Disconnected => this.ResourceBrush("Danger"),
                ConnectionState.Connecting => this.ResourceBrush("Warning"),
                _ => this.ResourceBrush("Warning"),
            };
        });
    }

    private void SimConnectService_OnSnapshotReceived(object? sender, SimulatorSnapshot snapshot)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.SimulatorText.Text = snapshot.Simulator;
            this.AircraftText.Text = string.IsNullOrWhiteSpace(snapshot.AircraftTitle)
                ? "Avion sans titre SimConnect"
                : snapshot.AircraftTitle;

            this.PositionText.Text = $"{snapshot.Latitude:F5}° · {snapshot.Longitude:F5}°";
            this.DistanceText.Text = $"Distance LFBI : {snapshot.DistanceToLfbiNm:F1} NM";
            this.GroundText.Text = snapshot.IsOnGround ? "AU SOL" : "EN VOL";
            this.AltitudeText.Text = $"Altitude : {snapshot.AltitudeFeet:F0} ft";
            this.HeadingText.Text = $"Cap : {snapshot.HeadingMagneticDegrees:000}° M";
            this.SpeedText.Text = $"IAS {snapshot.IndicatedAirspeedKnots:F0} kt · GS {snapshot.GroundSpeedKnots:F0} kt";
            this.ComActiveText.Text = $"Active : {snapshot.Com1ActiveMhz:F3}";
            this.ComStandbyText.Text = $"Standby : {snapshot.Com1StandbyMhz:F3}";
            this.XpdrText.Text = snapshot.TransponderCode;
            this.LastUpdateText.Text = $"Actualisé à {snapshot.Timestamp:HH:mm:ss}";
        });
    }

    private void Service_OnLogMessage(object? sender, string message) =>
        _ = this.Dispatcher.BeginInvoke(() => this.AppendLog(message));

    private void AppendLog(string message)
    {
        if (this.LogBox.LineCount > 250)
        {
            var firstLineLength = this.LogBox.GetLineLength(0);
            this.LogBox.Select(0, firstLineLength + Environment.NewLine.Length);
            this.LogBox.SelectedText = string.Empty;
        }

        this.LogBox.AppendText(message + Environment.NewLine);
        this.LogBox.ScrollToEnd();
    }

    private void SelectThemeInUi()
    {
        this.suppressSettingsEvents = true;
        try
        {
            this.ThemeComboBox.SelectedIndex = string.Equals(this.settings.Theme, ThemeService.Light, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void UpdatePttKeyText() => this.PttKeyText.Text = KeyName(this.settings.PttVirtualKey).ToUpperInvariant();

    private void SaveSettings()
    {
        try
        {
            this.settingsService.Save(this.settings);
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Réglages non enregistrés : {exception.Message}");
        }
    }

    private Brush ResourceBrush(string key) => (Brush)this.FindResource(key);

    private static bool DeviceExists(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        !string.IsNullOrWhiteSpace(id) && devices.Any(device => device.Id == id);

    private static string KeyName(int virtualKey)
    {
        return virtualKey switch
        {
            0xA2 => "Ctrl gauche",
            0xA3 => "Ctrl droit",
            0xA0 => "Maj gauche",
            0xA1 => "Maj droite",
            0xA4 => "Alt gauche",
            0xA5 => "Alt droit",
            0x20 => "Espace",
            0x0D => "Entrée",
            0x08 => "Retour arrière",
            _ => KeyInterop.KeyFromVirtualKey(virtualKey).ToString(),
        };
    }
}
