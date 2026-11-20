using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StarComms.App;

public sealed class RawInput : IDisposable
{
    readonly HwndSource source;
    public event Action<string, bool>? Input;
    public event Action? Interrupted;
    public event Action<bool>? LockChanged;
    [StructLayout(LayoutKind.Sequential)] struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, System.Text.StringBuilder text, int size);
    [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
    public RawInput(IntPtr hwnd)
    {
        source = HwndSource.FromHwnd(hwnd)!; source.AddHook(Hook);
        Device[] devices = [new() { Page = 1, Usage = 6, Flags = 0x2100, Target = hwnd }, new() { Page = 1, Usage = 2, Flags = 0x2100, Target = hwnd }];
        if (!RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>())) throw new System.ComponentModel.Win32Exception();
        WTSRegisterSessionNotification(hwnd, 0);
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x2B1 && wParam.ToInt64() is 7 or 8) LockChanged?.Invoke(wParam.ToInt64() == 7);
        if (msg == 0x2B1 && wParam.ToInt64() == 7 || msg == 0x218 || msg == 0xFE) Interrupted?.Invoke();
        if (msg != 0xFF) return IntPtr.Zero;
        uint size = 0, header = (uint)(8 + IntPtr.Size * 2); GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, header); if (size is 0 or > 1024) return IntPtr.Zero;
        IntPtr data = Marshal.AllocHGlobal((int)size);
        try {
            if (GetRawInputData(lParam, 0x10000003, data, ref size, header) != size) return IntPtr.Zero;
            int type = Marshal.ReadInt32(data); int offset = (int)header;
            if (type == 1) {
                ushort scan = (ushort)Marshal.ReadInt16(data, offset), flags = (ushort)Marshal.ReadInt16(data, offset + 2), vk = (ushort)Marshal.ReadInt16(data, offset + 6);
                if (vk is 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165 || scan == 0) return IntPtr.Zero;
                Input?.Invoke(vk == 27 ? "Escape" : $"K:{scan}:{(flags & 6)}", (flags & 1) == 0);
            } else if (type == 0) {
                ushort buttons = (ushort)Marshal.ReadInt16(data, offset + 4);
                foreach (var (down, up, name) in new[] { (0x10, 0x20, "M:3"), (0x40, 0x80, "M:4"), (0x100, 0x200, "M:5") }) { if ((buttons & down) != 0) Input?.Invoke(name, true); if ((buttons & up) != 0) Input?.Invoke(name, false); }
            }
        } finally { Marshal.FreeHGlobal(data); }
        return IntPtr.Zero;
    }
    public static string Label(string? binding)
    {
        if (binding == null) return "Nastavit tlačítko";
        if (binding.StartsWith("M:")) return "Myš " + binding[2..];
        var p = binding.Split(':'); if (p.Length != 3 || !int.TryParse(p[1], out int scan) || !int.TryParse(p[2], out int flags)) return binding;
        var text = new System.Text.StringBuilder(80); GetKeyNameText(scan << 16 | ((flags & 2) != 0 ? 1 << 24 : 0), text, 80); return text.Length > 0 ? text.ToString() : binding;
    }
    public void Dispose() { source.RemoveHook(Hook); WTSUnRegisterSessionNotification(source.Handle); RegisterRawInputDevices([new() { Page = 1, Usage = 6, Flags = 1 }, new() { Page = 1, Usage = 2, Flags = 1 }], 2, (uint)Marshal.SizeOf<Device>()); }
}
