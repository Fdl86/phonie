using System.Collections.Concurrent;
using Phonie.Core;
using Phonie.Models;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Phonie.Services;

public sealed class SimConnectService : IAsyncDisposable
{
    private static readonly TimeSpan OptionalRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlightResetDebounce = TimeSpan.FromSeconds(2);
    private const uint SimStartEventId = 41_001;
    private const uint SimStopEventId = 41_002;
    private const uint FlightLoadedEventId = 41_003;

    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> optionalReadWarnings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> optionalRetryAfter = new(StringComparer.Ordinal);
    private readonly AirportFacilityService airportFacilityService = new();
    private readonly GroundTrafficService groundTrafficService = new();
    private readonly NearbyAirportService nearbyAirportService = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> automaticAirportRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AirportFacilityReport> airportReports = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private SimConnectClient? client;
    private DateTimeOffset lastRepeatedErrorLog = DateTimeOffset.MinValue;
    private string? lastErrorMessage;
    private bool connectedAtLeastOnce;
    private DateTimeOffset lastWeatherRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset lastGroundTrafficRequest = DateTimeOffset.MinValue;
    private WeatherSnapshot cachedWeather = WeatherSnapshot.Empty;
    private SimulatorSnapshot? previousSnapshot;
    private string currentGeographicIcao = string.Empty;
    private string currentRadioIcao = string.Empty;
    private string currentRadioSource = "Station radio non résolue";
    private bool disposed;
    private DateTimeOffset lastFlightReset = DateTimeOffset.MinValue;
    private FlightSessionResetReason? lastFlightResetReason;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler<SimulatorSnapshot>? SnapshotReceived;

    public event EventHandler<string>? LogMessage;

    public event EventHandler<AirportFacilityReport>? AirportDataReceived;

    public event EventHandler<GroundTrafficSnapshot>? GroundTrafficReceived;

    public event EventHandler<AirportContextChanged>? AirportContextChanged;

    public event EventHandler<FlightSessionResetEvent>? FlightSessionReset;

    public SimConnectService()
    {
        this.airportFacilityService.LogMessage += (_, message) => this.LogMessage?.Invoke(this, message);
        this.airportFacilityService.ReportCompleted += this.AirportFacilityService_OnReportCompleted;
        this.groundTrafficService.LogMessage += (_, message) => this.LogMessage?.Invoke(this, message);
        this.groundTrafficService.SnapshotReceived += (_, snapshot) => this.GroundTrafficReceived?.Invoke(this, snapshot);
        this.nearbyAirportService.LogMessage += (_, message) => this.LogMessage?.Invoke(this, message);
    }

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
        this.PublishStatus(ConnectionState.Waiting, "Reconnexion demandée - nouvelle tentative automatique");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        this.PublishStatus(ConnectionState.Waiting, "En attente de Microsoft Flight Simulator");
        this.PublishLog("PHONIE DEV0.4.1.7 démarrée. Détection dynamique des contextes aérodrome et radio.");

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
                    var previous = this.previousSnapshot;
                    if (previous is not null
                        && !string.Equals(previous.AircraftTitle, snapshot.AircraftTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        this.NotifyFlightSessionReset(
                            FlightSessionResetReason.AircraftChanged,
                            $"Appareil remplacé : {previous.AircraftTitle} -> {snapshot.AircraftTitle}.");
                    }
                    else if (previous is not null)
                    {
                        var previousAtcId = NormalizeAtcId(previous.AircraftAtcId);
                        var currentAtcId = NormalizeAtcId(snapshot.AircraftAtcId);
                        if (!string.IsNullOrWhiteSpace(previousAtcId)
                            && !string.IsNullOrWhiteSpace(currentAtcId)
                            && !string.Equals(previousAtcId, currentAtcId, StringComparison.OrdinalIgnoreCase))
                        {
                            this.NotifyFlightSessionReset(
                                FlightSessionResetReason.CallsignChanged,
                                $"Indicatif SimConnect remplacé : {previousAtcId} -> {currentAtcId}.");
                        }
                    }

                    var teleported = this.IsTeleport(snapshot);
                    _ = this.nearbyAirportService.RequestRefresh(teleported || this.previousSnapshot is null);
                    snapshot = this.ResolveAirportContexts(snapshot);
                    this.TryRequestAirportDataAutomatically(snapshot);
                    this.TryRequestGroundTraffic(snapshot);
                    this.previousSnapshot = snapshot;
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
                        ? "Connexion perdue - reconnexion automatique"
                        : "En attente du simulateur - nouvelle tentative automatique");

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
        this.PublishStatus(ConnectionState.Connecting, "Connexion à SimConnect...");

        var newClient = new SimConnectClient("PHONIE DEV0.4.1.7")
        {
            AutoReconnectEnabled = false,
        };

        try
        {
            await newClient.ConnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            // The pipe can report connected slightly before the simulator data service is ready.
            // A single warm-up read prevents a burst of parallel 10-second timeouts.
            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
            _ = await newClient.SimVars.GetAsync<string>("TITLE", string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);

            newClient.SystemEventReceived += this.SimConnectClient_OnSystemEventReceived;
            newClient.FilenameEventReceived += this.SimConnectClient_OnFilenameEventReceived;
            await newClient.SubscribeToEventAsync("SimStart", SimStartEventId, cancellationToken).ConfigureAwait(false);
            await newClient.SubscribeToEventAsync("SimStop", SimStopEventId, cancellationToken).ConfigureAwait(false);
            await newClient.SubscribeToEventAsync("FlightLoaded", FlightLoadedEventId, cancellationToken).ConfigureAwait(false);

            await this.lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.client = newClient;
                this.optionalReadWarnings.Clear();
                this.optionalRetryAfter.Clear();
                this.lastWeatherRefresh = DateTimeOffset.MinValue;
                this.cachedWeather = WeatherSnapshot.Empty;
                this.lastGroundTrafficRequest = DateTimeOffset.MinValue;
                this.automaticAirportRequests.Clear();
                this.airportReports.Clear();
                this.previousSnapshot = null;
                this.currentGeographicIcao = string.Empty;
                this.currentRadioIcao = string.Empty;
                this.currentRadioSource = "Station radio non résolue";
            }
            finally
            {
                this.lifecycleGate.Release();
            }

            var simulator = newClient.IsMSFS2024 ? "MSFS 2024" : "MSFS 2020";
            this.connectedAtLeastOnce = true;
            this.lastErrorMessage = null;
            this.PublishStatus(ConnectionState.Connected, $"Connecté - {simulator}");
            this.PublishLog($"Connexion SimConnect établie avec {simulator}.");
            this.PublishLog("Scanner radio actif : station, type de service, espacement et météo locale.");

            try
            {
                this.airportFacilityService.Attach(newClient, simulator);
            }
            catch (Exception exception)
            {
                this.PublishLog($"Airport Data indisponible : {CleanMessage(exception)}");
            }

            try
            {
                this.nearbyAirportService.Attach(newClient);
                _ = this.nearbyAirportService.RequestRefresh(force: true);
            }
            catch (Exception exception)
            {
                this.PublishLog($"Détection aérodrome indisponible : {CleanMessage(exception)}");
            }

            try
            {
                this.groundTrafficService.Attach(newClient);
            }
            catch (Exception exception)
            {
                this.PublishLog($"Trafic sol indisponible : {CleanMessage(exception)}");
                this.GroundTrafficReceived?.Invoke(this, new GroundTrafficSnapshot(
                    DateTimeOffset.UtcNow,
                    false,
                    Array.Empty<GroundTrafficContactData>(),
                    "Trafic sol SimConnect indisponible."));
            }
        }
        catch
        {
            await newClient.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool RequestAirportData(string icao)
    {
        if (this.disposed)
        {
            return false;
        }

        try
        {
            return this.airportFacilityService.RequestAirport(icao);
        }
        catch (Exception exception)
        {
            this.PublishLog($"Airport Data : demande impossible - {CleanMessage(exception)}");
            return false;
        }
    }


    public bool TryGetAirportReport(string? icao, out AirportFacilityReport? report)
    {
        var normalized = NormalizeIcao(icao);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            report = null;
            return false;
        }

        if (this.airportReports.TryGetValue(normalized, out var found))
        {
            report = found;
            return true;
        }

        report = null;
        return false;
    }

    private void AirportFacilityService_OnReportCompleted(object? sender, AirportFacilityReport report)
    {
        var icao = NormalizeIcao(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao);
        if (!string.IsNullOrWhiteSpace(icao))
        {
            this.airportReports[icao] = report;
        }

        this.AirportDataReceived?.Invoke(this, report);
    }

    private SimulatorSnapshot ResolveAirportContexts(SimulatorSnapshot snapshot)
    {
        var nearby = this.nearbyAirportService.Latest.Airports;
        var candidates = nearby.Select(item =>
        {
            var facilityFrequencies = this.airportReports.TryGetValue(item.Icao, out var report)
                ? report.Frequencies.Select(frequency => frequency.FrequencyMhz)
                : Enumerable.Empty<double>();
            var frequencies = facilityFrequencies
                .Concat(OfficialRadioCatalogService.GetPublishedFrequencies(item.Icao))
                .Where(value => double.IsFinite(value) && value > 0)
                .Distinct()
                .ToArray();
            return new NearbyAirportCandidate(
                item.Icao,
                item.Region,
                item.Latitude,
                item.Longitude,
                item.AltitudeMeters,
                frequencies);
        }).ToArray();

        var stationPosition = AirportContextResolver.ProjectStationPosition(
            snapshot.Latitude,
            snapshot.Longitude,
            snapshot.Com1StationBearingDegrees,
            snapshot.Com1StationDistanceMeters);
        var selection = AirportContextResolver.Resolve(
            candidates,
            snapshot.Latitude,
            snapshot.Longitude,
            snapshot.IsOnGround,
            this.currentGeographicIcao,
            snapshot.Com1StationIdent,
            snapshot.Com1ActiveMhz,
            stationPosition?.Latitude,
            stationPosition?.Longitude);

        var geographicChanged = !string.Equals(
            selection.GeographicIcao,
            this.currentGeographicIcao,
            StringComparison.OrdinalIgnoreCase);
        var radioChanged = !string.Equals(
            selection.RadioIcao,
            this.currentRadioIcao,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(selection.RadioSource, this.currentRadioSource, StringComparison.Ordinal);

        if (geographicChanged)
        {
            var previous = string.IsNullOrWhiteSpace(this.currentGeographicIcao) ? "aucun" : this.currentGeographicIcao;
            var next = string.IsNullOrWhiteSpace(selection.GeographicIcao) ? "aucun" : selection.GeographicIcao;
            this.PublishLog($"Contexte géographique : {previous} -> {next}.");
            this.lastGroundTrafficRequest = DateTimeOffset.MinValue;
            this.lastWeatherRefresh = DateTimeOffset.MinValue;
            if (!string.IsNullOrWhiteSpace(selection.GeographicIcao))
            {
                this.automaticAirportRequests.TryRemove(selection.GeographicIcao, out _);
            }
        }

        if (radioChanged)
        {
            var previous = string.IsNullOrWhiteSpace(this.currentRadioIcao) ? "aucun" : this.currentRadioIcao;
            var next = string.IsNullOrWhiteSpace(selection.RadioIcao) ? "aucun" : selection.RadioIcao;
            this.PublishLog($"Contexte radio : {previous} -> {next} ({selection.RadioSource}).");
            this.lastWeatherRefresh = DateTimeOffset.MinValue;
            if (!string.IsNullOrWhiteSpace(selection.RadioIcao))
            {
                this.automaticAirportRequests.TryRemove(selection.RadioIcao, out _);
            }
        }

        this.currentGeographicIcao = selection.GeographicIcao;
        this.currentRadioIcao = selection.RadioIcao;
        this.currentRadioSource = selection.RadioSource;

        var resolved = snapshot with
        {
            GeographicAirportIcao = selection.GeographicIcao,
            GeographicAirportDistanceNm = selection.GeographicDistanceNm,
            RadioAirportIcao = selection.RadioIcao,
            RadioContextSource = selection.RadioSource,
        };

        if (geographicChanged || radioChanged)
        {
            this.AirportContextChanged?.Invoke(this, new AirportContextChanged(
                DateTimeOffset.UtcNow,
                selection.GeographicIcao,
                selection.GeographicDistanceNm,
                selection.RadioIcao,
                selection.RadioSource,
                nearby));
        }

        return resolved;
    }

    private bool IsTeleport(SimulatorSnapshot snapshot)
    {
        if (this.previousSnapshot is null)
        {
            return true;
        }

        var distance = AirportContextResolver.DistanceNm(
            this.previousSnapshot.Latitude,
            this.previousSnapshot.Longitude,
            snapshot.Latitude,
            snapshot.Longitude);
        return double.IsFinite(distance) && distance >= 20.0;
    }

    private void TryRequestAirportDataAutomatically(SimulatorSnapshot snapshot)
    {
        this.TryRequestAirportReport(snapshot.GeographicAirportIcao, "géographique");
        if (!string.Equals(
                snapshot.RadioAirportIcao,
                snapshot.GeographicAirportIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            this.TryRequestAirportReport(snapshot.RadioAirportIcao, "radio");
        }
    }

    private void TryRequestAirportReport(string? icao, string role)
    {
        var candidate = NormalizeIcao(icao);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (this.automaticAirportRequests.TryGetValue(candidate, out var previousRequest)
            && now - previousRequest < TimeSpan.FromMinutes(10))
        {
            return;
        }

        try
        {
            if (this.airportFacilityService.RequestAirport(candidate))
            {
                this.automaticAirportRequests[candidate] = now;
                this.PublishLog($"Contexte {role} : rechargement Facilities {candidate}.");
            }
            else
            {
                this.automaticAirportRequests[candidate] = now - TimeSpan.FromMinutes(9.8);
                this.PublishLog($"Contexte {role} : demande Facilities {candidate} reportée, nouvelle tentative proche.");
            }
        }
        catch (Exception exception)
        {
            this.automaticAirportRequests[candidate] = now - TimeSpan.FromMinutes(9.8);
            this.PublishLog($"Airport Data automatique {candidate} : {CleanMessage(exception)}");
        }
    }

    private void TryRequestGroundTraffic(SimulatorSnapshot snapshot)
    {
        if (!snapshot.IsOnGround
            || string.IsNullOrWhiteSpace(snapshot.GeographicAirportIcao)
            || !double.IsFinite(snapshot.GeographicAirportDistanceNm)
            || snapshot.GeographicAirportDistanceNm > 10.0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - this.lastGroundTrafficRequest < TimeSpan.FromSeconds(2))
        {
            return;
        }

        this.lastGroundTrafficRequest = now;
        _ = this.groundTrafficService.RequestSnapshot();
    }

    private async Task<SimulatorSnapshot> ReadSnapshotAsync(
        SimConnectClient connectedClient,
        CancellationToken cancellationToken)
    {
        // Core variables are read first. Optional enrichment starts only after the core succeeds.
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
            transponderTask).ConfigureAwait(false);

        var latitude = await latitudeTask.ConfigureAwait(false);
        var longitude = await longitudeTask.ConfigureAwait(false);

        // Enriched variables are optional and independently cooled down after a failure.
        var aircraftAtcIdTask = this.GetOptionalAsync(connectedClient, "ATC ID", string.Empty, string.Empty, cancellationToken);
        var stationIdentTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE FREQ IDENT:1", string.Empty, string.Empty, cancellationToken);
        var stationTypeTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE FREQ TYPE:1", string.Empty, string.Empty, cancellationToken);
        var spacingTask = this.GetOptionalAsync(connectedClient, "COM SPACING MODE:1", "enum", 0, cancellationToken);
        var receiveTask = this.GetOptionalAsync(connectedClient, "COM RECEIVE:1", "bool", 1, cancellationToken);
        var comStatusTask = this.GetOptionalAsync(connectedClient, "COM STATUS:1", "enum", 0, cancellationToken);
        var stationBearingTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE BEARING:1", "degrees", double.NaN, cancellationToken);
        var stationDistanceTask = this.GetOptionalAsync(connectedClient, "COM ACTIVE DISTANCE:1", "meters", double.NaN, cancellationToken);
        var weatherTask = this.ReadWeatherWhenDueAsync(connectedClient, cancellationToken);

        await Task.WhenAll(
            aircraftAtcIdTask,
            stationIdentTask,
            stationTypeTask,
            spacingTask,
            receiveTask,
            comStatusTask,
            stationBearingTask,
            stationDistanceTask,
            weatherTask).ConfigureAwait(false);

        var weather = await weatherTask.ConfigureAwait(false);

        return new SimulatorSnapshot(
            DateTimeOffset.Now,
            connectedClient.IsMSFS2024 ? "MSFS 2024" : "MSFS 2020",
            (await titleTask.ConfigureAwait(false)).Trim(),
            NormalizeAtcId(await aircraftAtcIdTask.ConfigureAwait(false)),
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
            await stationBearingTask.ConfigureAwait(false),
            await stationDistanceTask.ConfigureAwait(false),
            DecodeBco16(await transponderTask.ConfigureAwait(false)),
            string.Empty,
            double.NaN,
            string.Empty,
            "Station radio non résolue",
            NormalizeHeading(weather.WindDirectionTrueDegrees),
            weather.WindVelocityKnots,
            weather.QnhHpa,
            weather.TemperatureCelsius,
            weather.DewPointCelsius,
            weather.VisibilityMeters,
            weather.CeilingFeet);
    }

    private async Task<WeatherSnapshot> ReadWeatherWhenDueAsync(
        SimConnectClient connectedClient,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - this.lastWeatherRefresh < TimeSpan.FromSeconds(30))
        {
            return this.cachedWeather;
        }

        var windDirectionTask = this.GetOptionalAsync(connectedClient, "AMBIENT WIND DIRECTION", "degrees", double.NaN, cancellationToken);
        var windVelocityTask = this.GetOptionalAsync(connectedClient, "AMBIENT WIND VELOCITY", "knots", double.NaN, cancellationToken);
        var qnhTask = this.GetOptionalAsync(connectedClient, "SEA LEVEL PRESSURE", "millibars", double.NaN, cancellationToken);
        var temperatureTask = this.GetOptionalAsync(connectedClient, "AMBIENT TEMPERATURE", "celsius", double.NaN, cancellationToken);
        var dewPointTask = this.GetOptionalAsync(connectedClient, "AMBIENT DEW POINT", "celsius", double.NaN, cancellationToken);
        var visibilityTask = this.GetOptionalAsync(connectedClient, "AMBIENT VISIBILITY", "meters", double.NaN, cancellationToken);
        var cloudBaseTask = this.GetOptionalAsync(connectedClient, "CLOUD BASE", "feet", double.NaN, cancellationToken);

        await Task.WhenAll(
            windDirectionTask,
            windVelocityTask,
            qnhTask,
            temperatureTask,
            dewPointTask,
            visibilityTask,
            cloudBaseTask).ConfigureAwait(false);

        this.cachedWeather = new WeatherSnapshot(
            await windDirectionTask.ConfigureAwait(false),
            await windVelocityTask.ConfigureAwait(false),
            await qnhTask.ConfigureAwait(false),
            await temperatureTask.ConfigureAwait(false),
            await dewPointTask.ConfigureAwait(false),
            await visibilityTask.ConfigureAwait(false),
            await cloudBaseTask.ConfigureAwait(false));
        this.lastWeatherRefresh = now;
        return this.cachedWeather;
    }

    private async Task<T> GetOptionalAsync<T>(
        SimConnectClient connectedClient,
        string name,
        string unit,
        T fallback,
        CancellationToken cancellationToken)
    {
        if (this.optionalRetryAfter.TryGetValue(name, out var retryAfter) && retryAfter > DateTimeOffset.UtcNow)
        {
            return fallback;
        }

        try
        {
            var value = await connectedClient.SimVars.GetAsync<T>(name, unit, cancellationToken: cancellationToken).ConfigureAwait(false);
            this.optionalRetryAfter.TryRemove(name, out _);
            this.optionalReadWarnings.TryRemove(name, out _);
            return value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.optionalRetryAfter[name] = DateTimeOffset.UtcNow + OptionalRetryDelay;
            if (this.optionalReadWarnings.TryAdd(name, 0))
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
            if (!this.disposed && this.connectedAtLeastOnce)
            {
                this.NotifyFlightSessionReset(FlightSessionResetReason.ConnectionLost, "Connexion SimConnect interrompue.");
            }
            this.airportFacilityService.Detach();
            this.groundTrafficService.Detach();
            this.nearbyAirportService.Detach();
            this.automaticAirportRequests.Clear();
            this.airportReports.Clear();
            this.previousSnapshot = null;
            this.currentGeographicIcao = string.Empty;
            this.currentRadioIcao = string.Empty;
            this.currentRadioSource = "Station radio non résolue";
            this.cachedWeather = WeatherSnapshot.Empty;
            this.lastWeatherRefresh = DateTimeOffset.MinValue;
            this.lastGroundTrafficRequest = DateTimeOffset.MinValue;

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

    private void SimConnectClient_OnSystemEventReceived(object? sender, SimSystemEventReceivedEventArgs eventArgs)
    {
        if (eventArgs.EventId == SimStopEventId)
        {
            this.NotifyFlightSessionReset(FlightSessionResetReason.SimStopped, "MSFS a quitté ou arrêté la session de vol.");
        }
        else if (eventArgs.EventId == SimStartEventId)
        {
            this.NotifyFlightSessionReset(FlightSessionResetReason.SimStarted, "MSFS a démarré ou redémarré la session de vol.");
        }
    }

    private void SimConnectClient_OnFilenameEventReceived(object? sender, SimSystemEventFilenameReceivedEventArgs eventArgs)
    {
        var detail = string.IsNullOrWhiteSpace(eventArgs.FileName)
            ? "Nouveau vol chargé par MSFS."
            : $"Nouveau vol chargé : {Path.GetFileName(eventArgs.FileName)}.";
        this.NotifyFlightSessionReset(FlightSessionResetReason.FlightLoaded, detail);
    }

    private void NotifyFlightSessionReset(FlightSessionResetReason reason, string detail)
    {
        var now = DateTimeOffset.UtcNow;
        if (this.lastFlightResetReason == reason && now - this.lastFlightReset < FlightResetDebounce)
        {
            return;
        }

        this.lastFlightResetReason = reason;
        this.lastFlightReset = now;
        this.previousSnapshot = null;
        this.currentGeographicIcao = string.Empty;
        this.currentRadioIcao = string.Empty;
        this.currentRadioSource = "Station radio non résolue";
        this.FlightSessionReset?.Invoke(this, new FlightSessionResetEvent(now, reason, detail));
        this.PublishLog($"Nouvelle session de vol : {detail}");
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

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }

    private static string NormalizeAtcId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = new string(value.Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (compact.Length >= 4 && char.IsAsciiLetter(compact[0]))
        {
            return $"{compact[0]}-{compact[1..]}";
        }

        return value.Trim().ToUpperInvariant();
    }

    private static double NormalizeHeading(double heading)
    {
        if (!double.IsFinite(heading))
        {
            return double.NaN;
        }

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

    private sealed record WeatherSnapshot(
        double WindDirectionTrueDegrees,
        double WindVelocityKnots,
        double QnhHpa,
        double TemperatureCelsius,
        double DewPointCelsius,
        double VisibilityMeters,
        double CeilingFeet)
    {
        public static WeatherSnapshot Empty { get; } = new(
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN);
    }

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

        this.airportFacilityService.Dispose();
        this.groundTrafficService.Dispose();
        this.nearbyAirportService.Dispose();
        this.cancellation?.Dispose();
        this.lifecycleGate.Dispose();
    }
}
