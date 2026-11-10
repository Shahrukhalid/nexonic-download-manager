using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Nexonic.App;

/// <summary>
/// The notification-area icon, drawn by Windows itself (Shell_NotifyIcon) with a WPF menu that follows the app's theme.
/// It replaces the Windows Forms tray icon, so the app no longer ships Windows Forms (about 25 MB installed).
/// </summary>
sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8000 + 1;  // WM_APP + 1
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    const uint NIIF_INFO = 1;
    const int WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, IntPtr[]? large, IntPtr[] small, uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);

    readonly HwndSource window;
    readonly IntPtr icon;
    readonly uint taskbarCreated;
    readonly string tip;
    bool added, disposed;

    public event Action? DoubleClick;
    public ContextMenu Menu { get; } = new() { Placement = PlacementMode.MousePoint };

    public TrayIcon(string tip)
    {
        this.tip = tip.Length > 127 ? tip[..127] : tip;
        // A hidden window that receives the icon's mouse messages (and Explorer's "taskbar restarted" broadcast).
        window = new HwndSource(new HwndSourceParameters("Nexonic Download Manager tray") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(Hook);
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        var small = new IntPtr[1];
        ExtractIconExW(Environment.ProcessPath!, 0, null, small, 1);
        icon = small[0];
        Add();
    }

    NotifyIconData Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = window.Handle, uID = 1, uFlags = flags,
        uCallbackMessage = CallbackMessage, hIcon = icon, szTip = tip, szInfo = "", szInfoTitle = "",
    };

    void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        added = Shell_NotifyIconW(NIM_ADD, ref d);
    }

    /// <summary>A notification next to the icon (Windows shows it as a toast).</summary>
    public void Balloon(string title, string text)
    {
        if (!added || disposed) return;
        var d = Data(NIF_INFO);
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..254] + "…" : text;
        d.uTimeoutOrVersion = 3000;
        d.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)lParam & 0xFFFF)
            {
                case WM_LBUTTONDBLCLK: DoubleClick?.Invoke(); break;
                case WM_RBUTTONUP or WM_CONTEXTMENU: ShowMenu(); break;
            }
            handled = true;
        }
        else if (taskbarCreated != 0 && msg == (int)taskbarCreated && !disposed) Add();  // Explorer restarted: show the icon again
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        // The menu must own the foreground, or it would not close when the user clicks elsewhere.
        SetForegroundWindow(window.Handle);
        Menu.IsOpen = true;
        if (PresentationSource.FromVisual(Menu) is HwndSource popup) SetForegroundWindow(popup.Handle);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (added) { var d = Data(0); Shell_NotifyIconW(NIM_DELETE, ref d); }
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        window.Dispose();
    }

    public static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }
}
