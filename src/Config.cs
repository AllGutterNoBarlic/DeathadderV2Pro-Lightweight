using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeathAdderTool;

public sealed class Config
{
    public bool RemapDpiButtons { get; set; } = true;
    public string DpiUpMacro { get; set; } = "volumeup";
    public string DpiDownMacro { get; set; } = "volumedown";
    public int[] DpiPresets { get; set; } = { 400, 800, 1600, 3200, 6400 };

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeathAdderTool");
    static string File => Path.Combine(Dir, "config.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(File)) is { } c)
            {
                // Early versions put these header comments in the default macros; the UI labels the buttons already.
                c.DpiUpMacro = StripLine(c.DpiUpMacro ?? "", "# Top button (normally DPI up)");
                c.DpiDownMacro = StripLine(c.DpiDownMacro ?? "", "# Bottom button (normally DPI down)");
                // Hand-edited / damaged files: keep only sane presets.
                c.DpiPresets = (c.DpiPresets ?? Array.Empty<int>()).Where(p => p >= Macro.MinDpi && p <= Macro.MaxDpi).Distinct().OrderBy(p => p).ToArray();
                if (c.DpiPresets.Length == 0) c.DpiPresets = new Config().DpiPresets;
                return c;
            }
        }
        catch { }
        return new();
    }

    static string StripLine(string macro, string line) =>
        macro.StartsWith(line + "\n", StringComparison.Ordinal) ? macro[(line.Length + 1)..] : macro == line ? "" : macro;

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        // Write to a temp file then swap it in, so a crash mid-write can't leave a half-written config.
        var tmp = File + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        System.IO.File.Move(tmp, File, overwrite: true);
    }
}
