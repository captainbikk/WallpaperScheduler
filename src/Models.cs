using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WallpaperScheduler
{
    sealed class ScheduleItem
    {
        public const string AllMonitors = "*";

        public string Id { get; set; }
        public string Name { get; set; }
        public string ImagePath { get; set; }
        public string MonitorId { get; set; }
        public string MonitorKey { get; set; }
        public string MonitorLabel { get; set; }
        public string Start { get; set; }
        public int DurationValue { get; set; }
        public string DurationUnit { get; set; }
        public bool RepeatYearly { get; set; }
        public string Fit { get; set; }
        public bool Enabled { get; set; }

        public int DurationMinutes
        {
            get
            {
                int n = DurationValue < 1 ? 1 : DurationValue;
                switch ((DurationUnit ?? "hours").Trim().ToLowerInvariant())
                {
                    case "minute":
                    case "minutes": return n;
                    case "hour":
                    case "hours": return n * 60;
                    case "day":
                    case "days": return n * 1440;
                    case "week":
                    case "weeks": return n * 10080;
                    default: return n * 60;
                }
            }
        }

        public string DurationText
        {
            get
            {
                int n = DurationValue < 1 ? 1 : DurationValue;
                string unit = (DurationUnit ?? "hours").Trim().ToLowerInvariant();
                if (unit != "minutes" && unit != "hours" && unit != "days" && unit != "weeks")
                    unit = "hours";
                if (n == 1 && unit.EndsWith("s", StringComparison.Ordinal))
                    unit = unit.Substring(0, unit.Length - 1);
                return n + " " + unit;
            }
        }

        public bool TryGetStart(out DateTime start)
        {
            return DateTime.TryParseExact(
                Start ?? "",
                "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out start);
        }

        public ScheduleItem Clone()
        {
            return new ScheduleItem
            {
                Id = Id,
                Name = Name,
                ImagePath = ImagePath,
                MonitorId = MonitorId,
                MonitorKey = MonitorKey,
                MonitorLabel = MonitorLabel,
                Start = Start,
                DurationValue = DurationValue,
                DurationUnit = DurationUnit,
                RepeatYearly = RepeatYearly,
                Fit = Fit,
                Enabled = Enabled
            };
        }
    }

    sealed class ScheduleFile
    {
        public int Version { get; set; }
        public List<ScheduleItem> Items { get; set; }
    }

    sealed class MonitorState
    {
        public string DevicePath { get; set; }
        public string MonitorKey { get; set; }
        public string OriginalWallpaper { get; set; }
        public string AppliedScheduleId { get; set; }
        public string AppliedOccurrence { get; set; }
        public string AppliedImage { get; set; }
        public string AppliedReadback { get; set; }
        public bool OriginalSaved { get; set; }
    }

    sealed class AppState
    {
        public bool FitSaved { get; set; }
        public int SavedFit { get; set; }
        public List<MonitorState> Monitors { get; set; }
        public List<string> LoggedProblems { get; set; }
    }

    sealed class AppSettings
    {
        public bool StartWithWindows { get; set; }
        public bool BackgroundTask { get; set; }
        public bool CloseToTray { get; set; }
        public bool BalloonShown { get; set; }
        public bool ExitNoteShown { get; set; }
        public bool Initialized { get; set; }
        public string DefaultImagePath { get; set; }
        public string DefaultFit { get; set; }
    }

    static class Store
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static List<ScheduleItem> LoadSchedules()
        {
            var file = Load<ScheduleFile>(AppPaths.Schedules);
            if (file.Items == null)
                file.Items = new List<ScheduleItem>();
            var clean = new List<ScheduleItem>();
            foreach (var item in file.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id))
                    continue;
                if (string.IsNullOrWhiteSpace(item.MonitorId))
                    item.MonitorId = ScheduleItem.AllMonitors;
                if (string.IsNullOrWhiteSpace(item.Fit))
                    item.Fit = "fill";
                if (string.IsNullOrWhiteSpace(item.DurationUnit))
                    item.DurationUnit = "days";
                clean.Add(item);
            }
            return clean;
        }

        public static void SaveSchedules(List<ScheduleItem> items)
        {
            var file = new ScheduleFile { Version = 1, Items = items ?? new List<ScheduleItem>() };
            AtomicWrite(AppPaths.Schedules, Json.Serialize(file));
        }

        public static AppState LoadState()
        {
            var state = Load<AppState>(AppPaths.State);
            if (state.Monitors == null) state.Monitors = new List<MonitorState>();
            if (state.LoggedProblems == null) state.LoggedProblems = new List<string>();
            return state;
        }

        public static void SaveState(AppState state)
        {
            if (state.Monitors == null) state.Monitors = new List<MonitorState>();
            if (state.LoggedProblems == null) state.LoggedProblems = new List<string>();
            AtomicWrite(AppPaths.State, Json.Serialize(state));
        }

        public static AppSettings LoadSettings()
        {
            if (!File.Exists(AppPaths.Settings))
            {
                return new AppSettings
                {
                    StartWithWindows = true,
                    BackgroundTask = true,
                    CloseToTray = true,
                    Initialized = false
                };
            }
            var settings = Load<AppSettings>(AppPaths.Settings);
            settings.Initialized = true;
            return settings;
        }

        public static void SaveSettings(AppSettings settings)
        {
            settings.Initialized = true;
            AtomicWrite(AppPaths.Settings, Json.Serialize(settings));
        }

        static T Load<T>(string path) where T : class, new()
        {
            try
            {
                if (!File.Exists(path))
                    return new T();
                var text = File.ReadAllText(path, Encoding.UTF8);
                var obj = Json.Deserialize<T>(text);
                return obj ?? new T();
            }
            catch (Exception ex)
            {
                AppLog.Write("Couldn't read " + Path.GetFileName(path) + ": " + ex.Message);
                return new T();
            }
        }

        static void AtomicWrite(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents, new UTF8Encoding(false));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(tmp, path, null);
                    else
                        File.Move(tmp, path);
                    return;
                }
                catch (IOException)
                {
                    if (attempt == 2) throw;
                    System.Threading.Thread.Sleep(40);
                }
            }
        }
    }

    static class ScheduleMath
    {
        public static DateTime Occurrence(DateTime template, int year)
        {
            int day = template.Day;
            int days = DateTime.DaysInMonth(year, template.Month);
            if (day > days) day = days;
            return new DateTime(year, template.Month, day, template.Hour, template.Minute, 0, DateTimeKind.Local);
        }

        public static bool TryActive(ScheduleItem item, DateTime now, out DateTime start, out DateTime end)
        {
            start = default(DateTime);
            end = default(DateTime);
            if (item == null || !item.Enabled)
                return false;
            DateTime template;
            if (!item.TryGetStart(out template))
                return false;

            if (!item.RepeatYearly)
            {
                start = template;
                if (!TryAddMinutes(start, item.DurationMinutes, out end))
                    return false;
                return now >= start && now < end;
            }

            bool found = false;
            foreach (var year in new[] { now.Year - 1, now.Year })
            {
                var candidate = Occurrence(template, year);
                DateTime candidateEnd;
                if (!TryAddMinutes(candidate, item.DurationMinutes, out candidateEnd))
                    continue;
                if (now >= candidate && now < candidateEnd && (!found || candidate > start))
                {
                    start = candidate;
                    end = candidateEnd;
                    found = true;
                }
            }
            return found;
        }

        public static bool TryNextFuture(ScheduleItem item, DateTime now, out DateTime start)
        {
            start = default(DateTime);
            DateTime template;
            if (item == null || !item.Enabled || !item.TryGetStart(out template))
                return false;

            if (!item.RepeatYearly)
            {
                if (template > now)
                {
                    start = template;
                    return true;
                }
                return false;
            }

            bool found = false;
            foreach (var year in new[] { now.Year - 1, now.Year, now.Year + 1 })
            {
                var candidate = Occurrence(template, year);
                if (candidate > now && (!found || candidate < start))
                {
                    start = candidate;
                    found = true;
                }
            }
            return found;
        }

        public static bool TryNextOrCurrent(ScheduleItem item, DateTime now, out DateTime start)
        {
            DateTime activeStart;
            DateTime activeEnd;
            if (TryActive(item, now, out activeStart, out activeEnd))
            {
                start = activeStart;
                return true;
            }
            return TryNextFuture(item, now, out start);
        }

        public static DateTime LastEnd(ScheduleItem item, DateTime now)
        {
            DateTime template;
            if (item == null || !item.TryGetStart(out template))
                return now;
            if (!item.RepeatYearly)
            {
                DateTime end;
                return TryAddMinutes(template, item.DurationMinutes, out end) ? end : template;
            }

            DateTime best = DateTime.MinValue;
            foreach (var year in new[] { now.Year - 1, now.Year, now.Year + 1 })
            {
                var candidate = Occurrence(template, year);
                DateTime end;
                if (!TryAddMinutes(candidate, item.DurationMinutes, out end))
                    continue;
                if (end <= now && end > best)
                    best = end;
            }
            if (best == DateTime.MinValue)
            {
                DateTime end;
                var candidate = Occurrence(template, now.Year);
                return TryAddMinutes(candidate, item.DurationMinutes, out end) ? end : candidate;
            }
            return best;
        }

        public static string Describe(ScheduleItem item, DateTime now)
        {
            DateTime template;
            if (item == null || !item.TryGetStart(out template))
                return "Choose a start date.";

            DateTime start;
            if (!TryNextOrCurrent(item, now, out start))
                start = item.RepeatYearly ? Occurrence(template, now.Year) : template;
            DateTime end;
            if (!TryAddMinutes(start, item.DurationMinutes, out end))
                return "That length of time is too long.";

            string when;
            if (item.RepeatYearly)
            {
                when = "Every year on " + template.ToString("MMMM d") + " at " + template.ToString("h:mm tt");
                if (template.Month == 2 && template.Day == 29)
                    when += " (February 28 in other years)";
            }
            else
            {
                when = "From " + start.ToString("dddd, MMMM d, yyyy") + " at " + start.ToString("h:mm tt");
            }

            string where = item.MonitorId == ScheduleItem.AllMonitors
                ? "every monitor"
                : (string.IsNullOrWhiteSpace(item.MonitorLabel) ? "the chosen monitor" : item.MonitorLabel);

            return when + ", " + item.DurationText + " on " + where + ". It comes down "
                + end.ToString("dddd, MMMM d, yyyy") + " at " + end.ToString("h:mm tt") + ".";
        }

        public static MonitorInfo FindMonitor(ScheduleItem item, IList<MonitorInfo> monitors)
        {
            if (item == null || monitors == null)
                return null;
            if (item.MonitorId == ScheduleItem.AllMonitors)
                return null;

            MonitorInfo byId = null;
            MonitorInfo byKey = null;
            int keyCount = 0;
            foreach (var monitor in monitors)
            {
                if (!string.IsNullOrEmpty(item.MonitorId)
                    && string.Equals(monitor.DevicePath, item.MonitorId, StringComparison.OrdinalIgnoreCase))
                    byId = monitor;
                if (!string.IsNullOrEmpty(item.MonitorKey)
                    && item.MonitorKey != ScheduleItem.AllMonitors
                    && string.Equals(monitor.Key, item.MonitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    byKey = monitor;
                    keyCount++;
                }
            }
            if (byId != null)
                return byId;
            if (keyCount == 1)
                return byKey;
            return null;
        }

        public static List<MonitorInfo> Targets(ScheduleItem item, IList<MonitorInfo> monitors)
        {
            var list = new List<MonitorInfo>();
            if (monitors == null)
                return list;
            if (item.MonitorId == ScheduleItem.AllMonitors || item.MonitorKey == ScheduleItem.AllMonitors)
            {
                list.AddRange(monitors);
                return list;
            }
            var one = FindMonitor(item, monitors);
            if (one != null)
                list.Add(one);
            return list;
        }

        public static void RunTests(List<string> errors)
        {
            var one = new ScheduleItem
            {
                Enabled = true,
                Start = "2026-10-25T10:00:00",
                DurationValue = 2,
                DurationUnit = "hours",
                RepeatYearly = false
            };
            Check(errors, "2 hours is 120 minutes", one.DurationMinutes == 120);
            one.DurationValue = 1;
            one.DurationUnit = "days";
            Check(errors, "1 day is 1440 minutes", one.DurationMinutes == 1440);
            one.DurationValue = 2;
            one.DurationUnit = "weeks";
            Check(errors, "2 weeks is 20160 minutes", one.DurationMinutes == 20160);
            Check(errors, "duration text singular", new ScheduleItem { DurationValue = 1, DurationUnit = "days" }.DurationText == "1 day");

            one.DurationValue = 2;
            one.DurationUnit = "hours";
            DateTime start, end;
            var now = new DateTime(2026, 10, 25, 11, 0, 0);
            Check(errors, "one-shot is active inside the window", TryActive(one, now, out start, out end) && start.Hour == 10);
            Check(errors, "one-shot is inactive before the start", !TryActive(one, new DateTime(2026, 10, 25, 9, 59, 0), out start, out end));
            Check(errors, "one-shot ends exactly at the end time", !TryActive(one, new DateTime(2026, 10, 25, 12, 0, 0), out start, out end));

            var yearly = new ScheduleItem
            {
                Enabled = true,
                Start = "2020-10-31T18:00:00",
                DurationValue = 8,
                DurationUnit = "hours",
                RepeatYearly = true
            };
            Check(errors, "yearly anniversary is active", TryActive(yearly, new DateTime(2026, 10, 31, 20, 0, 0), out start, out end));
            Check(errors, "yearly is inactive the day before", !TryActive(yearly, new DateTime(2026, 10, 30, 20, 0, 0), out start, out end));

            var holiday = new ScheduleItem
            {
                Enabled = true,
                Start = "2019-12-31T18:00:00",
                DurationValue = 2,
                DurationUnit = "days",
                RepeatYearly = true
            };
            Check(errors, "yearly window can cross New Year", TryActive(holiday, new DateTime(2026, 1, 1, 1, 0, 0), out start, out end) && start.Year == 2025);

            var leap = new ScheduleItem
            {
                Enabled = true,
                Start = "2024-02-29T09:00:00",
                DurationValue = 1,
                DurationUnit = "days",
                RepeatYearly = true
            };
            Check(errors, "Feb 29 uses Feb 28 in other years", TryActive(leap, new DateTime(2025, 2, 28, 10, 0, 0), out start, out end) && start.Day == 28);

            DateTime future;
            Check(errors, "next future yearly is this year's date when it is still ahead",
                TryNextFuture(yearly, new DateTime(2026, 1, 1, 0, 0, 0), out future) && future.Year == 2026 && future.Month == 10);
            Check(errors, "next future yearly rolls to next year after the window",
                TryNextFuture(yearly, new DateTime(2026, 11, 1, 0, 0, 0), out future) && future.Year == 2027);

            one.Enabled = false;
            Check(errors, "disabled schedules are inactive", !TryActive(one, now, out start, out end));
        }

        static void Check(List<string> errors, string name, bool ok)
        {
            if (!ok) errors.Add(name);
        }

        static bool TryAddMinutes(DateTime start, int minutes, out DateTime end)
        {
            try
            {
                end = start.AddMinutes(minutes);
                return true;
            }
            catch
            {
                end = start;
                return false;
            }
        }
    }
}
