using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeathAdderTool;

/// <summary>
/// Tiny macro language, one command per line:
///   ctrl+shift+m      press a key chord (any WinForms Keys name: a, f5, enter, volumeup, mediaplaypause...)
///   down ctrl / up ctrl   hold / release a key (anything still held is released when the macro ends)
///   up / down         on their own: the arrow keys
///   wait 100          sleep N ms (0-60000)
///   type Hello!       type text
///   run notepad.exe   launch a program / URL / file (args after the first token)
///   dpi 800 | dpi up | dpi down | dpi cycle
///   # comment
/// A macro runs once per button press (no "while held").
/// </summary>
public static class Macro
{
    public static Action<string>? DpiHandler; // set by the app: receives "800" / "up" / "down" / "cycle"

    public const int MaxWait = 60000, MinDpi = 100, MaxDpi = 20000;

    static string[] Lines(string script) => script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static string? Validate(string script)
    {
        int n = 0;
        foreach (var raw in Lines(script))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var (cmd, rest) = Split(line);
            if (rest.Length == 0 && cmd is "up" or "down") cmd = "";   // bare "up"/"down" = arrow key
            switch (cmd)
            {
                case "wait":
                    if (!int.TryParse(rest, out int ms) || ms < 0 || ms > MaxWait) return $"Line {n}: wait needs a number of ms from 0 to {MaxWait}";
                    break;
                case "type":
                    if (rest.Length == 0) return $"Line {n}: type needs some text";
                    break;
                case "run":
                    if (ParseRun(rest) is not { } r || r.file.Length == 0) return $"Line {n}: run needs a program, file or URL (put paths with spaces in \"quotes\")";
                    break;
                case "dpi":
                    if (!IsDpiArg(rest)) return $"Line {n}: dpi needs a number from {MinDpi} to {MaxDpi}, or up / down / cycle";
                    break;
                case "down": case "up":
                    if (ParseKey(rest) == null) return $"Line {n}: unknown key '{rest}'";
                    break;
                default:
                    if (ParseChord(line) == null) return $"Line {n}: unknown key or command '{line}'";
                    break;
            }
        }
        return null;
    }

    static bool IsDpiArg(string s) =>
        s.ToLowerInvariant() is "up" or "down" or "cycle" || (int.TryParse(s, out int d) && d >= MinDpi && d <= MaxDpi);

    public static void Run(string script)
    {
        var held = new List<Keys>();
        try
        {
            foreach (var raw in Lines(script))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var (cmd, rest) = Split(line);
                if (rest.Length == 0 && cmd is "up" or "down") cmd = "";   // bare "up"/"down" = arrow key
                try
                {
                    switch (cmd)
                    {
                        case "wait": if (int.TryParse(rest, out int ms)) Thread.Sleep(Math.Clamp(ms, 0, MaxWait)); break;
                        case "type": TypeText(TypeArg(raw)); break;
                        case "run": Launch(rest); break;
                        case "dpi": if (IsDpiArg(rest)) DpiHandler?.Invoke(rest.ToLowerInvariant()); break;
                        case "down":
                            if (ParseKey(rest) is { } dk) { Key(dk, false); if (!held.Contains(dk)) held.Add(dk); }
                            break;
                        case "up":
                            if (ParseKey(rest) is { } uk) { Key(uk, true); held.Remove(uk); }
                            break;
                        default:
                            var chord = ParseChord(line);
                            if (chord == null) break;
                            foreach (var k in chord) Key(k, false);
                            Thread.Sleep(15); // some games miss zero-length presses
                            for (int i = chord.Count - 1; i >= 0; i--) Key(chord[i], true);
                            break;
                    }
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }
        finally
        {
            // Never leave a key stuck down (missing "up", error mid-macro...).
            for (int i = held.Count - 1; i >= 0; i--) Key(held[i], true);
        }
    }

    static (string cmd, string rest) Split(string line)
    {
        int sp = line.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? (line.ToLowerInvariant(), "") : (line[..sp].ToLowerInvariant(), line[(sp + 1)..].Trim());
    }

    /// <summary>Text after "type " exactly as written (keeps inner/leading spaces after the single separator).</summary>
    static string TypeArg(string raw)
    {
        var s = raw.TrimStart();
        int sp = s.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? "" : s[(sp + 1)..].TrimEnd('\r');
    }

    static (string file, string args)? ParseRun(string s)
    {
        s = s.Trim();
        if (s.StartsWith('"'))
        {
            int e = s.IndexOf('"', 1);
            if (e < 0) return null; // unclosed quote
            return (s[1..e].Trim(), s[(e + 1)..].Trim());
        }
        int sp = s.IndexOf(' ');
        return sp > 0 ? (s[..sp], s[(sp + 1)..].Trim()) : (s, "");
    }

    static void Launch(string s)
    {
        if (ParseRun(s) is not { } r || r.file.Length == 0) return;
        Process.Start(new ProcessStartInfo(r.file, r.args) { UseShellExecute = true })?.Dispose();
    }

    static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Keys.LControlKey, ["control"] = Keys.LControlKey, ["shift"] = Keys.LShiftKey,
        ["alt"] = Keys.LMenu, ["win"] = Keys.LWin, ["esc"] = Keys.Escape, ["del"] = Keys.Delete,
        ["pgup"] = Keys.PageUp, ["pgdn"] = Keys.PageDown, ["ins"] = Keys.Insert, ["bksp"] = Keys.Back,
        ["backspace"] = Keys.Back, ["printscreen"] = Keys.PrintScreen, ["prtsc"] = Keys.PrintScreen,
        ["volup"] = Keys.VolumeUp, ["voldown"] = Keys.VolumeDown, ["mute"] = Keys.VolumeMute,
        ["play"] = Keys.MediaPlayPause, ["playpause"] = Keys.MediaPlayPause, ["next"] = Keys.MediaNextTrack,
        ["prev"] = Keys.MediaPreviousTrack, ["stop"] = Keys.MediaStop, ["menu"] = Keys.Apps,
        ["0"] = Keys.D0, ["1"] = Keys.D1, ["2"] = Keys.D2, ["3"] = Keys.D3, ["4"] = Keys.D4,
        ["5"] = Keys.D5, ["6"] = Keys.D6, ["7"] = Keys.D7, ["8"] = Keys.D8, ["9"] = Keys.D9,
    };

    // Enum names that aren't keyboard keys (masks, "no key", mouse buttons).
    static readonly HashSet<Keys> NotKeys = new()
    {
        Keys.None, Keys.KeyCode, Keys.Modifiers, Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2,
    };

    public static Keys? ParseKey(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (Aliases.TryGetValue(s, out var k)) return k;
        if (s.Contains(',') || char.IsDigit(s[0]) || s[0] == '-') return null;       // "a, b" would OR flags together
        if (Enum.TryParse<Keys>(s, true, out k) && Enum.IsDefined(k) && (k & Keys.Modifiers) == 0 && !NotKeys.Contains(k))
            return k;
        return null;
    }

    static List<Keys>? ParseChord(string s)
    {
        var list = new List<Keys>();
        foreach (var part in s.Split('+'))
        {
            var k = ParseKey(part);
            if (k == null) return null;
            if (!list.Contains(k.Value)) list.Add(k.Value);
        }
        return list.Count > 0 ? list : null;
    }

    // ---------- SendInput ----------

    static readonly HashSet<Keys> Extended = new()
    {
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Left, Keys.Right,
        Keys.Up, Keys.Down, Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Apps, Keys.Divide,
        Keys.NumLock, Keys.PrintScreen, Keys.VolumeUp, Keys.VolumeDown, Keys.VolumeMute, Keys.MediaPlayPause,
        Keys.MediaNextTrack, Keys.MediaPreviousTrack, Keys.MediaStop,
    };

    static void Key(Keys k, bool up)
    {
        var inp = new INPUT { type = 1 };
        inp.u.ki.wVk = (ushort)k;
        inp.u.ki.wScan = (ushort)MapVirtualKey((uint)k, 0);
        inp.u.ki.dwFlags = (up ? 2u : 0u) | (Extended.Contains(k) ? 1u : 0u);
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    static void TypeText(string text)
    {
        foreach (char c in text) // UTF-16 units; surrogate pairs are sent as two consecutive units, as Windows expects
        {
            var d = new INPUT { type = 1 }; d.u.ki.wScan = c; d.u.ki.dwFlags = 4; // KEYEVENTF_UNICODE
            var u = d; u.u.ki.dwFlags = 4 | 2;
            SendInput(2, new[] { d, u }, Marshal.SizeOf<INPUT>());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
}
