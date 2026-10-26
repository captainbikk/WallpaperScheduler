using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WallpaperScheduler
{
    sealed class MonitorInfo
    {
        public string DevicePath { get; set; }
        public string Key { get; set; }
        public string Model { get; set; }
        public string Placement { get; set; }
        public string Resolution { get; set; }
        public bool Primary { get; set; }
        public bool Portrait { get; set; }
        public Rectangle Bounds { get; set; }
        public string CurrentWallpaper { get; set; }

        public string DetailLine
        {
            get { return Placement + " · " + Resolution + (Portrait ? " portrait" : ""); }
        }

        public string ChoiceLabel
        {
            get { return Model + " — " + Placement + ", " + Resolution + (Portrait ? " portrait" : ""); }
        }
    }

    static class WallpaperApi
    {
        const int SOk = 0;

        public static List<MonitorInfo> GetMonitors()
        {
            var names = TryLoadEdidNames();
            var screenNames = TryLoadScreenNames();
            var raw = new List<MonitorInfo>();
            var com = Create();
            try
            {
                uint count;
                Check(com.GetMonitorDevicePathCount(out count), "Reading the monitor list");
                for (uint i = 0; i < count; i++)
                {
                    IntPtr pathPtr;
                    if (com.GetMonitorDevicePathAt(i, out pathPtr) < SOk)
                        continue;
                    string path = TakeString(pathPtr);
                    RECT rect;
                    if (com.GetMonitorRECT(path, out rect) < SOk)
                        continue;
                    var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                        continue;

                    string wall = "";
                    IntPtr wallPtr;
                    if (com.GetWallpaper(path, out wallPtr) >= SOk)
                        wall = TakeString(wallPtr);

                    raw.Add(Describe(path, bounds, wall, names, screenNames));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(com);
            }

            if (raw.Count == 0)
                throw new InvalidOperationException("Windows did not report any monitors.");

            var screens = Screen.AllScreens;
            int matches = 0;
            foreach (var monitor in raw)
            {
                if (MatchingScreen(screens, monitor.Bounds) != null)
                    matches++;
            }
            var accepted = new List<MonitorInfo>();
            foreach (var monitor in raw)
            {
                var screen = MatchingScreen(screens, monitor.Bounds);
                if (matches > 0 && screen == null)
                    continue;
                monitor.Primary = screen != null && screen.Primary;
                accepted.Add(monitor);
            }
            if (accepted.Count == 0)
                accepted = raw;

            if (!AnyPrimary(accepted))
            {
                MonitorInfo fallback = accepted[0];
                foreach (var monitor in accepted)
                {
                    if (monitor.Bounds.Contains(0, 0))
                    {
                        fallback = monitor;
                        break;
                    }
                }
                fallback.Primary = true;
            }

            Rectangle primaryBounds = accepted[0].Bounds;
            foreach (var monitor in accepted)
            {
                if (monitor.Primary)
                    primaryBounds = monitor.Bounds;
            }
            foreach (var monitor in accepted)
                monitor.Placement = monitor.Primary ? "Primary" : Place(monitor.Bounds, primaryBounds);

            accepted.Sort(delegate (MonitorInfo a, MonitorInfo b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                int left = a.Bounds.Left.CompareTo(b.Bounds.Left);
                if (left != 0) return left;
                return a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            return accepted;
        }

        public static string GetWallpaper(string devicePath)
        {
            var com = Create();
            try
            {
                IntPtr ptr;
                Check(com.GetWallpaper(devicePath, out ptr), "Reading a wallpaper");
                return TakeString(ptr);
            }
            finally { Marshal.ReleaseComObject(com); }
        }

        public static void SetWallpaper(string devicePath, string imagePath)
        {
            var com = Create();
            try { Check(com.SetWallpaper(devicePath, imagePath), "Setting a wallpaper"); }
            finally { Marshal.ReleaseComObject(com); }
        }

        public static int GetPosition()
        {
            var com = Create();
            try
            {
                int position;
                Check(com.GetPosition(out position), "Reading the wallpaper style");
                return position;
            }
            finally { Marshal.ReleaseComObject(com); }
        }

        public static void SetPosition(int position)
        {
            var com = Create();
            try { Check(com.SetPosition(position), "Setting the wallpaper style"); }
            finally { Marshal.ReleaseComObject(com); }
        }

        static IDesktopWallpaper Create()
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            if (type == null)
                throw new InvalidOperationException("Windows desktop wallpaper support is not available on this PC.");
            return (IDesktopWallpaper)Activator.CreateInstance(type);
        }

        static void Check(int hr, string action)
        {
            if (hr < 0)
                throw new InvalidOperationException(action + " failed (0x" + hr.ToString("X8") + ").");
        }

        static string TakeString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return "";
            try { return Marshal.PtrToStringUni(ptr) ?? ""; }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }

        static Screen MatchingScreen(Screen[] screens, Rectangle bounds)
        {
            if (screens == null)
                return null;
            foreach (var screen in screens)
            {
                var b = screen.Bounds;
                if (b.Left == bounds.Left && b.Top == bounds.Top && b.Width == bounds.Width && b.Height == bounds.Height)
                    return screen;
            }
            return null;
        }

        static bool AnyPrimary(List<MonitorInfo> monitors)
        {
            foreach (var monitor in monitors)
                if (monitor.Primary) return true;
            return false;
        }

        static string Place(Rectangle bounds, Rectangle primary)
        {
            int dx = Center(bounds.Left, bounds.Width) - Center(primary.Left, primary.Width);
            int dy = Center(bounds.Top, bounds.Height) - Center(primary.Top, primary.Height);
            if (Math.Abs(dx) < 80 && Math.Abs(dy) < 80)
                return "Same area";
            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx < 0 ? "Left" : "Right";
            return dy < 0 ? "Above" : "Below";
        }

        static int Center(int origin, int size)
        {
            return origin + size / 2;
        }

        static MonitorInfo Describe(string path, Rectangle bounds, string wallpaper, Dictionary<string, string> edid, Dictionary<string, string> screenNames)
        {
            string token = TokenFromPath(path);
            string model = "";
            if (!string.IsNullOrEmpty(token) && edid != null && edid.ContainsKey(token))
                model = edid[token];
            if (string.IsNullOrWhiteSpace(model))
            {
                string screenKey = bounds.Left + "," + bounds.Top + "," + bounds.Width + "," + bounds.Height;
                if (screenNames != null && screenNames.ContainsKey(screenKey))
                    model = screenNames[screenKey];
            }
            if (string.IsNullOrWhiteSpace(model))
                model = "Monitor";

            return new MonitorInfo
            {
                DevicePath = path,
                Key = StableKey(path, bounds),
                Model = model.Trim(),
                Resolution = bounds.Width + "×" + bounds.Height,
                Portrait = bounds.Height > bounds.Width,
                Bounds = bounds,
                CurrentWallpaper = wallpaper ?? ""
            };
        }

        static string TokenFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            int marker = path.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return "";
            int start = marker + "DISPLAY#".Length;
            int end = path.IndexOf('#', start);
            if (end < 0)
                return "";
            return path.Substring(start, end - start).ToUpperInvariant();
        }

        static string StableKey(string path, Rectangle bounds)
        {
            string token = TokenFromPath(path);
            var uid = Regex.Match(path ?? "", @"UID[0-9A-Fa-f]+", RegexOptions.IgnoreCase);
            if (token.Length == 0 && !uid.Success)
                return bounds.Width + "x" + bounds.Height + "@" + bounds.Left + "," + bounds.Top;
            return token + "|" + (uid.Success ? uid.Value.ToUpperInvariant() : "");
        }

        static Dictionary<string, string> edidCache;

        public static void ForgetMonitorNames()
        {
            edidCache = null;
        }

        static Dictionary<string, string> TryLoadEdidNames()
        {
            if (edidCache != null)
                return edidCache;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM WmiMonitorID"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject monitor in results)
                    {
                        using (monitor)
                        {
                            string instance = Convert.ToString(monitor["InstanceName"]) ?? "";
                            string token = TokenFromInstance(instance);
                            if (token.Length == 0 || map.ContainsKey(token))
                                continue;
                            string friendly = Clean(Decode(monitor["UserFriendlyName"]));
                            string brand = Brand(Clean(Decode(monitor["ManufacturerName"])));
                            string model = friendly;
                            if (!string.IsNullOrWhiteSpace(brand)
                                && (string.IsNullOrWhiteSpace(model) || model.IndexOf(brand, StringComparison.OrdinalIgnoreCase) < 0))
                            {
                                model = string.IsNullOrWhiteSpace(model) ? brand : brand + " " + model;
                            }
                            if (!string.IsNullOrWhiteSpace(model))
                                map[token] = model;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Monitor names were not available: " + ex.Message);
            }
            edidCache = map;
            return map;
        }

        static string TokenFromInstance(string instance)
        {
            int slash = instance.IndexOf('\\');
            if (slash < 0 || slash + 1 >= instance.Length)
                return "";
            string rest = instance.Substring(slash + 1);
            int next = rest.IndexOf('\\');
            string token = next >= 0 ? rest.Substring(0, next) : rest;
            return token.ToUpperInvariant();
        }

        static Dictionary<string, string> TryLoadScreenNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    var device = NewDevice();
                    if (!EnumDisplayDevices(screen.DeviceName, 0, ref device, 0))
                        continue;
                    string name = Clean(device.DeviceString);
                    if (name.Length == 0 || name.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    var b = screen.Bounds;
                    map[b.Left + "," + b.Top + "," + b.Width + "," + b.Height] = name;
                }
            }
            catch { }
            return map;
        }

        static DISPLAY_DEVICE NewDevice()
        {
            var device = new DISPLAY_DEVICE();
            device.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            return device;
        }

        static string Decode(object value)
        {
            if (value is ushort[] words)
            {
                var chars = new char[words.Length];
                int n = 0;
                foreach (var word in words)
                {
                    if (word == 0) break;
                    chars[n++] = (char)word;
                }
                return new string(chars, 0, n);
            }
            if (value is byte[] bytes)
            {
                int n = bytes.Length;
                while (n > 0 && (bytes[n - 1] == 0 || bytes[n - 1] == 32)) n--;
                return n == 0 ? "" : System.Text.Encoding.ASCII.GetString(bytes, 0, n);
            }
            return Convert.ToString(value) ?? "";
        }

        static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            return value.Trim().Trim('\0');
        }

        static string Brand(string code)
        {
            switch ((code ?? "").Trim().ToUpperInvariant())
            {
                case "AUS":
                case "ACI": return "ASUS";
                case "IVM": return "Iiyama";
                case "GSM":
                case "LGD": return "LG";
                case "SAM":
                case "SEC":
                case "SDC": return "Samsung";
                case "DEL": return "Dell";
                case "ACR": return "Acer";
                case "LEN": return "Lenovo";
                case "MSI": return "MSI";
                case "AOC": return "AOC";
                case "PHL": return "Philips";
                case "HWP":
                case "HPQ": return "HP";
                case "VSC": return "ViewSonic";
                case "BNQ": return "BenQ";
                case "APP": return "Apple";
                case "ENC": return "Eizo";
                case "SNY": return "Sony";
                case "NEC": return "NEC";
                case "SPT": return "Sceptre";
                case "MEI": return "Panasonic";
                default: return "";
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopWallpaper
        {
            [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out IntPtr wallpaper);
            [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorID);
            [PreserveSig] int GetMonitorDevicePathCount(out uint count);
            [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT rect);
            [PreserveSig] int SetBackgroundColor(uint color);
            [PreserveSig] int GetBackgroundColor(out uint color);
            [PreserveSig] int SetPosition(int position);
            [PreserveSig] int GetPosition(out int position);
        }
    }
}
