using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DeathAdderTool;

/// <summary>
/// Minimal Razer HID control channel (the 90-byte feature report protocol, same as OpenRazer).
/// </summary>
public sealed class RazerHid : IDisposable
{
    public const ushort RazerVid = 0x1532;
    // DeathAdder V2 Pro: 0x007C wired, 0x007D wireless (HyperSpeed dongle)
    public static readonly ushort[] SupportedPids = { 0x007C, 0x007D };

    public const int ReportLen = 90;

    readonly SafeFileHandle _h;
    public string Path { get; }
    public ushort Pid { get; }
    public byte TransactionId { get; set; } = 0x3F;
    readonly object _lock = new();

    RazerHid(SafeFileHandle h, string path, ushort pid) { _h = h; Path = path; Pid = pid; }

    public bool IsWireless => Pid == 0x007D;

    public record HidInfo(string Path, ushort Vid, ushort Pid, ushort UsagePage, ushort Usage, int FeatureLen);

    public static List<HidInfo> Enumerate(ushort vid, ushort[]? pidFilter = null)
    {
        var list = new List<HidInfo>();
        HidD_GetHidGuid(out var guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return list;
        try
        {
            var ifd = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifd); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref ifd, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref ifd, buf, need, out _, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(buf + 4)!;
                    // Only open devices whose path says they're from this vendor; never touch other HID devices (keyboards etc.).
                    if (!path.Contains($"vid_{vid:x4}", StringComparison.OrdinalIgnoreCase)) continue;
                    if (pidFilter != null && !pidFilter.Any(p => path.Contains($"pid_{p:x4}", StringComparison.OrdinalIgnoreCase))) continue;
                    using var h = Open(path);
                    if (h.IsInvalid) continue;
                    var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(h, ref attr) || attr.VendorID != vid) continue;
                    ushort up = 0, u = 0; int flen = 0;
                    if (HidD_GetPreparsedData(h, out IntPtr pp))
                    {
                        if (HidP_GetCaps(pp, out var caps) == HIDP_STATUS_SUCCESS)
                        { up = caps.UsagePage; u = caps.Usage; flen = caps.FeatureReportByteLength; }
                        HidD_FreePreparsedData(pp);
                    }
                    list.Add(new HidInfo(path, attr.VendorID, attr.ProductID, up, u, flen));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    static SafeFileHandle Open(string path) =>
        CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>Finds the DeathAdder V2 Pro control interface (the one with 91-byte feature reports that answers).</summary>
    public static RazerHid? FindMouse()
    {
        foreach (var d in Enumerate(RazerVid, SupportedPids))
        {
            if (Array.IndexOf(SupportedPids, d.Pid) < 0 || d.FeatureLen != ReportLen + 1) continue;
            var h = Open(d.Path);
            if (h.IsInvalid) continue;
            var dev = new RazerHid(h, d.Path, d.Pid);
            foreach (byte tid in new byte[] { 0x3F, 0x1F, 0xFF })
            {
                dev.TransactionId = tid;
                try { if (dev.GetFirmware() != null) return dev; } catch { }
            }
            dev.Dispose();
        }
        return null;
    }

    // ---------- core transport ----------

    public const byte StatusNew = 0x00, StatusBusy = 0x01, StatusOk = 0x02, StatusFail = 0x03, StatusTimeout = 0x04, StatusUnsupported = 0x05;

    /// <summary>Sends a command and returns the 90-byte response (null if the device didn't answer OK).</summary>
    public byte[]? Send(byte cmdClass, byte cmdId, byte dataSize, params byte[] args)
    {
        lock (_lock)
        {
            // The tray process and the settings-window process both talk to the mouse; a set+get pair must not interleave.
            var mx = HidMutex.Value;
            bool owned = false;
            try { owned = mx?.WaitOne(2000) ?? false; } catch (AbandonedMutexException) { owned = true; }
            try { return SendLocked(cmdClass, cmdId, dataSize, args); }
            finally { if (owned) mx!.ReleaseMutex(); }
        }
    }

    // Null if it can't be opened (e.g. another instance runs elevated): we then just proceed without cross-process locking.
    static readonly Lazy<Mutex?> HidMutex = new(() =>
    {
        try { return new Mutex(false, @"Local\DeathAdderTool.Hid"); }
        catch (Exception) { return null; }
    });

    byte[]? SendLocked(byte cmdClass, byte cmdId, byte dataSize, byte[] args)
    {
        {
            var rep = new byte[ReportLen + 1]; // [0] = report id 0
            rep[2] = TransactionId;
            rep[6] = dataSize;
            rep[7] = cmdClass;
            rep[8] = cmdId;
            Array.Copy(args, 0, rep, 9, Math.Min(args.Length, 80));
            byte crc = 0;
            for (int i = 3; i < 89; i++) crc ^= rep[i]; // bytes 2..87 of the report
            rep[89] = crc;

            if (!HidD_SetFeature(_h, rep, rep.Length)) throw new Win32Exception();

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Thread.Sleep(IsWireless ? 15 : 5);
                var resp = new byte[ReportLen + 1];
                if (!HidD_GetFeature(_h, resp, resp.Length)) throw new Win32Exception();
                byte status = resp[1];
                if (status == StatusBusy || status == StatusNew) continue; // not processed yet
                if (status == StatusOk)
                {
                    // An OK report for a different command is the previous response still sitting there: keep polling.
                    if (resp[2] != TransactionId || resp[7] != cmdClass || resp[8] != cmdId) continue;
                    var r = new byte[ReportLen];
                    Array.Copy(resp, 1, r, 0, ReportLen);
                    return r;
                }
                LastStatus = status;
                return null;
            }
            LastStatus = StatusTimeout;
            return null;
        }
    }

    public byte LastStatus { get; private set; }

    // ---------- commands ----------

    public string? GetFirmware()
    {
        var r = Send(0x00, 0x81, 0x02);
        return r == null ? null : $"v{r[8]}.{r[9]}";
    }

    public const byte VarStore = 0x01, NoStore = 0x00;

    public (int x, int y)? GetDpi()
    {
        var r = Send(0x04, 0x85, 0x07, VarStore);
        if (r == null) return null;
        return ((r[9] << 8) | r[10], (r[11] << 8) | r[12]);
    }

    public bool SetDpi(int x, int y)
    {
        x = Math.Clamp(x, 100, 20000); y = Math.Clamp(y, 100, 20000);
        return Send(0x04, 0x05, 0x07, VarStore, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0, 0) != null;
    }

    /// <summary>Battery percent (wireless only), or null.</summary>
    public int? GetBattery()
    {
        var r = Send(0x07, 0x80, 0x02);
        return r == null ? null : (int)Math.Round(r[9] / 255.0 * 100);
    }

    // ---------- button mapping (class 0x02, set 0x0C / get 0x8C) ----------
    // args: [profile, button, hypershift, action, paramLen, params...]
    // Stock table read from this mouse: 0x01-0x05 mouse buttons, 0x09/0x0A wheel, 0x0B DPI up, 0x0C DPI down, 0x0E bottom button.

    public const byte ButtonDpiUp = 0x0B, ButtonDpiDown = 0x0C;
    const byte ActKeyboard = 0x02, ActDpi = 0x06;
    // 0x00 = live/direct table, 0x01 = stored profile (what survives power-off). We write both.
    static readonly byte[] Profiles = { 0x00, 0x01 };

    public byte[]? GetButton(byte profile, byte button) => Send(0x02, 0x8C, 0x0A, profile, button, 0x00);

    bool SetButton(byte button, byte action, params byte[] p)
    {
        foreach (var prof in Profiles)
        {
            var args = new byte[5 + p.Length];
            args[0] = prof; args[1] = button; args[2] = 0x00; args[3] = action; args[4] = (byte)p.Length;
            p.CopyTo(args, 5);
            // Skip the write if the mouse already has this mapping (avoids needless flash writes on every launch).
            var cur = GetButton(prof, button);
            if (cur != null && cur.AsSpan(10, 3 + p.Length).SequenceEqual(args.AsSpan(2, 3 + p.Length))) continue;
            if (Send(0x02, 0x0C, 0x0A, args) == null) return false;
        }
        return true;
    }

    /// <summary>Make a button send a keyboard key (HID usage, e.g. 0x72 = F23).</summary>
    public bool MapButtonToKey(byte button, byte hidUsage, byte modifiers = 0) =>
        SetButton(button, ActKeyboard, modifiers, hidUsage);

    /// <summary>Restore the factory DPI up / DPI down behaviour.</summary>
    public bool RestoreDpiButtons() =>
        SetButton(ButtonDpiUp, ActDpi, 0x01) && SetButton(ButtonDpiDown, ActDpi, 0x02);

    public void Dispose() => _h.Dispose();

    // ---------- P/Invoke ----------

    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int need, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
}
