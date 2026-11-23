using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeathAdderTool;

/// <summary>
/// Hidden message-only window that
///  - receives the F23/F24 hotkeys the mouse firmware sends after remapping (RegisterHotKey, so the app only
///    ever receives those exact keys - it never sees other keystrokes), and
///  - lets other threads post "show window" / "quit" requests onto the UI thread.
/// No WPF here, so the idle tray app doesn't load the UI framework.
/// </summary>
public sealed class HotKeys : NativeWindow, IDisposable
{
    public event Action<int>? Pressed;  // vk
    public event Action? ShowRequested;
    public event Action? QuitRequested;

    const int WM_HOTKEY = 0x0312, WM_SHOW = 0x8001, WM_QUIT_APP = 0x8002;
    const uint MOD_NOREPEAT = 0x4000;
    readonly Dictionary<int, int> _idToVk = new();
    int _nextId = 1;

    public HotKeys() => CreateHandle(new CreateParams { Caption = "DeathAdderTool", Parent = new IntPtr(-3) /*HWND_MESSAGE*/ });

    /// <summary>Registers vk with every modifier combination so it fires even while Ctrl/Shift/Alt/Win are held.
    /// Returns false if another program already owns the plain hotkey.</summary>
    public bool Register(int vk)
    {
        bool basic = false;
        for (uint mods = 0; mods < 16; mods++) // ALT=1 CTRL=2 SHIFT=4 WIN=8
        {
            int id = _nextId++;
            if (RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, (uint)vk))
            {
                _idToVk[id] = vk;
                if (mods == 0) basic = true;
            }
        }
        return basic;
    }

    /// <summary>Thread-safe.</summary>
    public void PostShow() => PostMessage(Handle, WM_SHOW, IntPtr.Zero, IntPtr.Zero);
    /// <summary>Thread-safe.</summary>
    public void PostQuit() => PostMessage(Handle, WM_QUIT_APP, IntPtr.Zero, IntPtr.Zero);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_HOTKEY when _idToVk.TryGetValue(m.WParam.ToInt32(), out int vk): Pressed?.Invoke(vk); return;
            case WM_SHOW: ShowRequested?.Invoke(); return;
            case WM_QUIT_APP: QuitRequested?.Invoke(); return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _idToVk.Keys) UnregisterHotKey(Handle, id);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
}
