using System;
using System.Runtime.InteropServices;

namespace VentiloTray;

/// <summary>Icône de la zone de notification, en Win32 brut (WinUI 3 n'en fournit pas).</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = Native.WM_APP + 1;
    private const uint IconId = 1;
    private const uint CmdOpen = 1, CmdQuit = 2;

    private readonly Native.WndProc _proc; // gardé en champ : le GC ne doit pas le collecter
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreated;
    private IntPtr _hIcon;
    private string _tip = "Ventilateur";
    private bool _added;

    public event Action? Clicked;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _proc = WndProc;
        var cls = "VentiloTray.Tray." + Guid.NewGuid().ToString("N");
        var wc = new Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = Native.GetModuleHandle(null),
            lpszClassName = cls,
        };
        Native.RegisterClassEx(ref wc);
        _hwnd = Native.CreateWindowEx(0, cls, "VentiloTray", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    public void Update(IntPtr hIcon, string tip)
    {
        var old = _hIcon;
        _hIcon = hIcon;
        _tip = tip.Length > 127 ? tip[..127] : tip;
        var data = Data();
        if (!_added)
        {
            _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
            data.uVersion = Native.NOTIFYICON_VERSION_4;
            Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref data);
        }
        else
        {
            Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
        }
        if (old != IntPtr.Zero && old != hIcon) Native.DestroyIcon(old);
    }

    /// <summary>Rectangle de l'icône à l'écran (pixels physiques), si Windows le fournit.</summary>
    public Native.RECT? GetRect()
    {
        var id = new Native.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
        };
        return Native.Shell_NotifyIconGetRect(ref id, out var r) == 0 ? r : null;
    }

    private Native.NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP,
        uCallbackMessage = WM_TRAY,
        hIcon = _hIcon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            int evt = (int)((long)lParam & 0xFFFF);
            if (evt == Native.NIN_SELECT || evt == Native.NIN_KEYSELECT) Clicked?.Invoke();
            else if (evt == Native.WM_CONTEXTMENU) ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated)
        {
            // Explorer a redémarré : il faut recréer l'icône
            _added = false;
            Update(_hIcon, _tip);
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdOpen, "Ouvrir");
        Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
        Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdQuit, "Quitter");
        Native.GetCursorPos(out var pt);
        Native.SetForegroundWindow(_hwnd);
        int cmd = Native.TrackPopupMenuEx(menu,
            Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        Native.PostMessage(_hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Native.DestroyMenu(menu);
        if (cmd == CmdOpen) Clicked?.Invoke();
        else if (cmd == CmdQuit) QuitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data();
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            _added = false;
        }
        if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
        Native.DestroyWindow(_hwnd);
    }
}
