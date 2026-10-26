using System;
using System.IO;
using System.Text;

namespace WallpaperScheduler
{
    static class AppPaths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WallpaperScheduler");

        public static readonly string Schedules = Path.Combine(Root, "schedules.json");
        public static readonly string State = Path.Combine(Root, "state.json");
        public static readonly string Settings = Path.Combine(Root, "settings.json");
        public static readonly string Log = Path.Combine(Root, "log.txt");
    }

    static class AppLog
    {
        static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(AppPaths.Log, line, Encoding.UTF8);
                    var info = new FileInfo(AppPaths.Log);
                    if (info.Length > 180000)
                    {
                        var text = File.ReadAllText(AppPaths.Log, Encoding.UTF8);
                        if (text.Length > 90000)
                            text = text.Substring(text.Length - 90000);
                        File.WriteAllText(AppPaths.Log, text, Encoding.UTF8);
                    }
                }
            }
            catch { }
        }
    }
}
