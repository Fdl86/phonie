using System.Buffers.Binary;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Phonie.Models;

namespace Phonie.Services;

public sealed class AudioService : IDisposable
{
    private const double LimiterLinear = 0.8912509381337456; // -1 dBFS
    private static readonly TimeSpan MinimumRecordingDuration = TimeSpan.FromMilliseconds(250);
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    private readonly object syncRoot = new();
    private readonly object meterSyncRoot = new();
    private WasapiCapture? capture;
    private WaveFileWriter? writer;
    private WaveFormat? recordingWaveFormat;
    private Stopwatch? recordingWatch;
    private string? currentRecordingPath;
    private WasapiOut? playback;
    private AudioFileReader? playbackReader;
    private MMDevice? meterInputDevice;
    private string? meterInputDeviceId;
    private double recordingGainLinear = 1;
    private int recordingGainDb;
    private long limitedSampleCount;
    private double processedPeak;
    private bool unsupportedFormatWarningPublished;
    private bool disposed;

    public event EventHandler<string>? LogMessage;

    public event EventHandler<bool>? RecordingStateChanged;

    public event EventHandler<AudioRecordingResult>? RecordingCompleted;

    public string LastRecordingPath => Path.Combine(AppPaths.RecordingsDirectory, "last-ptt.wav");

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => this.GetDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => this.GetDevices(DataFlow.Render);

    public string? GetDefaultInputDeviceId() => this.GetDefaultDeviceId(DataFlow.Capture);

    public string? GetDefaultOutputDeviceId() => this.GetDefaultDeviceId(DataFlow.Render);

    public float GetInputPeak(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return 0;
        }

        lock (this.meterSyncRoot)
        {
            try
            {
                if (this.meterInputDevice is null || !string.Equals(this.meterInputDeviceId, deviceId, StringComparison.Ordinal))
                {
                    this.meterInputDevice?.Dispose();
                    using var enumerator = new MMDeviceEnumerator();
                    this.meterInputDevice = enumerator.GetDevice(deviceId);
                    this.meterInputDeviceId = deviceId;
                }

                return Math.Clamp(this.meterInputDevice.AudioMeterInformation.MasterPeakValue, 0, 1);
            }
            catch
            {
                this.meterInputDevice?.Dispose();
                this.meterInputDevice = null;
                this.meterInputDeviceId = null;
                return 0;
            }
        }
    }

    public bool StartRecording(string? inputDeviceId, int gainDb)
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
                Directory.CreateDirectory(AppPaths.RecordingsDirectory);
                var temporaryRecordingPath = Path.Combine(AppPaths.RecordingsDirectory, ".ptt-current.wav");
                if (File.Exists(temporaryRecordingPath))
                {
                    File.Delete(temporaryRecordingPath);
                }

                using var enumerator = new MMDeviceEnumerator();
                using var selectedDevice = enumerator.GetDevice(inputDeviceId);
                var newCapture = new WasapiCapture(selectedDevice);
                var newWriter = new WaveFileWriter(temporaryRecordingPath, newCapture.WaveFormat);

                newCapture.DataAvailable += this.Capture_OnDataAvailable;
                newCapture.RecordingStopped += this.Capture_OnRecordingStopped;

                this.capture = newCapture;
                this.writer = newWriter;
                this.recordingWaveFormat = newCapture.WaveFormat;
                this.currentRecordingPath = temporaryRecordingPath;
                this.recordingWatch = Stopwatch.StartNew();
                this.recordingGainDb = Math.Clamp(gainDb, 0, 18);
                this.recordingGainLinear = Math.Pow(10.0, this.recordingGainDb / 20.0);
                this.limitedSampleCount = 0;
                this.processedPeak = 0;
                this.unsupportedFormatWarningPublished = false;

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

    public void StopPlayback()
    {
        lock (this.syncRoot)
        {
            this.StopPlaybackInternal(true);
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
                this.StopPlaybackInternal(true);

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
                this.StopPlaybackInternal(false);
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
            var endpoints = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            var devices = new List<AudioDeviceInfo>(endpoints.Count);
            foreach (var device in endpoints)
            {
                try
                {
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
                finally
                {
                    device.Dispose();
                }
            }

            return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
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
            if (this.writer is null || this.recordingWaveFormat is null || e.BytesRecorded <= 0)
            {
                return;
            }

            var processed = this.ApplyGainAndLimiter(e.Buffer, e.BytesRecorded, this.recordingWaveFormat);
            this.limitedSampleCount += processed.LimitedSamples;
            this.processedPeak = Math.Max(this.processedPeak, processed.Peak);
            this.writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private AudioProcessStats ApplyGainAndLimiter(byte[] buffer, int count, WaveFormat format)
    {
        var subFormat = TryGetSubFormat(format);
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || subFormat == FloatSubFormat;
        var isPcm = format.Encoding == WaveFormatEncoding.Pcm || subFormat == PcmSubFormat;

        if (isFloat && format.BitsPerSample == 32)
        {
            return ProcessFloat32(buffer, count, this.recordingGainLinear);
        }

        if (isFloat && format.BitsPerSample == 64)
        {
            return ProcessFloat64(buffer, count, this.recordingGainLinear);
        }

        if (isPcm)
        {
            return format.BitsPerSample switch
            {
                8 => ProcessPcm8(buffer, count, this.recordingGainLinear),
                16 => ProcessPcm16(buffer, count, this.recordingGainLinear),
                24 => ProcessPcm24(buffer, count, this.recordingGainLinear),
                32 => ProcessPcm32(buffer, count, this.recordingGainLinear),
                _ => AudioProcessStats.Empty,
            };
        }

        if (!this.unsupportedFormatWarningPublished)
        {
            this.unsupportedFormatWarningPublished = true;
            this.PublishLog($"Gain micro non appliqué au format audio {format.Encoding} / {format.BitsPerSample} bits.");
        }

        return AudioProcessStats.Empty;
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
        var temporaryPath = this.currentRecordingPath;
        var duration = this.recordingWatch?.Elapsed ?? TimeSpan.Zero;
        var gainDb = this.recordingGainDb;
        var limitedSamples = this.limitedSampleCount;
        var peakPercent = Math.Clamp(this.processedPeak * 100.0, 0, 100);

        this.CleanupCapture();
        this.RecordingStateChanged?.Invoke(this, false);

        if (exception is not null)
        {
            DeleteQuietly(temporaryPath);
            this.PublishLog($"Enregistrement interrompu : {CleanMessage(exception)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(temporaryPath) || !File.Exists(temporaryPath))
        {
            return;
        }

        if (duration < MinimumRecordingDuration)
        {
            DeleteQuietly(temporaryPath);
            this.RecordingCompleted?.Invoke(this, new AudioRecordingResult(
                this.LastRecordingPath,
                duration,
                0,
                gainDb,
                limitedSamples,
                peakPercent,
                true));
            return;
        }

        try
        {
            File.Move(temporaryPath, this.LastRecordingPath, true);
            var fileInfo = new FileInfo(this.LastRecordingPath);
            this.RecordingCompleted?.Invoke(this, new AudioRecordingResult(
                this.LastRecordingPath,
                duration,
                fileInfo.Length,
                gainDb,
                limitedSamples,
                peakPercent,
                false));
        }
        catch (Exception moveException)
        {
            DeleteQuietly(temporaryPath);
            this.PublishLog($"Enregistrement non conservé : {CleanMessage(moveException)}");
        }
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
        this.recordingWaveFormat = null;
        this.recordingWatch = null;
        this.currentRecordingPath = null;
        this.recordingGainLinear = 1;
        this.recordingGainDb = 0;
        this.limitedSampleCount = 0;
        this.processedPeak = 0;
    }

    private void Playback_OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (this.syncRoot)
        {
            if (e.Exception is not null)
            {
                this.PublishLog($"Lecture interrompue : {CleanMessage(e.Exception)}");
            }

            this.StopPlaybackInternal(false);
        }
    }

    private void StopPlaybackInternal(bool requestStop)
    {
        if (this.playback is not null)
        {
            this.playback.PlaybackStopped -= this.Playback_OnPlaybackStopped;
            if (requestStop && this.playback.PlaybackState != PlaybackState.Stopped)
            {
                this.playback.Stop();
            }

            this.playback.Dispose();
        }

        this.playbackReader?.Dispose();
        this.playback = null;
        this.playbackReader = null;
    }

    private static Guid? TryGetSubFormat(WaveFormat format)
    {
        try
        {
            var property = format.GetType().GetProperty("SubFormat");
            return property?.GetValue(format) is Guid value ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static AudioProcessStats ProcessFloat32(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        var span = buffer.AsSpan(0, count);
        for (var offset = 0; offset + 4 <= span.Length; offset += 4)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            var value = BitConverter.Int32BitsToSingle(bits);
            var amplified = float.IsFinite(value) ? value * gain : 0;
            var limitedValue = Limit(amplified, ref limited, ref peak);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), BitConverter.SingleToInt32Bits((float)limitedValue));
        }

        return new AudioProcessStats(limited, peak);
    }

    private static AudioProcessStats ProcessFloat64(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        var span = buffer.AsSpan(0, count);
        for (var offset = 0; offset + 8 <= span.Length; offset += 8)
        {
            var bits = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
            var value = BitConverter.Int64BitsToDouble(bits);
            var amplified = double.IsFinite(value) ? value * gain : 0;
            var limitedValue = Limit(amplified, ref limited, ref peak);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), BitConverter.DoubleToInt64Bits(limitedValue));
        }

        return new AudioProcessStats(limited, peak);
    }

    private static AudioProcessStats ProcessPcm8(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        for (var index = 0; index < count; index++)
        {
            var value = (buffer[index] - 128) / 128.0;
            var limitedValue = Limit(value * gain, ref limited, ref peak);
            buffer[index] = (byte)Math.Clamp((int)Math.Round(limitedValue * 127.0 + 128.0), 0, 255);
        }

        return new AudioProcessStats(limited, peak);
    }

    private static AudioProcessStats ProcessPcm16(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        var span = buffer.AsSpan(0, count);
        for (var offset = 0; offset + 2 <= span.Length; offset += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
            var limitedValue = Limit(sample / 32768.0 * gain, ref limited, ref peak);
            var output = (short)Math.Clamp((int)Math.Round(limitedValue * 32767.0), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), output);
        }

        return new AudioProcessStats(limited, peak);
    }

    private static AudioProcessStats ProcessPcm24(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        for (var offset = 0; offset + 3 <= count; offset += 3)
        {
            var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((sample & 0x800000) != 0)
            {
                sample |= unchecked((int)0xFF000000);
            }

            var limitedValue = Limit(sample / 8388608.0 * gain, ref limited, ref peak);
            var output = Math.Clamp((int)Math.Round(limitedValue * 8388607.0), -8388608, 8388607);
            buffer[offset] = (byte)(output & 0xFF);
            buffer[offset + 1] = (byte)((output >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((output >> 16) & 0xFF);
        }

        return new AudioProcessStats(limited, peak);
    }

    private static AudioProcessStats ProcessPcm32(byte[] buffer, int count, double gain)
    {
        long limited = 0;
        double peak = 0;
        var span = buffer.AsSpan(0, count);
        for (var offset = 0; offset + 4 <= span.Length; offset += 4)
        {
            var sample = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            var limitedValue = Limit(sample / 2147483648.0 * gain, ref limited, ref peak);
            var scaled = Math.Round(limitedValue * 2147483647.0);
            var output = scaled <= int.MinValue ? int.MinValue : scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), output);
        }

        return new AudioProcessStats(limited, peak);
    }

    private static double Limit(double value, ref long limited, ref double peak)
    {
        var absolute = Math.Abs(value);
        if (absolute > LimiterLinear)
        {
            limited++;
            value = Math.CopySign(LimiterLinear, value);
            absolute = LimiterLinear;
        }

        peak = Math.Max(peak, absolute);
        return value;
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup only. A later startup will retry the temporary file.
        }
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
            this.StopPlaybackInternal(true);
        }

        lock (this.meterSyncRoot)
        {
            this.meterInputDevice?.Dispose();
            this.meterInputDevice = null;
            this.meterInputDeviceId = null;
        }
    }

    private readonly record struct AudioProcessStats(long LimitedSamples, double Peak)
    {
        public static AudioProcessStats Empty { get; } = new(0, 0);
    }
}
