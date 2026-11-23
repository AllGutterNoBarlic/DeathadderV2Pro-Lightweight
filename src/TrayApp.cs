using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeathAdderTool;

/// <summary>
/// The always-running part: tray icon, F23/F24 hotkeys, macros. Deliberately free of WPF so it idles at a few MB.
/// The settings window runs as a separate, short-lived process (see <see cref="SettingsHost"/>).
/// </summary>
public sealed class TrayApp : IDisposable
{
    readonly MouseService _svc = new();
    readonly NotifyIcon _tray;
    readonly HotKeys _keys = new();
    readonly EventWaitHandle _reload = new(false, EventResetMode.AutoReset, MouseService.ReloadEventName);
    readonly RegisteredWaitHandle _reloadWait;

    /// <summary>Thread-safe way to ask the UI thread to show the window or quit.</summary>
    public HotKeys Messages => _keys;

    SynchronizationContext _ui = null!;
    int _macroRunning;               // 1 while a macro runs: presses during it are ignored, so macros never interleave
    volatile bool _disposed;
    int? _lastDpi, _lastBatt;        // shown instantly in the tray menu, refreshed in the background

    public TrayApp()
    {
        _keys.ShowRequested += ShowSettings;
        _keys.QuitRequested += Application.Exit;
        _keysOk = _keys.Register(MouseService.VkDpiUp) & _keys.Register(MouseService.VkDpiDown);
        _keys.Pressed += vk =>
        {
            if (Interlocked.Exchange(ref _macroRunning, 1) == 1) return;
            string script = vk == MouseService.VkDpiUp ? _svc.Cfg.DpiUpMacro : _svc.Cfg.DpiDownMacro;
            Task.Run(() => { try { Macro.Run(script); } finally { Volatile.Write(ref _macroRunning, 0); } });
        };
        Macro.DpiHandler = arg =>
        {
            var dpi = _svc.DpiCommand(arg);
            if (dpi != null) { _lastDpi = dpi; if (!int.TryParse(arg, out _)) Notify("DPI", dpi.ToString()!); }
        };

        // The settings window (other process) tells us when the config changed.
        // If the window couldn't reach the mouse when saving, the remap loop below keeps retrying it.
        _reloadWait = ThreadPool.RegisterWaitForSingleObject(_reload, (_, _) => { _svc.ReloadConfig(); StartRemapLoop(); }, null, -1, false);

        _tray = new NotifyIcon { Icon = AppIcon(SystemInformation.SmallIconSize.Width), Text = "DeathAdder V2 Pro (unofficial)", Visible = true };
        _tray.ContextMenuStrip = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext(); // installed by the ContextMenuStrip
        _tray.ContextMenuStrip.Opening += (_, _) => { BuildMenu(); RefreshMenuAsync(); };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        BuildMenu();

        if (!_keysOk)
            Notify("DPI buttons unavailable", "Another program is using F23/F24, so the DPI buttons keep their normal DPI function.", ToolTipIcon.Warning);

        StartRemapLoop();
    }

    readonly bool _keysOk;
    int _remapLoopRunning;

    /// <summary>Puts the configured button mapping on the mouse. If the mouse/dongle isn't reachable (asleep, unplugged),
    /// keeps trying quietly every 30 s. Without our hotkeys there's nothing to receive F23/F24, so then it keeps the
    /// factory DPI function instead of leaving the buttons dead.</summary>
    void StartRemapLoop()
    {
        if (Interlocked.Exchange(ref _remapLoopRunning, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                for (int i = 0; !_disposed; i++)
                {
                    if (_svc.ApplyRemap(_svc.Cfg.RemapDpiButtons && _keysOk)) break;
                    if (i == 0) CompactHeap();
                    Thread.Sleep(30000);
                }
            }
            finally { Volatile.Write(ref _remapLoopRunning, 0); }
            Thread.Sleep(3000);
            CompactHeap(); // free startup-only allocations
        });
    }

    /// <summary>Balloon notification, safe to call from any thread.</summary>
    void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.None) =>
        _ui.Post(_ => { if (!_disposed) _tray.ShowBalloonTip(1500, title, text, icon); }, null);

    /// <summary>Reads DPI/battery off the UI thread (the mouse can take a while to answer when asleep),
    /// then updates the open menu.</summary>
    void RefreshMenuAsync() => Task.Run(() =>
    {
        var (dpi, batt) = (_svc.GetDpi(), _svc.GetBattery());
        if (dpi == _lastDpi && batt == _lastBatt) return;
        (_lastDpi, _lastBatt) = (dpi, batt);
        _ui.Post(_ => { if (!_disposed && _tray.ContextMenuStrip!.Visible) BuildMenu(); }, null);
    });

    void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        int? dpi = _lastDpi, batt = _lastBatt;
        var head = dpi == null ? "Mouse not found" : $"DPI {dpi}" + (batt != null ? $"   ·   Battery {batt}%" : "");
        menu.Items.Add(new ToolStripMenuItem(head) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        foreach (var p in _svc.Cfg.DpiPresets.OrderBy(x => x))
        {
            var item = new ToolStripMenuItem((dpi == p ? "●  " : "     ") + $"{p} DPI");
            item.Click += (_, _) => Task.Run(() => { if (_svc.SetDpi(p)) _lastDpi = p; });
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        var auto = new ToolStripMenuItem((Autostart ? "✓  " : "     ") + "Start with Windows");
        auto.Click += (_, _) => Autostart = !Autostart;
        menu.Items.Add(auto);
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
    }

    /// <summary>Opens the settings window in its own process (or brings the existing one forward).
    /// When it closes, all of the UI's memory goes back to Windows.</summary>
    public void ShowSettings()
    {
        if (Program.Signal(SettingsHost.ActivateEventName)) return;
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--settings") { UseShellExecute = false });
        // Let just that window come to the front (Windows otherwise only flashes its taskbar button).
        if (child != null) AllowSetForegroundWindow(child.Id);
    }

    /// <summary>Free unused managed memory so the idle tray app stays small.</summary>
    static void CompactHeap()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    public static bool Autostart
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return k?.GetValue("DeathAdderTool") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) k.SetValue("DeathAdderTool", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("DeathAdderTool", false);
        }
    }

    static Icon AppIcon(int size)
    {
        using var s = MouseService.IconStream();
        return new Icon(s, new Size(size, size));
    }

    public void Dispose()
    {
        _disposed = true;
        _tray.Visible = false;
        _tray.Dispose();
        _keys.Dispose();
        _reloadWait.Unregister(null);
        _reload.Dispose();
        _svc.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Dark tray menu to match the settings window.</summary>
    sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(230, 231, 242) : Color.FromArgb(150, 152, 180);
            base.OnRenderItemText(e);
        }
        sealed class Colors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(24, 25, 39), Sel = Color.FromArgb(52, 54, 120), Line = Color.FromArgb(48, 50, 70);
            public override Color ToolStripDropDownBackground => Bg;
            public override Color MenuBorder => Line;
            public override Color MenuItemBorder => Sel;
            public override Color MenuItemSelected => Sel;
            public override Color MenuItemSelectedGradientBegin => Sel;
            public override Color MenuItemSelectedGradientEnd => Sel;
            public override Color SeparatorDark => Line;
            public override Color SeparatorLight => Bg;
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
        }
    }
}
