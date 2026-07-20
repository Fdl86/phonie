using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Phonie.Models;

namespace Phonie.Services;

/// <summary>
/// Reads Windows GPU performance counters without vendor-specific tooling.
/// The PDH English-counter API is used so the code also works on localized Windows installations.
/// </summary>
public sealed class GpuTelemetryService : IDisposable
{
    private const uint ErrorSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFormatDouble = 0x00000200;

    private readonly int processId = Environment.ProcessId;
    private IntPtr query;
    private IntPtr gpuEngineCounter;
    private IntPtr gpuProcessDedicatedCounter;
    private IntPtr gpuProcessSharedCounter;
    private bool disposed;

    public GpuTelemetryService()
    {
        this.Adapters = ReadAdaptersFromRegistry();
        var openResult = PdhOpenQuery(null, IntPtr.Zero, out this.query);
        if (openResult != ErrorSuccess)
        {
            this.Status = $"Compteurs GPU Windows indisponibles (PDH 0x{openResult:X8})";
            this.query = IntPtr.Zero;
            return;
        }

        _ = TryAddCounter(this.query, @"\GPU Engine(*)\Utilization Percentage", out this.gpuEngineCounter);
        _ = TryAddCounter(this.query, @"\GPU Process Memory(*)\Dedicated Usage", out this.gpuProcessDedicatedCounter);
        _ = TryAddCounter(this.query, @"\GPU Process Memory(*)\Shared Usage", out this.gpuProcessSharedCounter);

        if (this.gpuEngineCounter == IntPtr.Zero
            && this.gpuProcessDedicatedCounter == IntPtr.Zero
            && this.gpuProcessSharedCounter == IntPtr.Zero)
        {
            this.Status = "Compteurs GPU Windows absents sur cette machine";
        }
        else
        {
            this.Status = "Compteurs GPU Windows actifs";
        }
    }

    public IReadOnlyList<GpuAdapterInfo> Adapters { get; }

    public string Status { get; private set; }

    public bool IsAvailable => this.query != IntPtr.Zero
        && (this.gpuEngineCounter != IntPtr.Zero
            || this.gpuProcessDedicatedCounter != IntPtr.Zero
            || this.gpuProcessSharedCounter != IntPtr.Zero);

    public async Task PrimeAsync(CancellationToken cancellationToken = default)
    {
        if (!this.IsAvailable)
        {
            return;
        }

        _ = PdhCollectQueryData(this.query);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        _ = PdhCollectQueryData(this.query);
    }

    public GpuTelemetrySnapshot Read()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsAvailable)
        {
            return new GpuTelemetrySnapshot(
                DateTimeOffset.Now,
                false,
                0,
                0,
                0,
                0,
                Array.Empty<int>(),
                this.Status);
        }

        var collectResult = PdhCollectQueryData(this.query);
        if (collectResult != ErrorSuccess)
        {
            this.Status = $"Lecture GPU indisponible (PDH 0x{collectResult:X8})";
            return new GpuTelemetrySnapshot(
                DateTimeOffset.Now,
                false,
                0,
                0,
                0,
                0,
                Array.Empty<int>(),
                this.Status);
        }

        var engineValues = ReadCounterArray(this.gpuEngineCounter);
        var dedicatedValues = ReadCounterArray(this.gpuProcessDedicatedCounter);
        var sharedValues = ReadCounterArray(this.gpuProcessSharedCounter);
        var pidToken = $"pid_{this.processId}_";

        var globalUtilization = engineValues.Count == 0
            ? 0
            : engineValues.Max(item => Math.Max(0, item.Value));
        var processEngines = engineValues
            .Where(item => item.Instance.Contains(pidToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var processUtilization = processEngines.Length == 0
            ? 0
            : processEngines.Max(item => Math.Max(0, item.Value));
        var processDedicated = SumBytes(dedicatedValues, pidToken);
        var processShared = SumBytes(sharedValues, pidToken);
        var physicalIndexes = processEngines
            .SelectMany(item => ExtractPhysicalAdapterIndexes(item.Instance))
            .Concat(dedicatedValues
                .Where(item => item.Instance.Contains(pidToken, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => ExtractPhysicalAdapterIndexes(item.Instance)))
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        return new GpuTelemetrySnapshot(
            DateTimeOffset.Now,
            true,
            Math.Clamp(processUtilization, 0, 100),
            Math.Clamp(globalUtilization, 0, 100),
            Math.Max(0, processDedicated),
            Math.Max(0, processShared),
            physicalIndexes,
            this.Status);
    }

    private static long SumBytes(IReadOnlyList<CounterValue> values, string pidToken)
    {
        double total = 0;
        foreach (var item in values)
        {
            if (item.Instance.Contains(pidToken, StringComparison.OrdinalIgnoreCase)
                && double.IsFinite(item.Value)
                && item.Value > 0)
            {
                total += item.Value;
            }
        }

        return total >= long.MaxValue ? long.MaxValue : (long)Math.Round(total);
    }

    private static IReadOnlyList<int> ExtractPhysicalAdapterIndexes(string instance)
    {
        var values = new List<int>();
        foreach (Match match in Regex.Matches(instance, @"phys_(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (int.TryParse(match.Groups[1].Value, out var index))
            {
                values.Add(index);
            }
        }

        return values;
    }

    private static bool TryAddCounter(IntPtr query, string path, out IntPtr counter)
    {
        var result = PdhAddEnglishCounter(query, path, IntPtr.Zero, out counter);
        if (result == ErrorSuccess)
        {
            return true;
        }

        counter = IntPtr.Zero;
        return false;
    }

    private static IReadOnlyList<CounterValue> ReadCounterArray(IntPtr counter)
    {
        if (counter == IntPtr.Zero)
        {
            return Array.Empty<CounterValue>();
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        var status = PdhGetFormattedCounterArray(counter, PdhFormatDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status != PdhMoreData || bufferSize == 0 || itemCount == 0)
        {
            return Array.Empty<CounterValue>();
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            status = PdhGetFormattedCounterArray(counter, PdhFormatDouble, ref bufferSize, ref itemCount, buffer);
            if (status != ErrorSuccess)
            {
                return Array.Empty<CounterValue>();
            }

            var itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
            var results = new List<CounterValue>(checked((int)itemCount));
            for (var index = 0; index < itemCount; index++)
            {
                var pointer = IntPtr.Add(buffer, checked((int)index * itemSize));
                var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(pointer);
                if (item.Name == IntPtr.Zero
                    || item.Value.Status != ErrorSuccess
                    || !double.IsFinite(item.Value.DoubleValue))
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    results.Add(new CounterValue(name, item.Value.DoubleValue));
                }
            }

            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<GpuAdapterInfo> ReadAdaptersFromRegistry()
    {
        var adapters = new List<GpuAdapterInfo>();
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var video = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return adapters;
            }

            foreach (var adapterKeyName in video.GetSubKeyNames())
            {
                using var adapterKey = video.OpenSubKey(adapterKeyName);
                if (adapterKey is null)
                {
                    continue;
                }

                foreach (var instanceName in adapterKey.GetSubKeyNames())
                {
                    using var instance = adapterKey.OpenSubKey(instanceName);
                    if (instance is null)
                    {
                        continue;
                    }

                    var name = instance.GetValue("HardwareInformation.AdapterString") as string
                        ?? instance.GetValue("DriverDesc") as string;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var memory = ReadMemoryValue(instance.GetValue("HardwareInformation.qwMemorySize"));
                    if (memory <= 0)
                    {
                        memory = ReadMemoryValue(instance.GetValue("HardwareInformation.MemorySize"));
                    }

                    adapters.Add(new GpuAdapterInfo(name.Trim(), Math.Max(0, memory), "Registre Windows"));
                }
            }
        }
        catch
        {
            // GPU counters can still work even when registry metadata is inaccessible.
        }

        return adapters
            .GroupBy(item => $"{item.Name}|{item.DedicatedMemoryBytes}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.DedicatedMemoryBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static long ReadMemoryValue(object? value)
    {
        return value switch
        {
            long signedLong => signedLong,
            ulong unsignedLong when unsignedLong <= long.MaxValue => (long)unsignedLong,
            int signedInt => unchecked((uint)signedInt),
            uint unsignedInt => unsignedInt,
            byte[] bytes when bytes.Length >= sizeof(long) => BitConverter.ToInt64(bytes, 0),
            byte[] bytes when bytes.Length >= sizeof(int) => unchecked((uint)BitConverter.ToInt32(bytes, 0)),
            _ => 0,
        };
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (this.query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(this.query);
            this.query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public IntPtr Name;
        public PdhFormattedCounterValue Value;
    }

    private sealed record CounterValue(string Instance, double Value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
