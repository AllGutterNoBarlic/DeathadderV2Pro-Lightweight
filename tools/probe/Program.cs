using DeathAdderTool;

foreach (var d in RazerHid.Enumerate(RazerHid.RazerVid))
    Console.WriteLine($"PID {d.Pid:X4} page {d.UsagePage:X4} usage {d.Usage:X4} feat {d.FeatureLen}  {d.Path}");

using var m = RazerHid.FindMouse();
if (m == null) { Console.WriteLine("No responding mouse interface"); return; }
Console.WriteLine($"Found: {m.Path}\n tid=0x{m.TransactionId:X2} fw={m.GetFirmware()} dpi={m.GetDpi()} batt={m.GetBattery()}");

// raw: probe <class> <id> <size> [args...]  (hex)
if (args.Length >= 3)
{
    var a = args.Select(s => Convert.ToByte(s, 16)).ToArray();
    var r = m.Send(a[0], a[1], a[2], a.Skip(3).ToArray());
    Console.WriteLine(r == null ? $"status {m.LastStatus:X2}" : BitConverter.ToString(r, 0, 24));
}

// scan: read button mapping table (read-only)
if (args.Length == 1 && args[0] == "scan")
{
    foreach (byte prof in new byte[] { 0x00, 0x01 })
    for (int b = 0; b < 0x80; b++)
    {
        var r = m.Send(0x02, 0x8C, 0x0A, prof, (byte)b, 0x00);
        if (r != null) Console.WriteLine($"prof {prof:X2} btn {b:X2}: " + BitConverter.ToString(r, 8, 12));
        else if (b < 3) Console.WriteLine($"prof {prof:X2} btn {b:X2}: status {m.LastStatus:X2}");
    }
}
