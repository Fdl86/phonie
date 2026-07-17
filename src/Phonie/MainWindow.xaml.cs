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
    private readonly DispatcherTimer audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private AppSettings settings;
    private ConnectionState currentConnectionState = ConnectionState.Waiting;
    private bool suppressSettingsEvents;
    private bool awaitingPttKey;
    private bool pttHeld;
    private bool closing;
    private bool audioReady;
    private bool pttReady;
    private string? lastRecordingPath;
    private string? lastRadioSignature;

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
            this.pttReady = true;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT global prêt sur {KeyName(this.settings.PttVirtualKey)}.");
        }
        catch (Exception exception)
        {
            this.pttReady = false;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT global indisponible : {exception.Message}");
        }

        this.RefreshFooterState();
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
        this.awaitingPttKey = true;
        this.ChoosePttButton.Content = "Appuyez sur une touche…";
        this.PttStateText.Text = "Échap pour annuler";
        this.SetDotResource(this.PttFooterDot, "Warning");
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
            if (this.awaitingPttKey)
            {
                if (virtualKey == 0x1B)
                {
                    this.awaitingPttKey = false;
                    this.ChoosePttButton.Content = "Changer la touche";
                    this.PttStateText.Text = "Relâché";
                    this.RefreshFooterState();
                    return;
                }

                this.settings.PttVirtualKey = virtualKey;
                this.awaitingPttKey = false;
                this.ChoosePttButton.Content = "Changer la touche";
                this.UpdatePttKeyText();
                this.PttStateText.Text = "Relâché";
                this.SaveSettings();
                this.RefreshFooterState();
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
                this.SetDotResource(this.PttFooterDot, "Danger");
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
            this.SetBackgroundResource(this.PttPanel, recording ? "PttActiveBackground" : "PttIdleBackground");
            this.PttStateText.Text = recording ? "ÉMISSION — parlez" : "Relâché";
            this.SetForegroundResource(this.PttStateText, recording ? "Success" : "SecondaryText");
            this.SetDotResource(this.PttFooterDot, recording ? "Accent" : this.pttReady ? "Success" : "Danger");
        });
    }

    private void AudioService_OnRecordingCompleted(object? sender, AudioRecordingResult result)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.lastRecordingPath = result.FilePath;
            this.PlayLastRecordingButton.IsEnabled = result.FileSizeBytes > 44;
            this.PttStateText.Text = result.FileSizeBytes > 44 ? "Enregistré" : "Enregistrement vide";
            this.SetForegroundResource(this.PttStateText, result.FileSizeBytes > 44 ? "Success" : "Warning");
            this.SetDotResource(this.PttFooterDot, result.FileSizeBytes > 44 ? "Success" : "Warning");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT enregistré : {result.Duration.TotalSeconds:F1} s · {result.FileSizeBytes / 1024.0:F0} Ko.");
        });
    }

    private void AudioMeterTimer_OnTick(object? sender, EventArgs e)
    {
        var selectedInput = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var level = Math.Clamp(this.audioService.GetInputPeak(selectedInput?.Id) * 100.0, 0.0, 100.0);
        this.MicrophoneLevelBar.Value = level;
        this.MicrophoneLevelText.Text = $"Niveau d'entrée : {level:F0} %";

        var meterResource = level >= 82 ? "MeterHigh" : level >= 58 ? "MeterMedium" : "MeterLow";
        this.MicrophoneLevelBar.SetResourceReference(ProgressBar.ForegroundProperty, meterResource);
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
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.SimulatorText.Text = snapshot.Simulator;
            this.AircraftText.Text = string.IsNullOrWhiteSpace(snapshot.AircraftTitle)
                ? "Avion sans titre SimConnect"
                : snapshot.AircraftTitle;

            this.PositionText.Text = FormatCoordinates(snapshot.Latitude, snapshot.Longitude);
            this.DistanceText.Text = $"Distance LFBI : {snapshot.DistanceToLfbiNm:F1} NM";
            this.GroundText.Text = snapshot.IsOnGround ? "AU SOL" : "EN VOL";
            this.AltitudeText.Text = $"Altitude : {snapshot.AltitudeFeet:F0} ft";
            this.HeadingText.Text = $"Cap : {snapshot.HeadingMagneticDegrees:000}° M";
            this.SpeedText.Text = $"IAS {snapshot.IndicatedAirspeedKnots:F0} kt · GS {snapshot.GroundSpeedKnots:F0} kt";
            this.ComActiveText.Text = $"Active : {snapshot.Com1ActiveMhz:F3}";
            this.ComStandbyText.Text = $"Standby : {snapshot.Com1StandbyMhz:F3}";
            this.XpdrText.Text = snapshot.TransponderCode;
            this.LastUpdateText.Text = $"Actualisé à {snapshot.Timestamp:HH:mm:ss}";

            var stationIdent = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent) ? "Station non identifiée" : snapshot.Com1StationIdent;
            var stationType = FriendlyStationType(snapshot.Com1StationType);
            var spacing = snapshot.Com1SpacingMode == 1 ? "8,33 kHz" : "25 kHz";
            var receive = snapshot.Com1Receiving ? "réception active" : "réception coupée";
            var radioStatus = snapshot.Com1Status == 0 ? string.Empty : $" · statut {snapshot.Com1Status}";

            this.StationText.Text = string.IsNullOrWhiteSpace(snapshot.Com1StationType)
                ? stationIdent
                : $"{stationIdent} · {stationType}";
            this.ComMetaText.Text = $"{stationIdent} · {stationType} · {spacing} · {receive}{radioStatus}";

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

            this.WeatherPrimaryText.Text = $"Vent {FormatDirection(snapshot.WindDirectionTrueDegrees)} / {FormatWindSpeed(snapshot.WindVelocityKnots)} · QNH {FormatQnh(snapshot.QnhHpa)}";
            this.WeatherSecondaryText.Text = $"Température {FormatTemperature(snapshot.TemperatureCelsius)} · visibilité {FormatVisibility(snapshot.VisibilityMeters)}";

            var radioSignature = $"{snapshot.Com1ActiveMhz:F3}|{snapshot.Com1StationIdent}|{snapshot.Com1StationType}|{snapshot.Com1SpacingMode}|{snapshot.Com1Receiving}";
            if (!string.Equals(radioSignature, this.lastRadioSignature, StringComparison.Ordinal))
            {
                this.lastRadioSignature = radioSignature;
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] COM1 : {snapshot.Com1ActiveMhz:F3} · {stationIdent} · {stationType} · {spacing} · {policy.Title}.");
            }
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

    private void RefreshVisualStates()
    {
        this.SetDotResource(this.StatusDot, StateResource(this.currentConnectionState));
        this.SetDotResource(this.SimFooterDot, StateResource(this.currentConnectionState));
        this.SetBackgroundResource(this.PttPanel, this.pttHeld ? "PttActiveBackground" : "PttIdleBackground");
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
        return $"{Math.Abs(latitude):F5}° {latHemisphere} · {Math.Abs(longitude):F5}° {lonHemisphere}";
    }


    private static string FormatDirection(double degrees) =>
        double.IsFinite(degrees) ? $"{degrees:000}°" : "—";

    private static string FormatWindSpeed(double knots) =>
        double.IsFinite(knots) ? $"{knots:F0} kt" : "—";

    private static string FormatQnh(double hpa) =>
        double.IsFinite(hpa) ? Math.Round(hpa, MidpointRounding.AwayFromZero).ToString("F0") : "—";

    private static string FormatTemperature(double celsius) =>
        double.IsFinite(celsius) ? $"{celsius:F0} °C" : "—";

    private static string FormatVisibility(double meters)
    {
        if (!double.IsFinite(meters) || meters <= 0)
        {
            return "—";
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
