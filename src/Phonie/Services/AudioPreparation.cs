using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Phonie.Services;

internal static class AudioPreparation
{
    public static void CreateMono16KhzPcmWav(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? AppPaths.CacheDirectory);
        using var reader = new AudioFileReader(sourcePath);
        ISampleProvider mono = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => new ChannelToMonoSampleProvider(reader),
        };
        var resampled = new WdlResamplingSampleProvider(mono, 16_000);
        WaveFileWriter.CreateWaveFile16(destinationPath, resampled);
    }

    private sealed class ChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly float[] sourceBuffer;

        public ChannelToMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
            this.sourceBuffer = new float[source.WaveFormat.Channels * 4096];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var channels = this.source.WaveFormat.Channels;
            var requested = Math.Min(count * channels, this.sourceBuffer.Length);
            var read = this.source.Read(this.sourceBuffer, 0, requested);
            var frames = read / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0.0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += this.sourceBuffer[(frame * channels) + channel];
                }

                buffer[offset + frame] = sum / channels;
            }

            return frames;
        }
    }
}
