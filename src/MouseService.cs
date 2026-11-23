using System;
using System.Linq;

namespace DeathAdderTool;

/// <summary>Config + mouse access shared by the tray process and the settings-window process.</summary>
public sealed class MouseService : IDisposable
{
    // Keys the firmware sends after remapping (unused by normal keyboards).
    public const int VkDpiUp = 0x86, VkDpiDown = 0x87;       // VK_F23 / VK_F24
    public const byte HidDpiUp = 0x72, HidDpiDown = 0x73;    // HID usages F23 / F24

    /// <summary>Signalled by the settings window after Save so the tray process re-reads the config.</summary>
    public const string ReloadEventName = "DeathAdderTool.Reload";

    public Config Cfg { get; private set; } = Config.Load();
    RazerHid? _mouse;
    readonly object _lock = new();

    public void ReloadConfig() => Cfg = Config.Load();

    public void SaveConfig(Config c)
    {
        c.Save();   // throws on failure -> Cfg keeps the last saved state, so the window still knows it's unsaved
        Cfg = c;
        try { if (System.Threading.EventWaitHandle.TryOpenExisting(ReloadEventName, out var ev)) { ev.Set(); ev.Dispose(); } }
        catch (UnauthorizedAccessException) { } // tray running elevated while we aren't (or vice versa)
    }

    /// <summary>Runs an action against the mouse, reconnecting once if needed (wireless mice drop out when asleep).</summary>
    public T? WithMouse<T>(Func<RazerHid, T?> f)
    {
        lock (_lock)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    _mouse ??= RazerHid.FindMouse();
                    if (_mouse == null) return default;
                    var r = f(_mouse);
                    if (r != null && !(r is bool b && !b)) return r;
                }
                catch { }
                _mouse?.Dispose(); _mouse = null;
            }
            return default;
        }
    }

    public bool SetDpi(int dpi) => WithMouse(m => m.SetDpi(dpi, dpi));
    public int? GetDpi() => WithMouse(m => m.GetDpi()?.x);
    /// <summary>Battery % (null when wired or unreachable). Wired returns a sentinel inside WithMouse so
    /// "no battery" isn't mistaken for a failure that tears down and re-probes the device.</summary>
    public int? GetBattery() => WithMouse<int?>(m => m.IsWireless ? m.GetBattery() : -1) is int b && b >= 0 ? b : null;

    /// <summary>Handles the macro "dpi" command: a number, or up / down / cycle through the presets. Returns the new DPI.</summary>
    public int? DpiCommand(string arg)
    {
        var presets = Cfg.DpiPresets.OrderBy(x => x).ToArray();
        if (presets.Length == 0) presets = new[] { 800, 1600 };
        return WithMouse<int?>(m =>
        {
            int next;
            if (int.TryParse(arg, out int v)) next = Math.Clamp(v, 100, 20000);
            else
            {
                var cur = m.GetDpi()?.x ?? presets[0];
                next = arg switch
                {
                    "up" => presets.FirstOrDefault(p => p > cur, presets[^1]),
                    "down" => presets.LastOrDefault(p => p < cur, presets[0]),
                    _ => presets.FirstOrDefault(p => p > cur, presets[0]), // cycle
                };
            }
            return m.SetDpi(next, next) ? next : null;
        });
    }

    /// <summary>Writes the DPI-button mapping into the mouse: F23/F24 (macros) or the stock DPI up/down.</summary>
    public bool ApplyRemap(bool remap) => WithMouse(m => remap
        ? m.MapButtonToKey(RazerHid.ButtonDpiUp, HidDpiUp) && m.MapButtonToKey(RazerHid.ButtonDpiDown, HidDpiDown)
        : m.RestoreDpiButtons());

    public static System.IO.Stream IconStream() => typeof(MouseService).Assembly.GetManifestResourceStream("app.ico")!;

    public void Dispose() { lock (_lock) { _mouse?.Dispose(); _mouse = null; } }
}
