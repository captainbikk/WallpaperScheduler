using System;
using System.Collections.Generic;
using System.IO;

namespace WallpaperScheduler
{
    sealed class Assignment
    {
        public ScheduleItem Item { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public MonitorInfo Monitor { get; set; }
    }

    sealed class Plan
    {
        public Dictionary<string, Assignment> Assignment = new Dictionary<string, Assignment>(StringComparer.OrdinalIgnoreCase);
        public List<string> Problems = new List<string>();
        public string Summary = "";
        public bool SchedulesChanged;
    }

    sealed class ApplyResult
    {
        public Plan Plan;
        public List<MonitorInfo> Monitors;
        public bool Changed;
        public List<string> Actions = new List<string>();
    }

    static class Engine
    {
        public static ApplyResult Apply()
        {
            using (var mutex = new System.Threading.Mutex(false, @"Local\WallpaperScheduler.Apply"))
            {
                bool locked = false;
                try
                {
                    try { locked = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
                    catch (System.Threading.AbandonedMutexException) { locked = true; }
                    if (!locked)
                    {
                        return new ApplyResult
                        {
                            Plan = new Plan { Summary = "Another wallpaper update is still running." },
                            Monitors = new List<MonitorInfo>()
                        };
                    }
                    return ApplyCore();
                }
                finally
                {
                    if (locked)
                    {
                        try { mutex.ReleaseMutex(); } catch { }
                    }
                }
            }
        }

        static ApplyResult ApplyCore()
        {
            var now = DateTime.Now;
            var monitors = WallpaperApi.GetMonitors();
            var schedules = Store.LoadSchedules();
            var state = Store.LoadState();
            var settings = Store.LoadSettings();
            var plan = BuildPlan(now, monitors, schedules, File.Exists);
            string idleImage = IdlePicture(settings, File.Exists);
            if (idleImage == null && !string.IsNullOrWhiteSpace(settings.DefaultImagePath))
                plan.Problems.Add("The default wallpaper picture was not found: " + settings.DefaultImagePath.Trim());
            else
                plan.Summary = DescribeIdle(plan.Summary, idleImage != null, plan.Assignment.Count > 0);
            var result = new ApplyResult { Plan = plan, Monitors = monitors };

            int? desiredFit = DesiredFit(plan);
            if (!desiredFit.HasValue && idleImage != null && plan.Assignment.Count == 0)
            {
                int idleFit = FitPosition(DefaultFitOf(settings));
                if (idleFit >= 0)
                    desiredFit = idleFit;
            }
            if (desiredFit.HasValue)
            {
                if (!state.FitSaved)
                {
                    state.SavedFit = WallpaperApi.GetPosition();
                    state.FitSaved = true;
                }
                int currentFit = WallpaperApi.GetPosition();
                if (currentFit != desiredFit.Value)
                {
                    WallpaperApi.SetPosition(desiredFit.Value);
                    result.Changed = true;
                    Remember(result, "Wallpaper style set to " + FitName(desiredFit.Value) + ".");
                }
            }

            foreach (var monitor in monitors)
            {
                Assignment want;
                plan.Assignment.TryGetValue(monitor.DevicePath, out want);
                var recorded = FindState(state, monitor);

                if (want != null)
                {
                    string current = SafeGet(monitor.DevicePath);
                    string occurrence = want.Start.ToString("yyyy-MM-ddTHH:mm:ss");
                    bool same = recorded != null
                        && recorded.AppliedScheduleId == want.Item.Id
                        && recorded.AppliedOccurrence == occurrence
                        && PathsEqual(recorded.AppliedImage, want.Item.ImagePath)
                        && IsOurImage(current, recorded);
                    if (same)
                        continue;

                    if (recorded == null)
                    {
                        recorded = new MonitorState
                        {
                            DevicePath = monitor.DevicePath,
                            MonitorKey = monitor.Key
                        };
                        state.Monitors.Add(recorded);
                    }

                    // Remember the wallpaper from before this program changed it.
                    // The default picture is ours, so it must not become that original.
                    if (ShouldRememberOriginal(recorded, current, idleImage))
                    {
                        recorded.OriginalWallpaper = current ?? "";
                        recorded.OriginalSaved = true;
                    }

                    try
                    {
                        WallpaperApi.SetWallpaper(monitor.DevicePath, want.Item.ImagePath);
                    }
                    catch (Exception ex)
                    {
                        string message = "Windows rejected \"" + want.Item.Name + "\" on " + monitor.Model + ". " + ex.Message;
                        plan.Problems.Add(message);
                        continue;
                    }

                    recorded.DevicePath = monitor.DevicePath;
                    recorded.MonitorKey = monitor.Key;
                    recorded.AppliedScheduleId = want.Item.Id;
                    recorded.AppliedOccurrence = occurrence;
                    recorded.AppliedImage = want.Item.ImagePath;
                    recorded.AppliedReadback = SafeGet(monitor.DevicePath);
                    if (!string.IsNullOrEmpty(recorded.AppliedReadback))
                        monitor.CurrentWallpaper = recorded.AppliedReadback;
                    result.Changed = true;
                    Remember(result, "Set \"" + want.Item.Name + "\" on " + monitor.Model + " until " + want.End.ToString("ddd, MMM d, h:mm tt") + ".");
                }
                else if (idleImage != null)
                {
                    string current = SafeGet(monitor.DevicePath);
                    bool same = recorded != null
                        && recorded.AppliedScheduleId == DefaultMark
                        && PathsEqual(recorded.AppliedImage, idleImage)
                        && IsOurImage(current, recorded);
                    if (same)
                        continue;

                    if (recorded == null)
                    {
                        recorded = new MonitorState
                        {
                            DevicePath = monitor.DevicePath,
                            MonitorKey = monitor.Key
                        };
                        state.Monitors.Add(recorded);
                    }

                    if (ShouldRememberOriginal(recorded, current, idleImage))
                    {
                        recorded.OriginalWallpaper = current ?? "";
                        recorded.OriginalSaved = true;
                    }

                    try
                    {
                        WallpaperApi.SetWallpaper(monitor.DevicePath, idleImage);
                    }
                    catch (Exception ex)
                    {
                        plan.Problems.Add("Windows rejected the default wallpaper on " + monitor.Model + ". " + ex.Message);
                        continue;
                    }

                    recorded.DevicePath = monitor.DevicePath;
                    recorded.MonitorKey = monitor.Key;
                    recorded.AppliedScheduleId = DefaultMark;
                    recorded.AppliedOccurrence = "";
                    recorded.AppliedImage = idleImage;
                    recorded.AppliedReadback = SafeGet(monitor.DevicePath);
                    if (!string.IsNullOrEmpty(recorded.AppliedReadback))
                        monitor.CurrentWallpaper = recorded.AppliedReadback;
                    result.Changed = true;
                    Remember(result, "Set the default wallpaper on " + monitor.Model + ".");
                }
                else if (recorded != null && !string.IsNullOrEmpty(recorded.AppliedScheduleId))
                {
                    string current = SafeGet(monitor.DevicePath);
                    bool wasDefault = recorded.AppliedScheduleId == DefaultMark;
                    if (IsOurImage(current, recorded) && !string.IsNullOrWhiteSpace(recorded.OriginalWallpaper))
                    {
                        try
                        {
                            WallpaperApi.SetWallpaper(monitor.DevicePath, recorded.OriginalWallpaper);
                            monitor.CurrentWallpaper = recorded.OriginalWallpaper;
                            Remember(result, "Restored the previous wallpaper on " + monitor.Model + ".");
                        }
                        catch (Exception ex)
                        {
                            plan.Problems.Add("Couldn't restore the wallpaper on " + monitor.Model + ". " + ex.Message);
                        }
                    }
                    else if (!IsOurImage(current, recorded))
                    {
                        Remember(result, "Left " + monitor.Model + " as it is because the wallpaper was changed outside this program.");
                    }
                    else if (wasDefault)
                    {
                        Remember(result, "Cleared the default wallpaper on " + monitor.Model + ". There was no earlier wallpaper to put back.");
                    }
                    else
                    {
                        Remember(result, "The schedule on " + monitor.Model + " ended. There was no earlier wallpaper to put back.");
                    }

                    recorded.AppliedScheduleId = "";
                    recorded.AppliedOccurrence = "";
                    recorded.AppliedImage = "";
                    recorded.AppliedReadback = "";
                    recorded.OriginalWallpaper = "";
                    recorded.OriginalSaved = false;
                    result.Changed = true;
                }
            }

            bool scheduleApplied = false;
            bool defaultApplied = false;
            foreach (var recorded in state.Monitors)
            {
                if (recorded.AppliedScheduleId == DefaultMark)
                    defaultApplied = true;
                else if (!string.IsNullOrEmpty(recorded.AppliedScheduleId))
                    scheduleApplied = true;
            }
            bool defaultOwnsFit = defaultApplied && FitPosition(DefaultFitOf(settings)) >= 0;
            if (!scheduleApplied && state.FitSaved && !defaultOwnsFit)
            {
                try
                {
                    WallpaperApi.SetPosition(state.SavedFit);
                    Remember(result, "Restored the previous wallpaper style.");
                }
                catch (Exception ex)
                {
                    AppLog.Write("Couldn't restore the wallpaper style: " + ex.Message);
                }
                state.FitSaved = false;
                result.Changed = true;
            }

            state.Monitors.RemoveAll(delegate (MonitorState recorded)
            {
                return string.IsNullOrEmpty(recorded.AppliedScheduleId);
            });

            var still = new List<string>();
            foreach (var problem in plan.Problems)
            {
                still.Add(problem);
                bool seen = false;
                foreach (var old in state.LoggedProblems)
                {
                    if (old == problem) { seen = true; break; }
                }
                if (!seen)
                    AppLog.Write(problem);
            }
            state.LoggedProblems = still;

            if (plan.SchedulesChanged)
                Store.SaveSchedules(schedules);
            Store.SaveState(state);
            return result;
        }

        public static Plan BuildPlan(DateTime now, IList<MonitorInfo> monitors, IList<ScheduleItem> schedules, Func<string, bool> fileExists)
        {
            if (fileExists == null)
                fileExists = File.Exists;
            var plan = new Plan();
            if (schedules == null)
                schedules = new List<ScheduleItem>();
            if (monitors == null)
                monitors = new List<MonitorInfo>();

            foreach (var item in schedules)
            {
                if (item == null || !item.Enabled)
                    continue;
                DateTime template;
                if (!item.TryGetStart(out template))
                {
                    plan.Problems.Add("\"" + SafeName(item) + "\" has a start date that can't be read.");
                    continue;
                }

                DateTime start, end;
                if (!ScheduleMath.TryActive(item, now, out start, out end))
                    continue;

                if (string.IsNullOrWhiteSpace(item.ImagePath) || !fileExists(item.ImagePath))
                {
                    plan.Problems.Add("\"" + SafeName(item) + "\" is missing its picture: " + (item.ImagePath ?? ""));
                    continue;
                }

                var targets = ScheduleMath.Targets(item, monitors);
                if (targets.Count == 0)
                {
                    plan.Problems.Add("\"" + SafeName(item) + "\" is aimed at a monitor that is not connected.");
                    continue;
                }

                foreach (var monitor in targets)
                {
                    if (!string.Equals(item.MonitorId, monitor.DevicePath, StringComparison.OrdinalIgnoreCase)
                        && item.MonitorId != ScheduleItem.AllMonitors
                        && string.Equals(item.MonitorKey, monitor.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        item.MonitorId = monitor.DevicePath;
                        item.MonitorLabel = monitor.ChoiceLabel;
                        plan.SchedulesChanged = true;
                    }

                    Assignment existing;
                    if (!plan.Assignment.TryGetValue(monitor.DevicePath, out existing) || Beats(start, item, existing))
                    {
                        plan.Assignment[monitor.DevicePath] = new Assignment
                        {
                            Item = item,
                            Start = start,
                            End = end,
                            Monitor = monitor
                        };
                    }
                }
            }

            plan.Summary = Summarize(now, schedules, plan);
            return plan;
        }

        public static int FitPosition(string fit)
        {
            switch ((fit ?? "").Trim().ToLowerInvariant())
            {
                case "center": return 0;
                case "tile": return 1;
                case "stretch": return 2;
                case "fit": return 3;
                case "fill": return 4;
                case "span": return 5;
                default: return -1;
            }
        }

        public static void RunTests(List<string> errors)
        {
            var primary = new MonitorInfo { DevicePath = "path-a", Key = "A|UID1", Model = "ASUS", Placement = "Primary" };
            var left = new MonitorInfo { DevicePath = "path-b", Key = "B|UID2", Model = "Iiyama", Placement = "Left" };
            var monitors = new List<MonitorInfo> { primary, left };
            var now = new DateTime(2026, 10, 25, 12, 0, 0);

            var early = Item("1", "Early", "a.jpg", "path-a", "A|UID1", "2026-10-25T10:00:00", 5);
            var late = Item("2", "Late", "b.jpg", "path-a", "A|UID1", "2026-10-25T11:00:00", 3);
            var plan = BuildPlan(now, monitors, new List<ScheduleItem> { early, late }, delegate { return true; });
            Check(errors, "later schedule wins on the same monitor",
                plan.Assignment.ContainsKey("path-a") && plan.Assignment["path-a"].Item.Id == "2" && !plan.Assignment.ContainsKey("path-b"));

            plan = BuildPlan(now, monitors, new List<ScheduleItem> { early, late }, delegate (string path) { return path != "b.jpg"; });
            Check(errors, "a missing picture falls back to the earlier schedule",
                plan.Assignment.ContainsKey("path-a") && plan.Assignment["path-a"].Item.Id == "1");
            Check(errors, "missing picture is reported", plan.Problems.Count == 1);

            var all = Item("3", "All", "c.jpg", "*", "*", "2026-10-25T09:00:00", 8);
            plan = BuildPlan(now, monitors, new List<ScheduleItem> { all }, delegate { return true; });
            Check(errors, "all monitors receives the picture", plan.Assignment.Count == 2 && plan.Assignment["path-b"].Item.Id == "3");

            var off = Item("4", "Off", "c.jpg", "*", "*", "2026-10-25T09:00:00", 8);
            off.Enabled = false;
            plan = BuildPlan(now, monitors, new List<ScheduleItem> { off }, delegate { return true; });
            Check(errors, "disabled schedule assigns nothing", plan.Assignment.Count == 0);

            var other = Item("5", "Left only", "d.jpg", "path-b", "B|UID2", "2026-10-25T09:00:00", 8);
            plan = BuildPlan(now, monitors, new List<ScheduleItem> { other }, delegate { return true; });
            Check(errors, "one monitor stays untouched", plan.Assignment.Count == 1 && plan.Assignment.ContainsKey("path-b"));

            var moved = Item("6", "Moved", "d.jpg", "old-path", "B|UID2", "2026-10-25T09:00:00", 8);
            moved.MonitorLabel = "Iiyama";
            plan = BuildPlan(now, monitors, new List<ScheduleItem> { moved }, delegate { return true; });
            Check(errors, "a monitor is matched again by its hardware id",
                plan.Assignment.ContainsKey("path-b") && moved.MonitorId == "path-b" && plan.SchedulesChanged);

            var ready = new AppSettings { DefaultImagePath = @"C:\wall.jpg", DefaultFit = "fill" };
            Check(errors, "default picture is used when the file exists",
                IdlePicture(ready, delegate { return true; }) == @"C:\wall.jpg");
            Check(errors, "a missing default picture is skipped",
                IdlePicture(ready, delegate { return false; }) == null);
            Check(errors, "a blank default picture is skipped",
                IdlePicture(new AppSettings { DefaultImagePath = "  " }, delegate { return true; }) == null);
            Check(errors, "no default leaves the summary alone",
                DescribeIdle("No wallpapers scheduled yet.", false, false) == "No wallpapers scheduled yet.");
            Check(errors, "an idle default replaces the empty summary",
                DescribeIdle("No wallpapers scheduled yet.", true, false) == "The default wallpaper is up.");
            Check(errors, "an upcoming schedule keeps its time and mentions the default",
                DescribeIdle("Next: \"Party\" on Fri.", true, false).EndsWith("The default wallpaper stays up until then.", StringComparison.Ordinal));
            Check(errors, "an active schedule summary is left as it is",
                DescribeIdle("\"Party\" is on ASUS until Fri.", true, true) == "\"Party\" is on ASUS until Fri.");

            var fresh = new MonitorState();
            Check(errors, "the wallpaper before the first change is remembered",
                ShouldRememberOriginal(fresh, @"C:\old.jpg", @"C:\default.jpg"));
            Check(errors, "a desktop already showing the default is not treated as the original",
                !ShouldRememberOriginal(new MonitorState(), @"C:\default.jpg", @"C:\default.jpg"));
            fresh.OriginalSaved = true;
            fresh.OriginalWallpaper = @"C:\old.jpg";
            Check(errors, "a later picture does not replace the remembered wallpaper",
                !ShouldRememberOriginal(fresh, @"C:\default.jpg", @"C:\default.jpg"));
            var showingDefault = new MonitorState { AppliedScheduleId = DefaultMark, AppliedImage = @"C:\default.jpg" };
            Check(errors, "the default picture is not stored as the original",
                !ShouldRememberOriginal(showingDefault, @"C:\default.jpg", @"C:\other.jpg"));
        }

        static ScheduleItem Item(string id, string name, string image, string monitorId, string key, string start, int hours)
        {
            return new ScheduleItem
            {
                Id = id,
                Name = name,
                ImagePath = image,
                MonitorId = monitorId,
                MonitorKey = key,
                MonitorLabel = monitorId == "*" ? "All monitors" : monitorId,
                Start = start,
                DurationValue = hours,
                DurationUnit = "hours",
                Fit = "keep",
                Enabled = true
            };
        }

        static bool Beats(DateTime start, ScheduleItem item, Assignment existing)
        {
            if (start > existing.Start)
                return true;
            if (start < existing.Start)
                return false;
            return string.Compare(item.Id, existing.Item.Id, StringComparison.Ordinal) > 0;
        }

        public const string DefaultMark = "*default*";

        public static string IdlePicture(AppSettings settings, Func<string, bool> fileExists)
        {
            if (fileExists == null)
                fileExists = File.Exists;
            if (settings == null || string.IsNullOrWhiteSpace(settings.DefaultImagePath))
                return null;
            string path = settings.DefaultImagePath.Trim();
            return fileExists(path) ? path : null;
        }

        public static string DescribeIdle(string summary, bool hasDefault, bool anyScheduledNow)
        {
            if (!hasDefault || anyScheduledNow || string.IsNullOrEmpty(summary))
                return summary ?? "";
            if (summary.StartsWith("Next:", StringComparison.Ordinal))
                return summary + " The default wallpaper stays up until then.";
            if (summary.StartsWith("No wallpapers", StringComparison.Ordinal)
                || summary.StartsWith("No upcoming", StringComparison.Ordinal))
                return "The default wallpaper is up.";
            return summary;
        }

        static string DefaultFitOf(AppSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.DefaultFit))
                return "fill";
            return settings.DefaultFit.Trim();
        }

        static bool ShouldRememberOriginal(MonitorState recorded, string current, string idleImage)
        {
            if (recorded != null && recorded.OriginalSaved)
                return false;
            if (recorded != null && !string.IsNullOrEmpty(recorded.AppliedScheduleId))
                return false;
            if (IsOurImage(current, recorded))
                return false;
            if (!string.IsNullOrWhiteSpace(idleImage) && PathsEqual(current, idleImage))
                return false;
            return true;
        }

        static int? DesiredFit(Plan plan)
        {
            Assignment winner = null;
            foreach (var assignment in plan.Assignment.Values)
            {
                if (FitPosition(assignment.Item.Fit) < 0)
                    continue;
                if (winner == null || Beats(assignment.Start, assignment.Item, winner))
                    winner = assignment;
            }
            if (winner == null)
                return null;
            return FitPosition(winner.Item.Fit);
        }

        static string FitName(int position)
        {
            switch (position)
            {
                case 0: return "center";
                case 1: return "tile";
                case 2: return "stretch";
                case 3: return "fit";
                case 4: return "fill";
                case 5: return "span";
                default: return "current";
            }
        }

        static MonitorState FindState(AppState state, MonitorInfo monitor)
        {
            foreach (var recorded in state.Monitors)
            {
                if (string.Equals(recorded.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase))
                    return recorded;
            }
            foreach (var recorded in state.Monitors)
            {
                if (!string.IsNullOrEmpty(recorded.MonitorKey)
                    && string.Equals(recorded.MonitorKey, monitor.Key, StringComparison.OrdinalIgnoreCase))
                {
                    recorded.DevicePath = monitor.DevicePath;
                    return recorded;
                }
            }
            return null;
        }

        static bool IsOurImage(string current, MonitorState recorded)
        {
            if (recorded == null)
                return false;
            return PathsEqual(current, recorded.AppliedImage) || PathsEqual(current, recorded.AppliedReadback);
        }

        static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            try
            {
                a = Path.GetFullPath(a);
                b = Path.GetFullPath(b);
            }
            catch { }
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static string SafeGet(string devicePath)
        {
            try { return WallpaperApi.GetWallpaper(devicePath) ?? ""; }
            catch (Exception ex)
            {
                AppLog.Write("Couldn't read the wallpaper: " + ex.Message);
                return "";
            }
        }

        static void Remember(ApplyResult result, string message)
        {
            result.Actions.Add(message);
            AppLog.Write(message);
        }

        static string SafeName(ScheduleItem item)
        {
            return string.IsNullOrWhiteSpace(item.Name) ? "A schedule" : item.Name.Trim();
        }

        static string Summarize(DateTime now, IList<ScheduleItem> schedules, Plan plan)
        {
            if (schedules == null || schedules.Count == 0)
                return "No wallpapers scheduled yet.";

            if (plan.Assignment.Count > 0)
            {
                var parts = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var assignment in plan.Assignment.Values)
                {
                    if (!seen.Add(assignment.Item.Id))
                        continue;
                    string where = assignment.Item.MonitorId == ScheduleItem.AllMonitors
                        ? "all monitors"
                        : MonitorList(plan, assignment.Item.Id);
                    parts.Add("\"" + SafeName(assignment.Item) + "\" is on " + where + " until " + assignment.End.ToString("ddd, MMM d, h:mm tt"));
                }
                return string.Join("   ·   ", parts);
            }

            ScheduleItem next = null;
            DateTime nextStart = DateTime.MaxValue;
            foreach (var item in schedules)
            {
                DateTime start;
                if (item != null && item.Enabled && ScheduleMath.TryNextFuture(item, now, out start) && start < nextStart)
                {
                    next = item;
                    nextStart = start;
                }
            }
            if (next != null)
                return "Next: \"" + SafeName(next) + "\" on " + nextStart.ToString("ddd, MMM d, yyyy") + " at " + nextStart.ToString("h:mm tt") + ", for " + next.DurationText + ".";

            if (plan.Problems.Count > 0)
                return plan.Problems[0];
            return "No upcoming wallpapers. Add a date, or turn a past one into a yearly repeat.";
        }

        static string MonitorList(Plan plan, string scheduleId)
        {
            var names = new List<string>();
            foreach (var assignment in plan.Assignment.Values)
            {
                if (assignment.Item.Id != scheduleId)
                    continue;
                if (!names.Contains(assignment.Monitor.Model))
                    names.Add(assignment.Monitor.Model);
            }
            return string.Join(", ", names);
        }

        static void Check(List<string> errors, string name, bool ok)
        {
            if (!ok) errors.Add(name);
        }
    }
}
