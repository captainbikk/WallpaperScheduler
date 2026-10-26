using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WallpaperScheduler
{
    static class Program
    {
        const string GuiMutexName = @"Local\WallpaperScheduler.Gui";
        const string ShowEventName = @"Local\WallpaperScheduler.Show";

        static Mutex guiMutex;

        [STAThread]
        static void Main(string[] args)
        {
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
                flags.Add(arg ?? "");

            if (flags.Contains("--check"))
            {
                Environment.Exit(RunCheck());
                return;
            }

            if (flags.Contains("--apply"))
            {
                if (!GuiIsRunning())
                {
                    try { Engine.Apply(); }
                    catch (Exception ex) { AppLog.Write("Apply failed: " + ex); }
                }
                return;
            }

            guiMutex = new Mutex(false, GuiMutexName);
            bool owned;
            try { owned = guiMutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }

            if (!owned)
            {
                SignalShow();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    AppLog.Write(e.Exception.ToString());
                    try
                    {
                        MessageBox.Show(
                            "Something went wrong:\r\n\r\n" + e.Exception.Message,
                            "Wallpaper Scheduler",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    catch { }
                };
                Application.Run(new MainForm(flags.Contains("--background")));
            }
            finally
            {
                try { guiMutex.ReleaseMutex(); } catch { }
                guiMutex.Dispose();
            }
        }

        public static void SignalShow()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                    ev.Set();
            }
            catch { }
        }

        public static EventWaitHandle CreateShowEvent()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }

        static bool GuiIsRunning()
        {
            Mutex existing = null;
            try { existing = Mutex.OpenExisting(GuiMutexName); }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch { return false; }

            try
            {
                bool acquired;
                try { acquired = existing.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (acquired)
                {
                    existing.ReleaseMutex();
                    return false;
                }
                return true;
            }
            finally { existing.Dispose(); }
        }

        static int RunCheck()
        {
            AttachConsole();
            var errors = new List<string>();
            ScheduleMath.RunTests(errors);
            Engine.RunTests(errors);
            try
            {
                var monitors = WallpaperApi.GetMonitors();
                Console.WriteLine("Monitors: " + monitors.Count);
                foreach (var monitor in monitors)
                {
                    Console.WriteLine("- " + monitor.Model + " | " + monitor.Placement + " | " + monitor.Resolution
                        + (monitor.Portrait ? " portrait" : "") + " | key=" + monitor.Key);
                    Console.WriteLine("  path=" + monitor.DevicePath);
                    Console.WriteLine("  wall=" + monitor.CurrentWallpaper);
                }
                Console.WriteLine("Position=" + WallpaperApi.GetPosition());
            }
            catch (Exception ex)
            {
                errors.Add("Monitor read failed: " + ex);
                Console.WriteLine(ex);
            }

            if (errors.Count == 0)
            {
                Console.WriteLine("CHECK PASS");
                return 0;
            }

            Console.WriteLine("CHECK FAIL " + errors.Count);
            foreach (var error in errors)
                Console.WriteLine("  " + error);
            return 1;
        }

        static void AttachConsole()
        {
            if (!AttachConsoleNative(-1))
                AllocConsole();
            try
            {
                var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(writer);
                var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(error);
            }
            catch { }
            Console.WriteLine();
        }

        [DllImport("kernel32.dll", EntryPoint = "AttachConsole")]
        static extern bool AttachConsoleNative(int processId);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();
    }
}
