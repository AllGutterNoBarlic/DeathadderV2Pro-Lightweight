using System.Collections.Generic;
using System.Linq;

namespace DeathAdderTool;

/// <summary>Everything the macro language understands, for the "All commands" browser (and validated by --selftest).</summary>
public static class CommandCatalog
{
    /// <param name="Code">What's shown on the chip.</param>
    /// <param name="Desc">Explanation (empty for plain key names).</param>
    /// <param name="Insert">Text inserted into the macro when clicked (defaults to Code).</param>
    public record Item(string Code, string Desc = "", string? Insert = null)
    {
        public string Text => Insert ?? Code;
    }

    /// <param name="Keys">true = compact grid of key-name chips; false = rows of command + description.</param>
    public record Section(string Icon, string Title, string Blurb, bool Keys, Item[] Items);

    static Item[] K(params string[] names) => names.Select(n => new Item(n)).ToArray();
    static Item[] Range(string prefix, int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => new Item(prefix + i)).ToArray();

    public static readonly Section[] Sections =
    {
        new("syntax", "Commands", "One command per line, run top to bottom, once per button press.", false, new Item[]
        {
            new("ctrl+shift+m", "Press keys together, then release them. Join any key names below with +."),
            new("down shift", "Hold a key down until an up line. Anything still held is released when the macro ends."),
            new("up shift", "Release a key held with down."),
            new("type Hello!", "Type text exactly as written."),
            new("wait 100", "Pause, in milliseconds (0–60000)."),
            new("run notepad.exe", "Launch a program."),
            new("run https://github.com", "Open a website in your browser."),
            new("run \"C:\\Program Files\\App\\app.exe\" --flag", "Path with spaces in quotes, then arguments."),
            new("dpi 800", "Set an exact DPI (100–20000)."),
            new("dpi up", "Next higher DPI preset."),
            new("dpi down", "Next lower DPI preset."),
            new("dpi cycle", "Next preset, wrapping back to the lowest."),
            new("# note to self", "Comment. Ignored when the macro runs."),
        }),
        new("bolt", "Examples", "Ready-made macros. Click to insert the whole thing.", false, new Item[]
        {
            new("ctrl+shift+m", "Discord mute toggle: set Discord's Toggle Mute keybind to Ctrl+Shift+M."),
            new("win+shift+s", "Screenshot a region (Snipping Tool)."),
            new("ctrl+shift+esc", "Open Task Manager."),
            new("win+d", "Show the desktop."),
            new("dpi 400", "Sniper button: drop to 400 DPI (put dpi 1600 on the other)."),
            new("playpause", "Play / pause music."),
            new("ctrl+c → alt+tab → ctrl+v", "Copy, switch window, paste (5 lines, with short waits).", "ctrl+c\nwait 50\nalt+tab\nwait 150\nctrl+v"),
            new("enter → type gg wp → enter", "Send a message in game chat (4 lines).", "enter\nwait 50\ntype gg wp\nenter"),
            new("run https://youtube.com", "Open a website."),
        }),
        new("keyboard", "Modifiers", "Use in chords (ctrl+c) or with down / up.", true,
            K("ctrl", "shift", "alt", "win", "rcontrolkey", "rshiftkey", "rmenu", "rwin")),
        new("keyboard", "Letters & numbers", "", true,
            K("abcdefghijklmnopqrstuvwxyz".Select(c => c.ToString()).ToArray()).Concat(Range("", 0, 9)).ToArray()),
        new("keyboard", "Function keys", "", true, Range("f", 1, 24)),
        new("keyboard", "Editing & navigation", "", true,
            K("enter", "tab", "space", "backspace", "delete", "insert", "esc", "up", "down", "left", "right", "home", "end", "pgup", "pgdn")),
        new("play", "Media & volume", "", true,
            K("playpause", "next", "prev", "stop", "volup", "voldown", "mute", "selectmedia")),
        new("keyboard", "System", "", true,
            K("printscreen", "capslock", "numlock", "scroll", "pause", "apps", "sleep")),
        new("keyboard", "Numpad", "", true,
            Range("numpad", 0, 9).Concat(K("multiply", "add", "subtract", "divide", "decimal")).ToArray()),
        new("keyboard", "Punctuation", "Each chip shows the character it types (US layout).", true, new Item[]
        {
            new("oemminus", "-"), new("oemplus", "="), new("oemcomma", ","), new("oemperiod", "."),
            new("oemquestion", "/"), new("oemsemicolon", ";"), new("oemquotes", "'"), new("oemopenbrackets", "["),
            new("oemclosebrackets", "]"), new("oempipe", "\\"), new("oemtilde", "`"),
        }),
        new("link", "Browser & apps", "", true,
            K("browserback", "browserforward", "browserrefresh", "browserhome", "browsersearch", "browserfavorites",
              "launchmail", "launchapplication1", "launchapplication2")),
    };

    /// <summary>Every insertable snippet must be valid macro syntax. Returns the problems found.</summary>
    public static List<string> Validate() =>
        Sections.SelectMany(s => s.Items)
            .Select(i => (i, err: Macro.Validate(i.Text)))
            .Where(x => x.err != null)
            .Select(x => $"{x.i.Code}: {x.err}")
            .ToList();
}
