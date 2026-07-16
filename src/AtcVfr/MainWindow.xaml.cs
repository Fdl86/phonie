using System.Windows;
using System.Windows.Media;
using AtcVfr.Models;
using AtcVfr.Services;

namespace AtcVfr;

public partial class MainWindow : Window
{
    private readonly SimConnectService simConnectService = new();
    private bool closing;

    public MainWindow()
    {
        this.InitializeComponent();

        this.simConnectService.StatusChanged += this.SimConnectService_OnStatusChanged;
        this.simConnectService.SnapshotReceived += this.SimConnectService_OnSnapshotReceived;
        this.simConnectService.LogMessage += this.SimConnectService_OnLogMessage;

        this.Loaded += this.MainWindow_OnLoaded;
        this.Closing += this.MainWindow_OnClosing;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) => this.simConnectService.Start();

    private async void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        this.closing = true;
        e.Cancel = true;

        await this.simConnectService.DisposeAsync();
        this.Close();
    }

    private async void ReconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Reconnexion manuelle demandée.");
        await this.simConnectService.RequestReconnectAsync();
    }

    private void ClearLogButton_OnClick(object sender, RoutedEventArgs e) => this.LogBox.Clear();

    private void SimConnectService_OnStatusChanged(object? sender, ConnectionStatus status)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.ConnectionText.Text = status.Message;
            this.StatusDot.Fill = status.State switch
            {
                ConnectionState.Connected => this.Brush("#47C98B"),
                ConnectionState.Error => this.Brush("#E46C6C"),
                ConnectionState.Disconnected => this.Brush("#E46C6C"),
                ConnectionState.Connecting => this.Brush("#E6A94A"),
                _ => this.Brush("#E6A94A"),
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

    private void SimConnectService_OnLogMessage(object? sender, string message) =>
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

    private SolidColorBrush Brush(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
