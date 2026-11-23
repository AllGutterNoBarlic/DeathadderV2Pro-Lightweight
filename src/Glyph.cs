using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeathAdderTool;

/// <summary>Line icon drawn on a 24x24 grid (no icon font or image files needed).</summary>
public sealed class Glyph : Viewbox
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Glyph), new PropertyMetadata("", (d, _) => ((Glyph)d).Build()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Glyph), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xC9, 0xCB, 0xF2)), (d, _) => ((Glyph)d).Build()));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Glyph), new PropertyMetadata(1.8, (d, _) => ((Glyph)d).Build()));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public Glyph() { Stretch = Stretch.Uniform; Width = 22; Height = 22; }

    static string C(double cx, double cy, double r) => string.Format(CultureInfo.InvariantCulture,
        "M{0},{1} A{2},{2} 0 1 0 {3},{1} A{2},{2} 0 1 0 {0},{1} ", cx - r, cy, r, cx + r);

    static readonly Dictionary<string, string> Paths = new()
    {
        ["mouse"] = "M7,9 A5,5 0 0 1 17,9 L17,15 A5,5 0 0 1 7,15 Z M12,4 L12,8.5",
        ["crosshair"] = C(12, 12, 7) + C(12, 12, 1.3) + "M12,2 L12,6 M12,18 L12,22 M2,12 L6,12 M18,12 L22,12",
        ["book"] = "M12,7 C10,5 6,4.5 3,5.5 L3,19 C6,18 10,18.5 12,20 C14,18.5 18,18 21,19 L21,5.5 C18,4.5 14,5 12,7 Z M12,7 L12,20",
        ["layers"] = "M12,3 L21,8 L12,13 L3,8 Z M3,12.5 L12,17.5 L21,12.5 M3,16.5 L12,21.5 L21,16.5",
        ["up"] = "M12,19 L12,5 M6,11 L12,5 L18,11",
        ["down"] = "M12,5 L12,19 M6,13 L12,19 L18,13",
        ["keyboard"] = "M3,6 L21,6 L21,18 L3,18 Z M7,10 L7.01,10 M11,10 L11.01,10 M15,10 L15.01,10 M17,10 L17.01,10 M7,14 L17,14",
        ["updown"] = "M8,20 L8,4 M4,8 L8,4 L12,8 M16,4 L16,20 M12,16 L16,20 L20,16",
        ["clock"] = C(12, 12, 9) + "M12,7 L12,12 L15.5,14",
        ["play"] = "M7,4.5 L19,12 L7,19.5 Z",
        ["check"] = "M5,12.5 L10,17.5 L19,7",
        ["save"] = "M12,4 L12,15 M7,10 L12,15 L17,10 M4,16 L4,20 L20,20 L20,16",
        ["link"] = "M9.5,14.5 L14.5,9.5 M11,7 L13.2,4.8 A3.8,3.8 0 0 1 19.2,10.8 L17,13 M13,17 L10.8,19.2 A3.8,3.8 0 0 1 4.8,13.2 L7,11",
        ["unlink"] = "M11,7 L13.2,4.8 A3.8,3.8 0 0 1 19.2,10.8 L17,13 M13,17 L10.8,19.2 A3.8,3.8 0 0 1 4.8,13.2 L7,11 M4,4 L20,20",
        ["syntax"] = "M4,5 L20,5 L20,19 L4,19 Z M8,10 L16,10 M8,14 L13,14",
        ["list"] = "M9,6 L20,6 M9,12 L20,12 M9,18 L20,18 M4.5,6 L4.51,6 M4.5,12 L4.51,12 M4.5,18 L4.51,18",
        ["search"] = C(11, 11, 6.5) + "M16,16 L20.5,20.5",
        ["bolt"] = "M13,3 L5,13.5 L11.5,13.5 L10.5,21 L19,10 L12.5,10 Z",
        ["plus"] ="M12,6 L12,18 M6,12 L18,12",
        ["min"] = "M6,12 L18,12",
        ["max"] = "M6,6 L18,6 L18,18 L6,18 Z",
        ["restore"] = "M6,9 L15,9 L15,18 L6,18 Z M9,9 L9,6 L18,6 L18,15 L15,15",
        ["close"] = "M6,6 L18,18 M18,6 L6,18",
        ["spinup"] = "M7,14 L12,9 L17,14",
        ["spindown"] = "M7,10 L12,15 L17,10",
    };

    void Build()
    {
        var g = new Grid { Width = 24, Height = 24 };
        if (Paths.TryGetValue(Kind ?? "", out var data))
            g.Children.Add(new Path
            {
                Data = Geometry.Parse(data), Stroke = Stroke, StrokeThickness = Thickness,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        Child = g;
    }
}
