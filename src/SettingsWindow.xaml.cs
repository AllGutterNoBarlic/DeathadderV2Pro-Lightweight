using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeathAdderTool;

public partial class SettingsWindow : Window
{
    readonly MouseService _app;
    readonly List<int> _presets;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    TextBox _lastMacro;
    int? _currentDpi;
    bool _dpiBoxFilled;

    public SettingsWindow(MouseService app)
    {
        _app = app;
        InitializeComponent();
        using (var s = MouseService.IconStream()) Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        var c = app.Cfg;
        _presets = c.DpiPresets.Distinct().OrderBy(x => x).ToList();
        RemapBox.IsChecked = c.RemapDpiButtons;
        UpBox.Text = c.DpiUpMacro.Replace("\r", "").Replace("\n", "\r\n");
        DownBox.Text = c.DpiDownMacro.Replace("\r", "").Replace("\n", "\r\n");
        _lastMacro = UpBox;
        UpBox.GotKeyboardFocus += (_, _) => _lastMacro = UpBox;
        DownBox.GotKeyboardFocus += (_, _) => _lastMacro = DownBox;

        DpiBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DpiBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply_Click(this, new RoutedEventArgs()); };

        BuildReference();
        BuildPresetPills();

        // Fit smaller screens: the macro boxes absorb the difference.
        var work = SystemParameters.WorkArea;
        if (Height > work.Height - 16) Height = Math.Max(MinHeight = Math.Min(MinHeight, work.Height - 16), work.Height - 16);
        if (Width > work.Width - 16) Width = Math.Max(MinWidth = Math.Min(MinWidth, work.Width - 16), work.Width - 16);

        SourceInitialized += (_, _) => DarkTitleBar();
        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            Root.Margin = new Thickness(max ? 7 : 0);
            MaxGlyph.Kind = max ? "restore" : "max";
        };
        _timer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => { RefreshStatus(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    // ---------- status ----------

    bool _refreshing;

    async void RefreshStatus()
    {
        if (_refreshing) return; // a slow (sleeping) mouse can make ticks overlap
        _refreshing = true;
        int? dpi, batt;
        try { (dpi, batt) = await Task.Run(() => (_app.GetDpi(), _app.GetBattery())); }
        finally { _refreshing = false; }
        bool dpiChanged = dpi != _currentDpi;
        _currentDpi = dpi;
        bool ok = dpi != null;
        ConnText.Text = ok ? "Connected" : "Not connected";
        ConnGlyph.Kind = ok ? "link" : "unlink";
        ConnGlyph.Stroke = new SolidColorBrush(ok ? Color.FromRgb(0x8F, 0x92, 0xF7) : Color.FromRgb(0xE0, 0x6C, 0x75));
        DpiText.Text = dpi?.ToString() ?? "—";
        BattText.Text = batt is int b ? $"{b}%" : "—";
        BattFill.Width = 22.0 * Math.Clamp(batt ?? 0, 0, 100) / 100;
        if (!_dpiBoxFilled && dpi != null) { DpiBox.Text = dpi.ToString(); _dpiBoxFilled = true; }
        if (dpiChanged) BuildPresetPills(); // don't rebuild under an open right-click menu every tick
    }

    // ---------- DPI ----------

    int DpiBoxValue() => int.TryParse(DpiBox.Text, out var v) ? Math.Clamp(v, 100, 20000) : (_currentDpi ?? 800);

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        int v = DpiBoxValue();
        DpiBox.Text = v.ToString();
        await Task.Run(() => _app.SetDpi(v));
        RefreshStatus();
    }

    void SpinUp_Click(object sender, RoutedEventArgs e) => DpiBox.Text = Math.Min(20000, DpiBoxValue() / 100 * 100 + 100).ToString();
    void SpinDown_Click(object sender, RoutedEventArgs e) => DpiBox.Text = Math.Max(100, (DpiBoxValue() + 99) / 100 * 100 - 100).ToString();

    void BuildPresetPills()
    {
        var pill = (Style)FindResource("Pill");
        PresetPanel.Children.Clear();
        foreach (var p in _presets.OrderBy(x => x))
        {
            var b = new Button { Style = pill, Content = p.ToString(), Tag = p == _currentDpi ? "on" : null, ToolTip = "Set DPI · right-click to remove" };
            b.Click += (_, _) => { DpiBox.Text = p.ToString(); Apply_Click(b, new RoutedEventArgs()); };
            var remove = new MenuItem { Header = $"Remove {p}" };
            remove.IsEnabled = _presets.Count > 1; // keep at least one preset (dpi up/down/cycle need them)
            remove.Click += (_, _) => { _presets.Remove(p); BuildPresetPills(); };
            b.ContextMenu = new ContextMenu { Items = { remove } };
            PresetPanel.Children.Add(b);
        }
        var add = new Button { Style = pill, MinWidth = 36, Padding = new Thickness(8, 0, 8, 0), ToolTip = "Add the DPI value above as a preset",
            Content = new Glyph { Kind = "plus", Width = 16, Height = 16, Thickness = 2 } };
        add.Click += (_, _) => { int v = DpiBoxValue(); if (!_presets.Contains(v)) { _presets.Add(v); BuildPresetPills(); } };
        PresetPanel.Children.Add(add);
        PresetPanel.Children.Add(new TextBlock
        {
            Text = "(tray menu, dpi up/down/cycle)", Foreground = (Brush)FindResource("Muted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 8),
        });
    }

    // ---------- save / test ----------

    Config Current() => new()
    {
        RemapDpiButtons = RemapBox.IsChecked == true,
        DpiUpMacro = UpBox.Text.Replace("\r", ""),
        DpiDownMacro = DownBox.Text.Replace("\r", ""),
        DpiPresets = _presets.Count > 0 ? _presets.OrderBy(x => x).ToArray() : new Config().DpiPresets,
    };

    bool IsDirty()
    {
        Config a = Current(), b = _app.Cfg;
        return a.RemapDpiButtons != b.RemapDpiButtons || a.DpiUpMacro != b.DpiUpMacro || a.DpiDownMacro != b.DpiDownMacro
            || !a.DpiPresets.SequenceEqual(b.DpiPresets.OrderBy(x => x));
    }

    /// <summary>Validates and saves. Returns false if a macro has an error (and shows it).</summary>
    bool TrySave(out bool remap)
    {
        remap = RemapBox.IsChecked == true;
        foreach (var (box, name) in new[] { (UpBox, "Top"), (DownBox, "Bottom") })
        {
            var err = Macro.Validate(box.Text);
            if (err != null) { MessageBox.Show(this, $"{name} button macro — {err}", "Macro error", MessageBoxButton.OK, MessageBoxImage.Warning); box.Focus(); return false; }
        }
        try { _app.SaveConfig(Current()); }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't save settings:\n" + ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        return true;
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySave(out bool remap)) return;
        // Always (re)write the button mapping: fixes a mapping reset by Synapse, or a previous save while the mouse slept.
        // (Unchanged mappings are skipped by the device layer, so this doesn't wear the mouse's memory.)
        bool ok = await Task.Run(() => _app.ApplyRemap(remap));
        await Flash(SaveText, ok ? "Saved" : "Saved (mouse not reachable)", "Save");
    }

    /// <summary>The Test macro runs in this process; SettingsHost waits for it before exiting so no key is left held.</summary>
    public Task? TestTask { get; private set; }

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (TestTask is { IsCompleted: false }) return;
        var script = _lastMacro.Text;
        var err = Macro.Validate(script);
        if (err != null) { MessageBox.Show(this, err, "Macro error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        TestText.Text = "Running in 1.5s…";
        TestTask = Task.Run(() => { Thread.Sleep(1500); Macro.Run(script); });
        await TestTask;
        TestText.Text = "Test";
    }

    public bool ForceClose { get; set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || ForceClose || !IsDirty()) return;
        var r = MessageBox.Show(this, "You have unsaved changes. Save them before closing?", "DeathAdder V2 Pro",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes)
        {
            if (!TrySave(out bool remap)) { e.Cancel = true; return; }
            // Don't freeze the closing window on a sleeping mouse; the tray process retries the mapping anyway.
            Task.Run(() => _app.ApplyRemap(remap)).Wait(TimeSpan.FromSeconds(3));
        }
    }

    static async Task Flash(TextBlock t, string msg, string back)
    {
        t.Text = msg;
        await Task.Delay(1600);
        t.Text = back;
    }

    // ---------- command reference ----------

    void BuildReference()
    {
        Section("keyboard", "Key Chords", ("ctrl+shift+m", "Key chord\n(e.g., a, f5, enter, tab, …)"));
        Section("updown", "Key Control", ("down ctrl", "Hold a key"), ("up ctrl", "Release a key"), ("type Hello", "Type text"));
        Section("clock", "Timing", ("wait 100", "Pause (ms)"));
        Section("play", "Actions", ("run notepad.exe", "Launch app / file / URL"), ("dpi 800", "Set DPI"), ("dpi up | dpi down | dpi cycle", ""));
    }

    void Section(string icon, string title, params (string code, string desc)[] rows)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 14, 0, 7) };
        head.Children.Add(new Glyph { Kind = icon, Width = 22, Height = 22 });
        head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        RefPanel.Children.Add(head);

        var grid = new Grid { Margin = new Thickness(38, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement chip;
            if (rows[i].code.Contains(" | "))
            {
                var wrap = new WrapPanel();
                foreach (var part in rows[i].code.Split(" | ")) wrap.Children.Add(Chip(part, part));
                chip = wrap;
            }
            else chip = Chip(rows[i].code, rows[i].code);
            Grid.SetRow(chip, i);
            if (rows[i].desc.Length == 0) Grid.SetColumnSpan(chip, 2);
            grid.Children.Add(chip);
            if (rows[i].desc.Length > 0)
            {
                var d = new TextBlock { Text = rows[i].desc, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xB5, 0xB7, 0xCE)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 7) };
                Grid.SetRow(d, i); Grid.SetColumn(d, 1);
                grid.Children.Add(d);
            }
        }
        RefPanel.Children.Add(grid);
    }

    /// <summary>Clickable command chip: inserts its text into the macro box you last clicked into.</summary>
    Button Chip(string label, string insert)
    {
        var b = new Button { Style = (Style)FindResource("ChipBtn"), Content = label, ToolTip = $"Click to add to the {TargetName} button macro" };
        b.Click += (_, _) => InsertIntoMacro(insert, focus: true);
        b.ToolTipOpening += (_, _) => b.ToolTip = $"Click to add to the {TargetName} button macro";
        return b;
    }

    // ---------- inserting commands ----------

    /// <summary>"Top" or "Bottom": the macro box commands get inserted into.</summary>
    public string TargetName => _lastMacro == DownBox ? "Bottom" : "Top";

    public void SetTarget(bool bottom) => _lastMacro = bottom ? DownBox : UpBox;

    /// <summary>Inserts a command (or several lines) on its own line at the caret of the target macro box.</summary>
    public void InsertIntoMacro(string text, bool focus)
    {
        var box = _lastMacro;
        text = text.Replace("\r", "").Replace("\n", "\r\n");
        int caret = box.CaretIndex;
        string before = box.Text[..caret], after = box.Text[caret..];
        // Keep it on its own line: move to the end of the current line, start a new one if it isn't empty.
        int eol = after.IndexOf('\r'); if (eol < 0) eol = after.Length;
        before += after[..eol]; after = after[eol..];
        string lineStart = before.Contains('\n') ? before[(before.LastIndexOf('\n') + 1)..] : before;
        string prefix = lineStart.Trim().Length > 0 ? "\r\n" : "";
        box.Text = before + prefix + text + after;
        box.CaretIndex = before.Length + prefix.Length + text.Length;
        if (focus) box.Focus();
    }

    AllCommandsWindow? _allCommands;

    void AllCommands_Click(object sender, RoutedEventArgs e)
    {
        if (_allCommands == null)
        {
            _allCommands = new AllCommandsWindow(this) { Owner = this };
            _allCommands.Closed += (_, _) => _allCommands = null;
            _allCommands.Show();
        }
        _allCommands.Activate();
    }

    // ---------- window chrome ----------

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, round = 2;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));     // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE = round
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
