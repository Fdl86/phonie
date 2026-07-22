using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonie.Core;
using Phonie.Models;

namespace Phonie.Services;

public sealed class RadioDataUpdateService : IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private bool disposed;

    public RadioDataUpdateService()
    {
        client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PHONIE", "0.4.1.3"));
    }

    public event EventHandler<string>? LogMessage;

    public async Task<SiaRadioUpdateResult> CheckAndUpdateAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var localStatus = OfficialRadioCatalogService.Reload();
            var source = LoadUpdateSource();
            if (string.IsNullOrWhiteSpace(source.ManifestUrl))
            {
                return new SiaRadioUpdateResult(false, false, "URL du manifest radio absente.", localStatus);
            }

            PublishLog($"Contrôle base radio SIA : {source.ManifestUrl}");
            var manifestUri = new Uri(source.ManifestUrl, UriKind.Absolute);
            var remoteBytes = await client.GetByteArrayAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            var remote = JsonSerializer.Deserialize<SiaRadioManifest>(remoteBytes, jsonOptions)
                ?? throw new InvalidDataException("Manifest radio distant vide.");
            ValidateRemoteManifest(remote);

            var localManifest = ReadLocalManifest();
            if (!force
                && !remote.BootstrapRequired
                && ManifestsCarrySameDatasets(localManifest, remote)
                && localStatus.Valid)
            {
                return new SiaRadioUpdateResult(true, false, "Base radio SIA déjà à jour.", localStatus);
            }

            if (remote.BootstrapRequired || remote.Current is null)
            {
                throw new InvalidDataException("Le dépôt distant ne contient pas encore de base SIA publiée.");
            }

            ResetStaging();
            var descriptors = new[]
            {
                ("previous", remote.Previous),
                ("current", remote.Current),
                ("next", remote.Next),
            };
            foreach (var (label, descriptor) in descriptors)
            {
                if (descriptor is null)
                {
                    continue;
                }

                await DownloadAndValidateAsync(
                    manifestUri,
                    label,
                    descriptor,
                    cancellationToken).ConfigureAwait(false);
            }

            CommitStaging(remote);
            var updatedStatus = OfficialRadioCatalogService.Reload();
            if (!updatedStatus.Valid)
            {
                throw new InvalidDataException($"Base téléchargée non activable : {updatedStatus.Message}");
            }

            return new SiaRadioUpdateResult(
                true,
                true,
                $"Base radio SIA mise à jour : {updatedStatus.AiracCycle} - {updatedStatus.AirportCount} aérodromes.",
                updatedStatus);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var status = OfficialRadioCatalogService.Reload();
            var message = $"Mise à jour radio SIA impossible : {Clean(exception)}";
            PublishLog(message);
            return new SiaRadioUpdateResult(false, false, message, status);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DownloadAndValidateAsync(
        Uri manifestUri,
        string label,
        SiaRadioDatasetDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ValidateRelativePath(descriptor.RelativePath);
        if (descriptor.AirportCount < 100 || descriptor.FrequencyCount < 100)
        {
            throw new InvalidDataException(
                $"Couverture distante insuffisante pour {label} : {descriptor.AirportCount}/{descriptor.FrequencyCount}.");
        }

        var datasetUri = new Uri(manifestUri, descriptor.RelativePath.Replace('\\', '/'));
        PublishLog($"Téléchargement base SIA {label} : {datasetUri}");
        var bytes = await client.GetByteArrayAsync(datasetUri, cancellationToken).ConfigureAwait(false);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(sha, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 incorrect pour la base {label}.");
        }

        var stagingPath = Path.Combine(AppPaths.FranceRadioStagingDirectory, descriptor.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        await File.WriteAllBytesAsync(stagingPath, bytes, cancellationToken).ConfigureAwait(false);
        var catalog = SiaRadioCatalog.Load(stagingPath);
        if (!string.Equals(catalog.Dataset.Revision, descriptor.Revision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Révision incorrecte pour la base {label}.");
        }

        if (catalog.Dataset.Airports.Count != descriptor.AirportCount
            || catalog.Dataset.Airports.Sum(item => item.Frequencies.Count) != descriptor.FrequencyCount)
        {
            throw new InvalidDataException($"Statistiques différentes du manifest pour la base {label}.");
        }
    }

    private void CommitStaging(SiaRadioManifest remote)
    {
        foreach (var descriptor in new[] { remote.Previous, remote.Current, remote.Next }.OfType<SiaRadioDatasetDescriptor>())
        {
            var sourcePath = Path.Combine(AppPaths.FranceRadioStagingDirectory, descriptor.RelativePath);
            var targetPath = Path.Combine(AppPaths.FranceRadioDataDirectory, descriptor.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var temporary = targetPath + ".new";
            File.Copy(sourcePath, temporary, true);
            File.Move(temporary, targetPath, true);
        }

        var manifestTemporary = AppPaths.FranceRadioManifestPath + ".new";
        File.WriteAllText(manifestTemporary, JsonSerializer.Serialize(remote, jsonOptions));
        File.Move(manifestTemporary, AppPaths.FranceRadioManifestPath, true);
        ResetStaging();
    }

    private static FranceRadioUpdateSource LoadUpdateSource()
    {
        var path = Path.Combine(AppPaths.RadioDataDirectory, "france-update-source.json");
        if (!File.Exists(path))
        {
            return new FranceRadioUpdateSource();
        }

        return JsonSerializer.Deserialize<FranceRadioUpdateSource>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new FranceRadioUpdateSource();
    }

    private SiaRadioManifest? ReadLocalManifest()
    {
        try
        {
            return File.Exists(AppPaths.FranceRadioManifestPath)
                ? JsonSerializer.Deserialize<SiaRadioManifest>(File.ReadAllText(AppPaths.FranceRadioManifestPath), jsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ManifestsCarrySameDatasets(
        SiaRadioManifest? local,
        SiaRadioManifest remote)
    {
        if (local is null)
        {
            return false;
        }

        return SameDescriptor(local.Previous, remote.Previous)
            && SameDescriptor(local.Current, remote.Current)
            && SameDescriptor(local.Next, remote.Next);
    }

    private static bool SameDescriptor(
        SiaRadioDatasetDescriptor? left,
        SiaRadioDatasetDescriptor? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Revision, right.Revision, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
            && left.EffectiveFrom == right.EffectiveFrom
            && left.EffectiveUntil == right.EffectiveUntil;
    }

    private static void ValidateRemoteManifest(SiaRadioManifest value)
    {
        if (value.SchemaVersion != 2)
        {
            throw new InvalidDataException($"Schéma manifest distant non pris en charge : {value.SchemaVersion}.");
        }

        if (!string.Equals(value.Authority, "SIA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Le manifest distant ne déclare pas le SIA comme autorité.");
        }
    }

    private static void ValidateRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Chemin radio distant invalide.");
        }
    }

    private static void ResetStaging()
    {
        try
        {
            if (Directory.Exists(AppPaths.FranceRadioStagingDirectory))
            {
                Directory.Delete(AppPaths.FranceRadioStagingDirectory, true);
            }
        }
        catch
        {
            // Le répertoire sera recréé et les fichiers ciblés remplacés.
        }

        Directory.CreateDirectory(AppPaths.FranceRadioStagingDirectory);
    }

    private void PublishLog(string message) => LogMessage?.Invoke(this, message);

    private static string Clean(Exception exception) =>
        exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
        client.Dispose();
    }

    private sealed class FranceRadioUpdateSource
    {
        public int SchemaVersion { get; set; } = 1;
        public string ManifestUrl { get; set; } =
            "https://raw.githubusercontent.com/Fdl86/phonie/main/data/radio/france/manifest.json";
        public string Authority { get; set; } = "SIA";
        public string Description { get; set; } = string.Empty;
    }
}
