using System.IO;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Phonie.Models;

namespace Phonie.Services;

public sealed class AudioService : IDisposable
{
    private readonly object syncRoot = new();
    private WasapiCapture? capture;
    private WaveFileWriter? writer;
    private Stopwatch? recordingWatch;
    private string? currentRecordingPath;
    private WasapiOut? playback;
    private AudioFileReader? playbackReader;
    private bool disposed;

    public event EventHandler<string>? LogMessage;

    public event EventHandler<bool>? RecordingStateChanged;

    public event EventHandler<AudioRecordingResult>? RecordingCompleted;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        this.GetDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        this.GetDevices(DataFlow.Render);

    public string? GetDefaultInputDeviceId() => this.GetDefaultDeviceId(DataFlow.Capture);

    public string? GetDefaultOutputDeviceId() => this.GetDefaultDeviceId(DataFlow.Render);

    public float GetInputPeak(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return 0;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            return Math.Clamp(device.AudioMeterInformation.MasterPeakValue, 0, 1);
        }
        catch
        {
            return 0;
        }
    }

    public bool StartRecording(string? inputDeviceId)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        lock (this.syncRoot)
        {
            if (this.capture is not null || string.IsNullOrWhiteSpace(inputDeviceId))
            {
                return false;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var selectedDevice = enumerator.GetDevice(inputDeviceId);
                var newCapture = new WasapiCapture(selectedDevice);

                var recordingsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PHONIE",
                    "Recordings");

                Directory.CreateDirectory(recordingsDirectory);
                var recordingPath = Path.Combine(recordingsDirectory, $"ptt-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");
                var newWriter = new WaveFileWriter(recordingPath, newCapture.WaveFormat);

                newCapture.DataAvailable += this.Capture_OnDataAvailable;
                newCapture.RecordingStopped += this.Capture_OnRecordingStopped;

                this.capture = newCapture;
                this.writer = newWriter;
                this.currentRecordingPath = recordingPath;
                this.recordingWatch = Stopwatch.StartNew();

                newCapture.StartRecording();
                this.RecordingStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception exception)
            {
                this.CleanupCapture();
                this.PublishLog($"Microphone indisponible : {CleanMessage(exception)}");
                return false;
            }
        }
    }

    public void StopRecording()
    {
        lock (this.syncRoot)
        {
            try
            {
                this.capture?.StopRecording();
            }
            catch (Exception exception)
            {
                this.PublishLog($"Arrêt de l'enregistrement : {CleanMessage(exception)}");
                this.FinalizeRecording(exception);
            }
        }
    }

    public bool PlayFile(string? outputDeviceId, string? filePath)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (string.IsNullOrWhiteSpace(outputDeviceId) || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        lock (this.syncRoot)
        {
            try
            {
                this.StopPlaybackInternal();

                using var enumerator = new MMDeviceEnumerator();
                using var selectedDevice = enumerator.GetDevice(outputDeviceId);
                var reader = new AudioFileReader(filePath);
                var output = new WasapiOut(selectedDevice, AudioClientShareMode.Shared, true, 100);

                output.PlaybackStopped += this.Playback_OnPlaybackStopped;
                output.Init(reader);

                this.playbackReader = reader;
                this.playback = output;
                output.Play();
                return true;
            }
            catch (Exception exception)
            {
                this.StopPlaybackInternal();
                this.PublishLog($"Lecture audio impossible : {CleanMessage(exception)}");
                return false;
            }
        }
    }

    private IReadOnlyList<AudioDeviceInfo> GetDevices(DataFlow dataFlow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            this.PublishLog($"Inventaire audio impossible : {CleanMessage(exception)}");
            return [];
        }
    }

    private string? GetDefaultDeviceId(DataFlow dataFlow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                using var communicationDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Communications);
                return communicationDevice.ID;
            }
            catch
            {
                using var multimediaDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
                return multimediaDevice.ID;
            }
        }
        catch
        {
            return null;
        }
    }

    private void Capture_OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (this.syncRoot)
        {
            this.writer?.Write(e.Buffer, 0, e.BytesRecorded);
            this.writer?.Flush();
        }
    }

    private void Capture_OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (this.syncRoot)
        {
            this.FinalizeRecording(e.Exception);
        }
    }

    private void FinalizeRecording(Exception? exception)
    {
        var path = this.currentRecordingPath;
        var duration = this.recordingWatch?.Elapsed ?? TimeSpan.Zero;

        this.CleanupCapture();
        this.RecordingStateChanged?.Invoke(this, false);

        if (exception is not null)
        {
            this.PublishLog($"Enregistrement interrompu : {CleanMessage(exception)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var fileInfo = new FileInfo(path);
        var result = new AudioRecordingResult(path, duration, fileInfo.Length);
        this.RecordingCompleted?.Invoke(this, result);
    }

    private void CleanupCapture()
    {
        if (this.capture is not null)
        {
            this.capture.DataAvailable -= this.Capture_OnDataAvailable;
            this.capture.RecordingStopped -= this.Capture_OnRecordingStopped;
            this.capture.Dispose();
        }

        this.writer?.Dispose();
        this.recordingWatch?.Stop();
        this.capture = null;
        this.writer = null;
        this.recordingWatch = null;
        this.currentRecordingPath = null;
    }

    private void Playback_OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (this.syncRoot)
        {
            if (e.Exception is not null)
            {
                this.PublishLog($"Lecture interrompue : {CleanMessage(e.Exception)}");
            }

            this.StopPlaybackInternal();
        }
    }

    private void StopPlaybackInternal()
    {
        if (this.playback is not null)
        {
            this.playback.PlaybackStopped -= this.Playback_OnPlaybackStopped;
            this.playback.Stop();
            this.playback.Dispose();
        }

        this.playbackReader?.Dispose();
        this.playback = null;
        this.playbackReader = null;
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private void PublishLog(string message) =>
        this.LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        lock (this.syncRoot)
        {
            if (this.capture is not null)
            {
                try
                {
                    this.capture.StopRecording();
                }
                catch
                {
                    // Cleanup below.
                }
            }

            this.CleanupCapture();
            this.StopPlaybackInternal();
        }
    }
}
