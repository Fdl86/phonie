using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Phonie.Services;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly HookProcedure hookProcedure;
    private readonly HashSet<int> pressedKeys = [];
    private IntPtr hookId;
    private bool disposed;

    public GlobalKeyboardHook()
    {
        this.hookProcedure = this.HookCallback;
    }

    public event EventHandler<int>? KeyPressed;

    public event EventHandler<int>? KeyReleased;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.hookId != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = GetModuleHandle(currentModule?.ModuleName);
        this.hookId = SetWindowsHookEx(WhKeyboardLl, this.hookProcedure, moduleHandle, 0);

        if (this.hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Impossible d'activer le PTT global (Win32 {Marshal.GetLastWin32Error()}).");
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            var virtualKey = unchecked((int)data.VirtualKeyCode);
            var message = wParam.ToInt32();

            if (message is WmKeyDown or WmSysKeyDown)
            {
                if (this.pressedKeys.Add(virtualKey))
                {
                    this.KeyPressed?.Invoke(this, virtualKey);
                }
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                if (this.pressedKeys.Remove(virtualKey))
                {
                    this.KeyReleased?.Invoke(this, virtualKey);
                }
            }
        }

        return CallNextHookEx(this.hookId, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.pressedKeys.Clear();

        if (this.hookId != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(this.hookId);
            this.hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProcedure callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookId, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
