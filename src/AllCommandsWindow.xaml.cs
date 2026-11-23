using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeathAdderTool;

/// <summary>Browsable list of every macro command, key name and example. Clicking one inserts it into a macro.</summary>
public partial class AllCommandsWindow : Window
{
    readonly SettingsWindow _settings;

    public AllCommandsWindow(SettingsWindow settings)
    {
        _settings = settings;
        InitializeComponent();
        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Build();
        };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1, round = 2;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        };
        Loaded += (_, _) => SearchBox.Focus();
        UpdateTarget();
        Build();
    }

    void TopTarget_Click(object sender, RoutedEventArgs e) { _settings.SetTarget(bottom: false); UpdateTarget(); }
    void BottomTarget_Click(object sender, RoutedEventArgs e) { _settings.SetTarget(bottom: true); UpdateTarget(); }
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void UpdateTarget()
    {
        bool bottom = _settings.TargetName == "Bottom";
        TopTarget.Tag = bottom ? null : "on";
        BottomTarget.Tag = bottom ? "on" : null;
        SubText.Text = $"Click any command to add it to the {_settings.TargetName} DPI button macro.";
    }

    void Build()
    {
        string q = SearchBox.Text.Trim();
        bool Match(CommandCatalog.Item i) => q.Length == 0
            || i.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
            || i.Desc.Contains(q, StringComparison.OrdinalIgnoreCase);

        SectionPanel.Children.Clear();
        var muted = (Brush)FindResource("Muted");
        var desc = new SolidColorBrush(Color.FromRgb(0xB5, 0xB7, 0xCE));
        int shown = 0;

        foreach (var s in CommandCatalog.Sections)
        {
            bool titleHit = q.Length > 0 && s.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
            var items = s.Items.Where(i => titleHit || Match(i)).ToArray();
            if (items.Length == 0) continue;
            shown += items.Length;

            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Glyph { Kind = s.Icon, Width = 22, Height = 22 });
            head.Children.Add(new TextBlock { Text = s.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(head);
            if (s.Blurb.Length > 0)
                body.Children.Add(new TextBlock { Text = s.Blurb, Foreground = muted, Margin = new Thickness(34, 2, 0, 0), TextWrapping = TextWrapping.Wrap });

            if (s.Keys)
            {
                var wrap = new WrapPanel { Margin = new Thickness(34, 12, 0, 0) };
                foreach (var i in items)
                {
                    var b = Chip(i);
                    if (i.Desc.Length > 0) b.Content = $"{i.Code}  {i.Desc}";
                    wrap.Children.Add(b);
                }
                body.Children.Add(wrap);
            }
            else
            {
                var grid = new Grid { Margin = new Thickness(34, 12, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MinWidth = 210 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int r = 0; r < items.Length; r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var chip = Chip(items[r]);
                    Grid.SetRow(chip, r);
                    grid.Children.Add(chip);
                    var d = new TextBlock { Text = items[r].Desc, Foreground = desc, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 7) };
                    Grid.SetRow(d, r); Grid.SetColumn(d, 1);
                    grid.Children.Add(d);
                }
                body.Children.Add(grid);
            }

            SectionPanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14), Child = body });
        }

        if (shown == 0)
            SectionPanel.Children.Add(new TextBlock { Text = $"Nothing matches \"{q}\".", Foreground = muted, FontSize = 15, Margin = new Thickness(4, 20, 0, 0) });
    }

    Button Chip(CommandCatalog.Item item)
    {
        var b = new Button { Style = (Style)FindResource("ChipBtn"), Content = item.Code };
        b.ToolTip = item.Text.Contains('\n') ? "Inserts:\n" + item.Text : "Click to insert";
        b.Click += (_, _) =>
        {
            _settings.InsertIntoMacro(item.Text, focus: false);
            StatusGlyph.Visibility = Visibility.Visible;
            StatusText.Text = $"Added \"{item.Code}\" to the {_settings.TargetName} button. Click Save in the main window to keep it.";
            StatusText.Foreground = Brushes.White;
        };
        return b;
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
