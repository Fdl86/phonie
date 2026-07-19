using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
    private readonly JoystickService joystickService = new();
    private readonly DiagnosticsService diagnosticsService = new();
    private readonly DispatcherTimer audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly HashSet<string> activePttSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activePttSourceLabels = new(StringComparer.Ordinal);
    private AppSettings settings;
    private ConnectionState currentConnectionState = ConnectionState.Waiting;
    private bool suppressSettingsEvents;
    private bool awaitingPttKey;
    private bool awaitingJoystickButton;
    private bool pttHeld;
    private bool closing;
    private bool audioReady;
    private bool pttReady;
    private bool keyboardPttReady;
    private bool joystickMappingAvailable;
    private string? lastRecordingPath;
    private string? lastRadioSignature;
    private string diagnosticsPttSource = "Aucun";
    private string diagnosticsSimulator = "-";
    private string diagnosticsStation = "-";
    private double diagnosticsCom1;

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

        this.joystickService.ButtonChanged += this.JoystickService_OnButtonChanged;
        this.joystickService.DevicesChanged += this.JoystickService_OnDevicesChanged;
        this.joystickService.LogMessage += this.Service_OnLogMessage;

        this.diagnosticsService.SampleAvailable += this.DiagnosticsService_OnSampleAvailable;
        this.audioMeterTimer.Tick += this.AudioMeterTimer_OnTick;

        this.Loaded += this.MainWindow_OnLoaded;
        this.Closing += this.MainWindow_OnClosing;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SelectThemeInUi();
        this.SelectGainInUi();
        this.RefreshAudioDevices();
        this.UpdatePttLabels();

        this.lastRecordingPath = File.Exists(this.audioService.LastRecordingPath)
            ? this.audioService.LastRecordingPath
            : null;
        this.PlayLastRecordingButton.IsEnabled = this.lastRecordingPath is not null;
        this.DiagnosticsPathText.Text = Path.Combine("logs", Path.GetFileName(this.diagnosticsService.SessionFilePath));

        this.diagnosticsService.Start(() => new DiagnosticsContext(
            this.pttHeld,
            this.diagnosticsPttSource,
            this.diagnosticsSimulator,
            this.diagnosticsCom1,
            this.diagnosticsStation,
            this.settings.MicrophoneGainDb));

        try
        {
            this.keyboardHook.Start();
            this.keyboardPttReady = true;
            this.pttReady = true;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT clavier global prêt sur {KeyName(this.settings.PttVirtualKey)}.");
        }
        catch (Exception exception)
        {
            this.keyboardPttReady = false;
            this.pttReady = this.joystickMappingAvailable;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT clavier global indisponible : {exception.Message}");
        }

        this.joystickService.Start();
        this.RefreshFooterState();
        this.audioMeterTimer.Start();
        this.simConnectService.Start();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Log portable : {this.diagnosticsService.SessionFilePath}");
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
        await this.joystickService.DisposeAsync();
        this.audioService.Dispose();
        await this.simConnectService.DisposeAsync();
        await this.diagnosticsService.DisposeAsync();
        this.Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            this.ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                this.DragMove();
            }
            catch (InvalidOperationException)
            {
                // The button can be released while Windows starts the drag operation.
            }
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => this.ToggleMaximize();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => this.Close();

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        this.MaximizeButton.Content = this.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        this.MaximizeButton.ToolTip = this.WindowState == WindowState.Maximized ? "Restaurer" : "Agrandir";
    }

    private void ToggleMaximize() =>
        this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
        this.awaitingJoystickButton = false;
        this.AssignJoystickPttButton.Content = "Assigner un bouton HOTAS";
        this.awaitingPttKey = true;
        this.ChoosePttButton.Content = "Appuyez…";
        this.PttStateText.Text = "Échap pour annuler";
        this.SetDotResource(this.PttFooterDot, "Warning");
    }

    private void AssignJoystickPttButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.awaitingJoystickButton)
        {
            this.CancelJoystickAssignment();
            return;
        }

        this.awaitingPttKey = false;
        this.ChoosePttButton.Content = "Clavier";
        this.awaitingJoystickButton = true;
        this.AssignJoystickPttButton.Content = "Annuler l'assignation";
        this.PttStateText.Text = "Appuyez sur un bouton HOTAS…";
        this.SetDotResource(this.PttFooterDot, "Warning");
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Assignation HOTAS : attente du prochain bouton.");
    }

    private void ClearJoystickPttButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.settings.JoystickPttDeviceKey = null;
        this.settings.JoystickPttDeviceName = null;
        this.settings.JoystickPttButton = null;
        this.joystickMappingAvailable = false;
        this.pttReady = this.keyboardPttReady;
        this.CancelJoystickAssignment();
        this.SaveSettings();
        this.UpdatePttLabels();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Affectation PTT HOTAS supprimée.");
    }

    private void MarkDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var markerName = (this.MarkerComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(markerName))
        {
            markerName = "TEST MANUEL";
        }

        var marker = $"{markerName} - {this.diagnosticsSimulator} - COM1 {this.diagnosticsCom1:F3} - {this.diagnosticsStation} - PTT {this.diagnosticsPttSource} - gain +{this.settings.MicrophoneGainDb} dB";
        this.diagnosticsService.Mark(marker);
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Marque ajoutée : {markerName}.");
    }

    private void OpenDiagnosticsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{this.diagnosticsService.DirectoryPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Ouverture du dossier impossible : {exception.Message}");
        }
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
        this.RefreshVisualStates();
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

    private void GainComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents
            || this.GainComboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string gainText
            || !int.TryParse(gainText, out var gainDb))
        {
            return;
        }

        this.settings.MicrophoneGainDb = Math.Clamp(gainDb, 0, 18);
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Gain micro PHONIE : +{this.settings.MicrophoneGainDb} dB.");
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

            this.audioReady = inputs.Count > 0 && outputs.Count > 0;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Audio prêt : {inputs.Count} entrée(s), {outputs.Count} sortie(s).");
        }
        catch (Exception exception)
        {
            this.audioReady = false;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Initialisation audio impossible : {exception.Message}");
        }
        finally
        {
            this.suppressSettingsEvents = false;
            this.RefreshFooterState();
        }
    }

    private void KeyboardHook_OnKeyPressed(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (this.awaitingJoystickButton)
            {
                if (virtualKey == 0x1B)
                {
                    this.CancelJoystickAssignment();
                }

                return;
            }

            if (this.awaitingPttKey)
            {
                if (virtualKey == 0x1B)
                {
                    this.awaitingPttKey = false;
                    this.ChoosePttButton.Content = "Clavier";
                    this.PttStateText.Text = "Relâché";
                    this.RefreshFooterState();
                    return;
                }

                this.settings.PttVirtualKey = virtualKey;
                this.awaitingPttKey = false;
                this.ChoosePttButton.Content = "Clavier";
                this.UpdatePttLabels();
                this.PttStateText.Text = "Relâché";
                this.SaveSettings();
                this.RefreshFooterState();
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Touche PTT définie : {KeyName(virtualKey)}.");
                return;
            }

            if (virtualKey != this.settings.PttVirtualKey)
            {
                return;
            }

            this.BeginPtt($"KEY:{virtualKey}", $"Clavier {KeyName(virtualKey)}");
        });
    }

    private void KeyboardHook_OnKeyReleased(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (virtualKey == this.settings.PttVirtualKey)
            {
                this.EndPtt($"KEY:{virtualKey}");
            }
        });
    }

    private void JoystickService_OnButtonChanged(object? sender, JoystickButtonEvent buttonEvent)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (this.awaitingJoystickButton && buttonEvent.IsPressed)
            {
                this.settings.JoystickPttDeviceKey = buttonEvent.Device.Key;
                this.settings.JoystickPttDeviceName = buttonEvent.Device.Name;
                this.settings.JoystickPttButton = buttonEvent.ButtonNumber;
                this.joystickMappingAvailable = true;
                this.pttReady = true;
                this.CancelJoystickAssignment();
                this.SaveSettings();
                this.UpdatePttLabels();
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT HOTAS défini : {buttonEvent.Device.Name} - bouton {buttonEvent.ButtonNumber}.");
                return;
            }

            if (!this.MatchesConfiguredJoystickButton(buttonEvent))
            {
                return;
            }

            var sourceKey = $"JOY:{buttonEvent.Device.Key}:{buttonEvent.ButtonNumber}";
            var sourceLabel = $"HOTAS {buttonEvent.Device.Name} B{buttonEvent.ButtonNumber}";
            if (buttonEvent.IsPressed)
            {
                this.BeginPtt(sourceKey, sourceLabel);
            }
            else
            {
                this.EndPtt(sourceKey);
            }
        });
    }

    private void JoystickService_OnDevicesChanged(object? sender, IReadOnlyList<JoystickDeviceInfo> devices)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.joystickMappingAvailable = !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey)
                && devices.Any(device => string.Equals(device.Key, this.settings.JoystickPttDeviceKey, StringComparison.Ordinal));

            if (!this.joystickMappingAvailable && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey))
            {
                foreach (var source in this.activePttSources
                             .Where(source => source.StartsWith($"JOY:{this.settings.JoystickPttDeviceKey}:", StringComparison.Ordinal))
                             .ToArray())
                {
                    this.EndPtt(source);
                }
            }

            this.pttReady = this.keyboardPttReady || this.joystickMappingAvailable;
            this.UpdatePttLabels();
            this.RefreshFooterState();
        });
    }

    private bool MatchesConfiguredJoystickButton(JoystickButtonEvent buttonEvent) =>
        this.settings.JoystickPttButton == buttonEvent.ButtonNumber
        && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey)
        && string.Equals(this.settings.JoystickPttDeviceKey, buttonEvent.Device.Key, StringComparison.Ordinal);

    private void BeginPtt(string sourceKey, string sourceLabel)
    {
        if (!this.activePttSources.Add(sourceKey))
        {
            return;
        }

        this.activePttSourceLabels[sourceKey] = sourceLabel;
        this.UpdateActivePttSourceLabel();

        if (this.pttHeld)
        {
            return;
        }

        this.pttHeld = true;
        var input = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        if (this.audioService.StartRecording(input?.Id, this.settings.MicrophoneGainDb))
        {
            this.diagnosticsService.WriteEvent("PTT", $"Début - {sourceLabel}");
            return;
        }

        this.activePttSources.Clear();
        this.activePttSourceLabels.Clear();
        this.pttHeld = false;
        this.diagnosticsPttSource = "Aucun";
        this.PttStateText.Text = "Micro indisponible";
        this.SetDotResource(this.PttFooterDot, "Danger");
    }

    private void EndPtt(string sourceKey)
    {
        if (!this.activePttSources.Remove(sourceKey))
        {
            return;
        }

        this.activePttSourceLabels.Remove(sourceKey);
        this.UpdateActivePttSourceLabel();
        if (this.activePttSources.Count > 0 || !this.pttHeld)
        {
            return;
        }

        this.pttHeld = false;
        this.audioService.StopRecording();
    }

    private void UpdateActivePttSourceLabel()
    {
        this.diagnosticsPttSource = this.activePttSourceLabels.Count == 0
            ? "Aucun"
            : string.Join(" + ", this.activePttSourceLabels.Values);
    }

    private void CancelJoystickAssignment()
    {
        this.awaitingJoystickButton = false;
        this.AssignJoystickPttButton.Content = "Assigner un bouton HOTAS";
        this.PttStateText.Text = "Relâché";
        this.RefreshFooterState();
    }

    private void AudioService_OnRecordingStateChanged(object? sender, bool recording)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.SetBackgroundResource(this.PttPanel, recording ? "PttActiveBackground" : "PttIdleBackground");
            this.PttStateText.Text = recording ? "ÉMISSION - parlez" : "Relâché";
            this.SetForegroundResource(this.PttStateText, recording ? "Success" : "SecondaryText");
            this.SetDotResource(this.PttFooterDot, recording ? "Accent" : this.pttReady ? "Success" : "Danger");
        });
    }

    private void AudioService_OnRecordingCompleted(object? sender, AudioRecordingResult result)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (result.WasDiscarded)
            {
                this.PttStateText.Text = "Appui trop court - ignoré";
                this.SetForegroundResource(this.PttStateText, "Warning");
                this.SetDotResource(this.PttFooterDot, this.pttReady ? "Success" : "Warning");
                this.diagnosticsService.WriteEvent("PTT", $"Ignoré - {result.Duration.TotalSeconds:F2} s - seuil 0,25 s");
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT ignoré : appui de {result.Duration.TotalSeconds:F2} s.");
                return;
            }

            this.lastRecordingPath = result.FilePath;
            this.PlayLastRecordingButton.IsEnabled = result.FileSizeBytes > 44;
            this.PttStateText.Text = $"Enregistré - +{result.GainDb} dB";
            this.SetForegroundResource(this.PttStateText, result.LimitedSampleCount > 0 ? "Warning" : "Success");
            this.SetDotResource(this.PttFooterDot, "Success");
            this.diagnosticsService.ReportPttCompleted(result.Duration);
            this.diagnosticsService.WriteEvent(
                "PTT",
                $"Fin - {result.Duration.TotalSeconds:F1} s - {result.FileSizeBytes / 1024.0:F0} Ko - gain +{result.GainDb} dB - limiteur {result.LimitedSampleCount} - pic {result.PeakPercent:F0} %");
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] PTT enregistré : {result.Duration.TotalSeconds:F1} s - +{result.GainDb} dB - pic {result.PeakPercent:F0} % - limiteur {result.LimitedSampleCount}.");
        });
    }

    private void AudioMeterTimer_OnTick(object? sender, EventArgs e)
    {
        var selectedInput = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var rawPeak = Math.Clamp(this.audioService.GetInputPeak(selectedInput?.Id), 0.0f, 1.0f);
        var gainLinear = Math.Pow(10.0, this.settings.MicrophoneGainDb / 20.0);
        var amplifiedPeak = rawPeak * gainLinear;
        var level = Math.Clamp(amplifiedPeak * 100.0, 0.0, 100.0);
        this.MicrophoneLevelBar.Value = level;

        var meterResource = amplifiedPeak >= 1.0 ? "MeterHigh" : amplifiedPeak >= 0.75 ? "MeterMedium" : "MeterLow";
        this.MicrophoneLevelBar.SetResourceReference(ProgressBar.ForegroundProperty, meterResource);

        if (amplifiedPeak >= 1.0)
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - saturation, réduire le gain";
            this.SetForegroundResource(this.MicrophoneSaturationText, "Danger");
        }
        else if (amplifiedPeak >= 0.75)
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - niveau élevé";
            this.SetForegroundResource(this.MicrophoneSaturationText, "Warning");
        }
        else
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - niveau {level:F0} %";
            this.SetForegroundResource(this.MicrophoneSaturationText, "SecondaryText");
        }
    }

    private void SimConnectService_OnStatusChanged(object? sender, ConnectionStatus status)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.currentConnectionState = status.State;
            this.ConnectionText.Text = status.Message;
            var resource = status.State switch
            {
                ConnectionState.Connected => "Success",
                ConnectionState.Error => "Danger",
                ConnectionState.Disconnected => "Danger",
                ConnectionState.Connecting => "Warning",
                _ => "Warning",
            };

            this.SetDotResource(this.StatusDot, resource);
            this.SetDotResource(this.SimFooterDot, resource);
            this.RefreshFooterState();
        });
    }

    private void SimConnectService_OnSnapshotReceived(object? sender, SimulatorSnapshot snapshot)
    {
        this.diagnosticsService.ReportSnapshot();
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.diagnosticsSimulator = snapshot.Simulator;
            this.diagnosticsCom1 = snapshot.Com1ActiveMhz;
            this.diagnosticsStation = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent) ? "-" : snapshot.Com1StationIdent;

            this.SimulatorText.Text = snapshot.Simulator;
            this.AircraftText.Text = string.IsNullOrWhiteSpace(snapshot.AircraftTitle)
                ? "Avion sans titre SimConnect"
                : snapshot.AircraftTitle;

            this.PositionText.Text = FormatCoordinates(snapshot.Latitude, snapshot.Longitude);
            this.DistanceText.Text = $"Distance LFBI : {snapshot.DistanceToLfbiNm:F1} NM";
            this.GroundText.Text = snapshot.IsOnGround ? "AU SOL" : "EN VOL";
            this.AltitudeText.Text = $"Altitude : {snapshot.AltitudeFeet:F0} ft";
            this.HeadingText.Text = $"Cap : {snapshot.HeadingMagneticDegrees:000}° M";
            this.SpeedText.Text = $"IAS {snapshot.IndicatedAirspeedKnots:F0} kt - GS {snapshot.GroundSpeedKnots:F0} kt";
            this.ComActiveText.Text = $"Active : {snapshot.Com1ActiveMhz:F3}";
            this.ComStandbyText.Text = $"Standby : {snapshot.Com1StandbyMhz:F3}";
            this.XpdrText.Text = snapshot.TransponderCode;
            this.LastUpdateText.Text = $"Actualisé à {snapshot.Timestamp:HH:mm:ss}";

            var stationIdent = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent) ? "Station non identifiée" : snapshot.Com1StationIdent;
            var stationType = FriendlyStationType(snapshot.Com1StationType);
            var spacing = snapshot.Com1SpacingMode == 1 ? "8,33 kHz" : "25 kHz";
            var receive = snapshot.Com1Receiving ? "réception active" : "réception coupée";
            var radioStatus = snapshot.Com1Status == 0 ? string.Empty : $" - statut {snapshot.Com1Status}";

            this.StationText.Text = string.IsNullOrWhiteSpace(snapshot.Com1StationType)
                ? stationIdent
                : $"{stationIdent} - {stationType}";
            this.ComMetaText.Text = $"{stationIdent} - {stationType} - {spacing} - {receive}{radioStatus}";

            var policy = RadioPolicyResolver.Resolve(snapshot.Com1StationType);
            this.RadioPolicyTitleText.Text = policy.Title;
            this.RadioPolicyText.Text = policy.Guidance;
            var policyResource = policy.Kind switch
            {
                RadioPolicyKind.Controlled => "Accent",
                RadioPolicyKind.InformationService => "Success",
                RadioPolicyKind.AutomaticInformation => "Accent",
                RadioPolicyKind.SelfInformation => "Warning",
                _ => "MutedText",
            };
            this.SetForegroundResource(this.RadioPolicyTitleText, policyResource);
            this.RadioPolicyCard.SetResourceReference(Border.BorderBrushProperty, policyResource);

            this.WeatherPrimaryText.Text = $"Vent {FormatDirection(snapshot.WindDirectionTrueDegrees)} / {FormatWindSpeed(snapshot.WindVelocityKnots)} - QNH {FormatQnh(snapshot.QnhHpa)}";
            this.WeatherSecondaryText.Text = $"Température {FormatTemperature(snapshot.TemperatureCelsius)} - visibilité {FormatVisibility(snapshot.VisibilityMeters)}";

            var radioSignature = $"{snapshot.Com1ActiveMhz:F3}|{snapshot.Com1StationIdent}|{snapshot.Com1StationType}|{snapshot.Com1SpacingMode}|{snapshot.Com1Receiving}";
            if (!string.Equals(radioSignature, this.lastRadioSignature, StringComparison.Ordinal))
            {
                this.lastRadioSignature = radioSignature;
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] COM1 : {snapshot.Com1ActiveMhz:F3} - {stationIdent} - {stationType} - {spacing} - {policy.Title}.");
            }
        });
    }

    private void DiagnosticsService_OnSampleAvailable(object? sender, DiagnosticsSample sample)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.CpuDiagnosticText.Text = $"{sample.CpuPercent:F2} %";
            this.CpuAverageDiagnosticText.Text = $"Moy. {sample.AverageCpuPercent:F2} % / max {sample.MaximumCpuPercent:F2} %";
            this.MemoryDiagnosticText.Text = $"{sample.WorkingSetMb:F0} Mo";
            this.ProcessDiagnosticText.Text = $"Max {sample.MaximumWorkingSetMb:F0} Mo / T {sample.ThreadCount} / H {sample.HandleCount}";
            this.TelemetryDiagnosticText.Text = $"{sample.SnapshotsPerSecond:F2} Hz";
        });
    }

    private void Service_OnLogMessage(object? sender, string message) =>
        _ = this.Dispatcher.BeginInvoke(() => this.AppendLog(message));

    private void AppendLog(string message)
    {
        this.diagnosticsService.WriteEvent("APP", message);

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

    private void SelectGainInUi()
    {
        var allowedGains = new[] { 0, 3, 6, 9, 12, 15, 18 };
        var clampedGain = Math.Clamp(this.settings.MicrophoneGainDb, 0, 18);
        this.settings.MicrophoneGainDb = allowedGains
            .OrderBy(gain => Math.Abs(gain - clampedGain))
            .First();

        this.suppressSettingsEvents = true;
        try
        {
            this.GainComboBox.SelectedItem = this.GainComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), this.settings.MicrophoneGainDb.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void UpdatePttLabels()
    {
        this.PttKeyText.Text = $"CLAVIER : {KeyName(this.settings.PttVirtualKey).ToUpperInvariant()}";
        if (this.settings.JoystickPttButton is int button && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceName))
        {
            var status = this.joystickMappingAvailable ? "prêt" : "déconnecté";
            this.PttJoystickText.Text = $"HOTAS : {this.settings.JoystickPttDeviceName} - B{button} - {status}";
            this.SetForegroundResource(this.PttJoystickText, this.joystickMappingAvailable ? "Success" : "Warning");
            this.ClearJoystickPttButton.IsEnabled = true;
        }
        else
        {
            this.PttJoystickText.Text = "HOTAS : non assigné";
            this.SetForegroundResource(this.PttJoystickText, "MutedText");
            this.ClearJoystickPttButton.IsEnabled = false;
        }
    }

    private void RefreshVisualStates()
    {
        this.SetDotResource(this.StatusDot, StateResource(this.currentConnectionState));
        this.SetDotResource(this.SimFooterDot, StateResource(this.currentConnectionState));
        this.SetBackgroundResource(this.PttPanel, this.pttHeld ? "PttActiveBackground" : "PttIdleBackground");
        this.UpdatePttLabels();
        this.RefreshFooterState();
    }

    private void RefreshFooterState()
    {
        this.SetDotResource(this.AudioFooterDot, this.audioReady ? "Success" : "Danger");
        this.SetDotResource(this.PttFooterDot, this.pttHeld ? "Accent" : this.pttReady ? "Success" : "Danger");

        if (this.currentConnectionState == ConnectionState.Connected && this.audioReady && this.pttReady)
        {
            this.ReadyFooterText.Text = "Prêt";
            this.SetDotResource(this.ReadyFooterDot, "Success");
        }
        else if (this.audioReady && this.pttReady)
        {
            this.ReadyFooterText.Text = "En attente du simulateur";
            this.SetDotResource(this.ReadyFooterDot, "Warning");
        }
        else
        {
            this.ReadyFooterText.Text = "Initialisation incomplète";
            this.SetDotResource(this.ReadyFooterDot, "Danger");
        }
    }

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

    private void SetDotResource(Shape dot, string resourceKey) => dot.SetResourceReference(Shape.FillProperty, resourceKey);

    private void SetBackgroundResource(FrameworkElement element, string resourceKey) => element.SetResourceReference(Border.BackgroundProperty, resourceKey);

    private void SetForegroundResource(FrameworkElement element, string resourceKey) => element.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);

    private static string StateResource(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "Success",
        ConnectionState.Error => "Danger",
        ConnectionState.Disconnected => "Danger",
        _ => "Warning",
    };

    private static bool DeviceExists(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        !string.IsNullOrWhiteSpace(id) && devices.Any(device => device.Id == id);

    private static string FriendlyStationType(string? stationType)
    {
        return stationType?.Trim().ToUpperInvariant() switch
        {
            "ATIS" => "ATIS",
            "AWS" => "MÉTÉO AUTO",
            "UNI" => "UNICOM",
            "CTAF" => "AUTO-INFO",
            "GND" => "SOL",
            "TWR" => "TOUR",
            "CLR" => "PRÉVOL",
            "APPR" => "APPROCHE",
            "DEP" => "DÉPART",
            "FSS" => "INFORMATION / AFIS",
            _ => "TYPE INCONNU",
        };
    }

    private static string FormatCoordinates(double latitude, double longitude)
    {
        var latHemisphere = latitude >= 0 ? "N" : "S";
        var lonHemisphere = longitude >= 0 ? "E" : "W";
        return $"{Math.Abs(latitude):F5}° {latHemisphere} - {Math.Abs(longitude):F5}° {lonHemisphere}";
    }

    private static string FormatDirection(double degrees) =>
        double.IsFinite(degrees) ? $"{degrees:000}°" : "-";

    private static string FormatWindSpeed(double knots) =>
        double.IsFinite(knots) ? $"{knots:F0} kt" : "-";

    private static string FormatQnh(double hpa) =>
        double.IsFinite(hpa) ? Math.Round(hpa, MidpointRounding.AwayFromZero).ToString("F0") : "-";

    private static string FormatTemperature(double celsius) =>
        double.IsFinite(celsius) ? $"{celsius:F0} °C" : "-";

    private static string FormatVisibility(double meters)
    {
        if (!double.IsFinite(meters) || meters <= 0)
        {
            return "-";
        }

        return meters >= 1000 ? $"{meters / 1000.0:F1} km" : $"{meters:F0} m";
    }

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

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
