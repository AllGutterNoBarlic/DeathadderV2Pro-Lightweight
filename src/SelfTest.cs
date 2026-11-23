using System.Collections.Generic;

namespace DeathAdderTool;

/// <summary>
/// Offline checks run by --selftest (and CI). Only parsing/validation - nothing here sends input or talks to the mouse.
/// </summary>
static class SelfTest
{
    // (macro, should it be valid?)
    static readonly (string macro, bool ok)[] ParserCases =
    {
        // documented forms
        ("ctrl+shift+m", true), ("win+shift+s", true), ("alt+tab", true), ("CTRL+C", true), ("ctrl + c", true),
        ("down shift", true), ("up shift", true), ("down\tshift", true), ("up", true), ("down", true),
        ("type Hello!", true), ("type\thello", true), ("type   spaced", true), ("wait 0", true), ("wait 100", true),
        ("wait 60000", true), ("run notepad.exe", true), ("run https://github.com", true),
        ("run \"C:\\Program Files\\App\\app.exe\" --flag", true), ("dpi 800", true), ("dpi 100", true), ("dpi 20000", true),
        ("dpi up", true), ("dpi DOWN", true), ("dpi cycle", true), ("# comment", true), ("", true), ("   ", true),
        ("ctrl+c\r\nwait 50\r\nctrl+v", true), ("ctrl+c\rctrl+v", true), ("f24", true), ("numpad5", true),
        ("oemminus", true), ("volup", true), ("playpause", true), ("0", true), ("9", true), ("ctrl+ctrl", true),

        // things that must be rejected
        ("a, b", false), ("a,b", false), ("none", false), ("keycode", false), ("modifiers", false), ("lbutton", false),
        ("65", false), ("ctrl+65", false), ("-1", false), ("ctrl+", false), ("+c", false), ("ctrl++c", false),
        ("foo bar", false), ("down", true), ("down nope", false), ("up nope", false),
        ("wait", false), ("wait -1", false), ("wait -5", false), ("wait 60001", false), ("wait 1.5", false), ("wait abc", false),
        ("type", false), ("type    ", false), ("run", false), ("run \"\"", false), ("run \"unterminated", false),
        ("dpi", false), ("dpi abc", false), ("dpi -1", false), ("dpi 99", false), ("dpi 20001", false), ("dpi 800 extra", false),
        ("dpi upp", false),
    };

    public static List<string> Run()
    {
        var errors = CommandCatalog.Validate();
        foreach (var (macro, ok) in ParserCases)
        {
            bool valid = Macro.Validate(macro) == null;
            if (valid != ok) errors.Add($"parser: \"{macro.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}\" expected {(ok ? "valid" : "error")}, got {(valid ? "valid" : Macro.Validate(macro))}");
        }
        return errors;
    }

    public static int CaseCount => ParserCases.Length;
}
