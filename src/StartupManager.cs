using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WallpaperScheduler
{
    static class StartupManager
    {
        const string RunValueName = "WallpaperScheduler";
        const string TaskName = "WallpaperScheduler";

        public static void SetStartWithWindows(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null)
                    throw new InvalidOperationException("Windows startup settings are not available.");
                if (enabled)
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --background");
                else
                    key.DeleteValue(RunValueName, false);
            }
        }

        public static void EnableTask()
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "WallpaperScheduler-task.xml");
            File.WriteAllText(xmlPath, BuildXml(Application.ExecutablePath), Encoding.Unicode);
            try
            {
                Run("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F", false);
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        public static void DisableTask()
        {
            Run("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", true);
        }

        static string BuildXml(string exePath)
        {
            string start = DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss");
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.3\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo>\r\n" +
                "    <Description>Applies wallpapers scheduled in Wallpaper Scheduler.</Description>\r\n" +
                "  </RegistrationInfo>\r\n" +
                "  <Triggers>\r\n" +
                "    <TimeTrigger>\r\n" +
                "      <StartBoundary>" + start + "</StartBoundary>\r\n" +
                "      <Enabled>true</Enabled>\r\n" +
                "      <Repetition>\r\n" +
                "        <Interval>PT1M</Interval>\r\n" +
                "        <Duration>P9999D</Duration>\r\n" +
                "        <StopAtDurationEnd>false</StopAtDurationEnd>\r\n" +
                "      </Repetition>\r\n" +
                "    </TimeTrigger>\r\n" +
                "    <LogonTrigger>\r\n" +
                "      <Enabled>true</Enabled>\r\n" +
                "    </LogonTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <WakeToRun>false</WakeToRun>\r\n" +
                "    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>" + Xml(exePath) + "</Command>\r\n" +
                "      <Arguments>--apply</Arguments>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>\r\n";
        }

        static string Xml(string value)
        {
            return (value ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        static void Run(string fileName, string arguments, bool ignoreErrors)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, fileName),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null)
                    throw new InvalidOperationException("Couldn't start " + fileName + ".");
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(20000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException(fileName + " did not finish.");
                }
                if (process.ExitCode != 0 && !ignoreErrors)
                {
                    string detail = (error + " " + output).Trim();
                    if (detail.Length == 0)
                        detail = "Exit code " + process.ExitCode + ".";
                    throw new InvalidOperationException(detail);
                }
            }
        }
    }
}
