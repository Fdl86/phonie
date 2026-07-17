using Phonie.Models;
using Phonie.Utilities;
using SimConnect.NET;

namespace Phonie.Services;

public sealed class SimConnectService : IAsyncDisposable
{
    // LFBI ARP: 46°35'16"N 000°18'24"E.
    private const double LfbiLatitude = 46.587778;
    private const double LfbiLongitude = 0.306667;

    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly HashSet<string> optionalReadWarnings = new(StringComparer.Ordinal);
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private SimConnectClient? client;
    private DateTimeOffset lastRepeatedErrorLog = DateTimeOffset.MinValue;
    private string? lastErrorMessage;
    private bool connectedAtLeastOnce;
    private bool disposed;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler<SimulatorSnapshot>? SnapshotReceived;

    public event EventHandler<string>? LogMessage;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.worker is { IsCompleted: false })
        {
            return;
        }

        this.cancellation = new CancellationTokenSource();
        this.worker = Task.Run(() => this.RunAsync(this.cancellation.Token));
    }

    public async Task RequestReconnectAsync()
    {
        if (this.disposed)
        {
            return;
        }

        await this.ResetClientAsync().ConfigureAwait(false);
        this.PublishStatus(ConnectionState.Waiting, "Reconnexion demandée — nouvelle tentative automatique");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        this.PublishStatus(ConnectionState.Waiting, "En attente de Microsoft Flight Simulator");
        this.PublishLog("PHONIE DEV0.2.2 démarrée. Recherche locale de SimConnect.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (this.client is not { IsConnected: true })
                {
                    await this.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                if (this.client is { IsConnected: true } connectedClient)
                {
                    var snapshot = await this.ReadSnapshotAsync(connectedClient, cancellationToken).ConfigureAwait(false);
                    this.SnapshotReceived?.Invoke(this, snapshot);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var message = CleanMessage(exception);
                this.PublishRepeatedErrorWhenUseful(message);
                this.PublishStatus(
                    this.connectedAtLeastOnce ? ConnectionState.Disconnected : ConnectionState.Waiting,
                    this.connectedAtLeastOnce
                        ? "Connexion perdue — reconnexion automatique"
                        : "En attente du simulateur — nouvelle tentative automatique");

                await this.ResetClientAsync().ConfigureAwait(false);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        await this.ResetClientAsync().ConfigureAwait(false);
        this.PublishStatus(ConnectionState.Disconnected, "Arrêté");
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        this.PublishStatus(ConnectionState.Connecting, "Connexion à SimConnect…");

        var newClient = new SimConnectClient("PHONIE DEV0.2.2")
        {
            AutoReconnectEnabled = false,
        };

        try
        {
            await newClient.ConnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.Delay(350, cancellationToken).ConfigureAwait(false);

            await this.lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.client = newClient;
                this.optionalReadWarnings.Clear();
            }
            finally
            {
                this.lifecycleGate.Release();
            }

            var simulator = newClient.IsMSFS2024 ? "MSFS 2024" : "MSFS 2020";
            this.connectedAtLeastOnce = true;
            this.lastErrorMessage = null;
            this.PublishStatus(ConnectionState.Connected, $"Connecté — {simulator}");
            this.PublishLog($"Connexion SimConnect établie avec {simulator}.");
            this.PublishLog("Scanner radio actif : station, type de service, espacement et météo locale.");
        }
        catch
        {
            await newClient.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SimulatorSnapshot> ReadSnapshotAsync(
        SimConnectClient connectedClient,
        CancellationToken cancellationToken)
    {
        // Variables indispensables : une erreur sur l'une d'elles déclenche une reconnexion propre.
        var titleTask = connectedClient.SimVars.GetAsync<string>("TITLE", string.Empty, cancellationToken: cancellationToken);
        var latitudeTask = connectedClient.SimVars.GetAsync<double>("PLANE LATITUDE", "degrees", cancellationToken: cancellationToken);
        var longitudeTask = connectedClient.SimVars.GetAsync<double>("PLANE LONGITUDE", "degrees", cancellationToken: cancellationToken);
        var altitudeTask = connectedClient.SimVars.GetAsync<double>("PLANE ALTITUDE", "feet", cancellationToken: cancellationToken);
        var headingTask = connectedClient.SimVars.GetAsync<double>("PLANE HEADING DEGREES MAGNETIC", "degrees", cancellationToken: cancellationToken);
        var iasTask = connectedClient.SimVars.GetAsync<double>("AIRSPEED INDICATED", "knots", cancellationToken: cancellationToken);
        var groundSpeedTask = connectedClient.SimVars.GetAsync<double>("GROUND VELOCITY", "knots", cancellationToken: cancellationToken);
        var onGroundTask = connectedClient.SimVars.GetAsync<int>("SIM ON GROUND", "bool", cancellationToken: cancellationToken);
        var comActiveTask = connectedClient.SimVars.GetAsync<double>("COM ACTIVE FREQUENCY:1", "MHz", cancellationToken: cancellationToken);
        var comStandbyTask = connectedClient.SimVars.GetAsync<double>("COM STANDBY FREQUENCY:1", "MHz", cancellationToken: cancellationToken);
        var transponderTask = connectedClient.SimVars.GetAsync<int>("TRANSPONDER CODE:1", "BCO16", cancellationToken: cancellationToken);

        // Variables enrichies : elles sont optionnelles pour préserver la compatibilité entre avions et versions du simulateur.
        var stationIdentTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE FREQ IDENT:1", string.Empty, string.Empty, cancellationToken);
        var stationTypeTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE FREQ TYPE:1", string.Empty, string.Empty, cancellationToken);
        var spacingTask = this.GetOptionalAsync(connectedClient, "COM SPACING MODE:1", "enum", 0, cancellationToken);
        var receiveTask = this.GetOptionalAsync(connectedClient, "COM RECEIVE:1", "bool", 1, cancellationToken);
        var comStatusTask = this.GetOptionalAsync(connectedClient, "COM STATUS:1", "enum", 0, cancellationToken);
        var windDirectionTask = this.GetOptionalAsync(connectedClient, "AMBIENT WIND DIRECTION", "degrees", double.NaN, cancellationToken);
        var windVelocityTask = this.GetOptionalAsync(connectedClient, "AMBIENT WIND VELOCITY", "knots", double.NaN, cancellationToken);
        var qnhTask = this.GetOptionalAsync(connectedClient, "SEA LEVEL PRESSURE", "millibars", double.NaN, cancellationToken);
        var temperatureTask = this.GetOptionalAsync(connectedClient, "AMBIENT TEMPERATURE", "celsius", double.NaN, cancellationToken);
        var visibilityTask = this.GetOptionalAsync(connectedClient, "AMBIENT VISIBILITY", "meters", double.NaN, cancellationToken);

        await Task.WhenAll(
            titleTask,
            latitudeTask,
            longitudeTask,
            altitudeTask,
            headingTask,
            iasTask,
            groundSpeedTask,
            onGroundTask,
            comActiveTask,
            comStandbyTask,
            transponderTask,
            stationIdentTask,
            stationTypeTask,
            spacingTask,
            receiveTask,
            comStatusTask,
            windDirectionTask,
            windVelocityTask,
            qnhTask,
            temperatureTask,
            visibilityTask).ConfigureAwait(false);

        var latitude = await latitudeTask.ConfigureAwait(false);
        var longitude = await longitudeTask.ConfigureAwait(false);
        var distance = GeoMath.DistanceNm(latitude, longitude, LfbiLatitude, LfbiLongitude);

        return new SimulatorSnapshot(
            DateTimeOffset.Now,
            connectedClient.IsMSFS2024 ? "MSFS 2024" : "MSFS 2020",
            (await titleTask.ConfigureAwait(false)).Trim(),
            latitude,
            longitude,
            await altitudeTask.ConfigureAwait(false),
            NormalizeHeading(await headingTask.ConfigureAwait(false)),
            await iasTask.ConfigureAwait(false),
            await groundSpeedTask.ConfigureAwait(false),
            await onGroundTask.ConfigureAwait(false) != 0,
            await comActiveTask.ConfigureAwait(false),
            await comStandbyTask.ConfigureAwait(false),
            (await stationIdentTask.ConfigureAwait(false)).Trim().ToUpperInvariant(),
            (await stationTypeTask.ConfigureAwait(false)).Trim().ToUpperInvariant(),
            await spacingTask.ConfigureAwait(false),
            await receiveTask.ConfigureAwait(false) != 0,
            await comStatusTask.ConfigureAwait(false),
            DecodeBco16(await transponderTask.ConfigureAwait(false)),
            distance,
            NormalizeHeading(await windDirectionTask.ConfigureAwait(false)),
            await windVelocityTask.ConfigureAwait(false),
            await qnhTask.ConfigureAwait(false),
            await temperatureTask.ConfigureAwait(false),
            await visibilityTask.ConfigureAwait(false));
    }

    private async Task<T> GetOptionalAsync<T>(
        SimConnectClient connectedClient,
        string name,
        string unit,
        T fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connectedClient.SimVars.GetAsync<T>(name, unit, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (this.optionalReadWarnings.Add(name))
            {
                this.PublishLog($"Donnée optionnelle indisponible ({name}) : {CleanMessage(exception)}");
            }

            return fallback;
        }
    }

    private async Task ResetClientAsync()
    {
        await this.lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.client is null)
            {
                return;
            }

            var oldClient = this.client;
            this.client = null;

            try
            {
                await oldClient.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                this.PublishLog($"Fermeture SimConnect : {CleanMessage(exception)}");
            }
        }
        finally
        {
            this.lifecycleGate.Release();
        }
    }

    private void PublishRepeatedErrorWhenUseful(string message)
    {
        var now = DateTimeOffset.Now;
        var sameError = string.Equals(message, this.lastErrorMessage, StringComparison.Ordinal);
        if (sameError && now - this.lastRepeatedErrorLog < TimeSpan.FromSeconds(30))
        {
            return;
        }

        this.lastErrorMessage = message;
        this.lastRepeatedErrorLog = now;
        this.PublishLog($"SimConnect indisponible : {message}");
    }

    private static double NormalizeHeading(double heading)
    {
        var normalized = heading % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static string DecodeBco16(int rawValue)
    {
        var first = (rawValue >> 12) & 0xF;
        var second = (rawValue >> 8) & 0xF;
        var third = (rawValue >> 4) & 0xF;
        var fourth = rawValue & 0xF;
        return $"{first}{second}{third}{fourth}";
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private void PublishStatus(ConnectionState state, string message) =>
        this.StatusChanged?.Invoke(this, new ConnectionStatus(state, message));

    private void PublishLog(string message) =>
        this.LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cancellation?.Cancel();

        if (this.worker is not null)
        {
            try
            {
                await this.worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        this.cancellation?.Dispose();
        this.lifecycleGate.Dispose();
    }
}
