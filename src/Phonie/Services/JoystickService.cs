using System.Runtime.InteropServices;
using Phonie.Models;

namespace Phonie.Services;

/// <summary>
/// Lightweight HOTAS/joystick button reader using the WinMM API already present in Windows.
/// Only connected devices are polled, at 20 Hz, which keeps CPU usage negligible while
/// remaining responsive enough for a push-to-talk button.
/// </summary>
public sealed class JoystickService : IAsyncDisposable
{
    private const uint JoyReturnButtons = 0x00000080;
    private const int JoyErrorNone = 0;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(2);

    private readonly object syncRoot = new();
    private readonly Dictionary<uint, DeviceState> devices = new();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private bool disposed;

    public event EventHandler<JoystickButtonEvent>? ButtonChanged;

    public event EventHandler<IReadOnlyList<JoystickDeviceInfo>>? DevicesChanged;

    public event EventHandler<string>? LogMessage;

    public IReadOnlyList<JoystickDeviceInfo> CurrentDevices
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.devices.Values.Select(state => state.Info).OrderBy(info => info.Name).ToArray();
            }
        }
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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var nextRescan = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextRescan)
                {
                    this.RescanDevices();
                    nextRescan = now + RescanInterval;
                }

                this.PollButtons();
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.PublishLog($"Lecture joystick interrompue : {CleanMessage(exception)}");

                try
                {
                    await Task.Delay(RescanInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void RescanDevices()
    {
        var discovered = new Dictionary<uint, DeviceState>();
        var count = NativeMethods.joyGetNumDevs();

        for (uint id = 0; id < count; id++)
        {
            var capsResult = NativeMethods.joyGetDevCapsW(new UIntPtr(id), out var caps, (uint)Marshal.SizeOf<JoyCaps>());
            if (capsResult != JoyErrorNone || caps.wNumButtons == 0)
            {
                continue;
            }

            var state = CreateJoyInfo();
            if (NativeMethods.joyGetPosEx(id, ref state) != JoyErrorNone)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(caps.szPname) ? $"Joystick {id + 1}" : caps.szPname.Trim();
            var key = $"{caps.wMid:X4}:{caps.wPid:X4}:{name}";
            var info = new JoystickDeviceInfo(id, key, name, Math.Min(caps.wNumButtons, 32u));

            lock (this.syncRoot)
            {
                if (this.devices.TryGetValue(id, out var existing) && string.Equals(existing.Info.Key, key, StringComparison.Ordinal))
                {
                    existing.Info = info;
                    discovered[id] = existing;
                }
                else
                {
                    discovered[id] = new DeviceState(info, state.dwButtons);
                }
            }
        }

        IReadOnlyList<JoystickDeviceInfo>? changedList = null;
        lock (this.syncRoot)
        {
            var oldSignature = DeviceSignature(this.devices.Values.Select(value => value.Info));
            var newSignature = DeviceSignature(discovered.Values.Select(value => value.Info));

            this.devices.Clear();
            foreach (var pair in discovered)
            {
                this.devices[pair.Key] = pair.Value;
            }

            if (!string.Equals(oldSignature, newSignature, StringComparison.Ordinal))
            {
                changedList = this.devices.Values.Select(value => value.Info).OrderBy(info => info.Name).ToArray();
            }
        }

        if (changedList is not null)
        {
            this.DevicesChanged?.Invoke(this, changedList);
            this.PublishLog(changedList.Count == 0
                ? "Aucun joystick/HOTAS détecté."
                : $"Joystick/HOTAS : {string.Join(", ", changedList.Select(device => device.Name))}.");
        }
    }

    private void PollButtons()
    {
        DeviceState[] snapshot;
        lock (this.syncRoot)
        {
            snapshot = this.devices.Values.ToArray();
        }

        foreach (var device in snapshot)
        {
            var joyInfo = CreateJoyInfo();
            if (NativeMethods.joyGetPosEx(device.Info.Id, ref joyInfo) != JoyErrorNone)
            {
                continue;
            }

            var currentButtons = joyInfo.dwButtons;
            var changedButtons = device.PreviousButtons ^ currentButtons;
            if (changedButtons == 0)
            {
                continue;
            }

            device.PreviousButtons = currentButtons;
            var buttonCount = (int)Math.Min(device.Info.ButtonCount, 32u);
            for (var index = 0; index < buttonCount; index++)
            {
                var mask = 1u << index;
                if ((changedButtons & mask) == 0)
                {
                    continue;
                }

                var pressed = (currentButtons & mask) != 0;
                this.ButtonChanged?.Invoke(this, new JoystickButtonEvent(device.Info, index + 1, pressed));
            }
        }
    }

    private static JoyInfoEx CreateJoyInfo() => new()
    {
        dwSize = (uint)Marshal.SizeOf<JoyInfoEx>(),
        dwFlags = JoyReturnButtons,
    };

    private static string DeviceSignature(IEnumerable<JoystickDeviceInfo> deviceInfos) =>
        string.Join("|", deviceInfos.OrderBy(info => info.Id).Select(info => $"{info.Id}:{info.Key}:{info.ButtonCount}"));

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

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
    }

    private sealed class DeviceState(JoystickDeviceInfo info, uint previousButtons)
    {
        public JoystickDeviceInfo Info { get; set; } = info;

        public uint PreviousButtons { get; set; } = previousButtons;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort wMid;
        public ushort wPid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        public static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)]
        public static extern int joyGetDevCapsW(UIntPtr uJoyID, out JoyCaps pjc, uint cbjc);

        [DllImport("winmm.dll")]
        public static extern int joyGetPosEx(uint uJoyID, ref JoyInfoEx pji);
    }
}
