using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WallpaperScheduler
{
    sealed class MainForm : Form
    {
        const int ColOn = 0;
        const int ColName = 1;
        const int ColStarts = 2;
        const int ColLasts = 3;
        const int ColMonitor = 4;
        const int ColPicture = 5;
        const int ColStatus = 6;

        readonly bool startHidden;
        bool allowShow;
        bool reallyExit;
        bool suppress;
        bool started;
        int applying;

        AppSettings settings;
        List<ScheduleItem> schedules = new List<ScheduleItem>();
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        Plan plan = new Plan();

        readonly FlowLayoutPanel monitorStrip;
        readonly DataGridView grid;
        readonly Label emptyLabel;
        readonly Label statusLabel;
        readonly CheckBox startupBox;
        readonly CheckBox backgroundBox;
        readonly Button editButton;
        readonly Button removeButton;
        readonly ToolTip tips = new ToolTip();
        NotifyIcon tray;
        System.Windows.Forms.Timer timer;
        EventWaitHandle showEvent;
        Thread showThread;
        volatile bool stopping;

        readonly PictureBox defaultPreview;
        readonly Label defaultPathLabel;
        readonly ComboBox defaultFitBox;
        readonly Button defaultClearButton;

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;
            allowShow = !startHidden;
            Text = "Wallpaper Scheduler";
            Font = Ui.BaseFont;
            BackColor = Theme.AppBack;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 700);

            var area = Screen.PrimaryScreen.WorkingArea;
            Width = Math.Min(1180, Math.Max(960, area.Width - 80));
            Height = Math.Min(780, Math.Max(660, area.Height - 80));

            var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Header };
            header.Controls.Add(new Label
            {
                Text = "Wallpaper Scheduler",
                ForeColor = Color.White,
                Font = Ui.TitleFont,
                AutoSize = true,
                Location = new Point(18, 12)
            });
            header.Controls.Add(new Label
            {
                Text = "Pick a picture, a monitor, a date, and how long it stays up.",
                ForeColor = Theme.HeaderSub,
                Font = Ui.BaseFont,
                AutoSize = true,
                Location = new Point(20, 46)
            });

            var monitorHost = new Panel { Dock = DockStyle.Top, Height = 158, BackColor = Theme.AppBack };
            var monitorTitle = new Label
            {
                Text = "Monitors",
                Font = Ui.BoldFont,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(16, 10)
            };
            var refresh = new LinkLabel
            {
                Text = "Refresh",
                AutoSize = true,
                Location = new Point(90, 10)
            };
            refresh.LinkClicked += delegate { RefreshMonitors(); };
            monitorStrip = new FlowLayoutPanel
            {
                Location = new Point(16, 36),
                Height = 114,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.AppBack
            };
            monitorHost.Controls.Add(monitorTitle);
            monitorHost.Controls.Add(refresh);
            monitorHost.Controls.Add(monitorStrip);
            monitorHost.Resize += delegate { monitorStrip.Width = Math.Max(200, monitorHost.ClientSize.Width - 32); };

            var defaultHost = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White };
            defaultPreview = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(232, 236, 240)
            };
            var defaultTitle = new Label
            {
                Text = "Default wallpaper",
                Font = Ui.BoldFont,
                ForeColor = Theme.Text,
                AutoEllipsis = true
            };
            defaultPathLabel = new Label
            {
                Text = "None. A monitor keeps its previous wallpaper when nothing is scheduled.",
                ForeColor = Theme.Muted,
                AutoEllipsis = true
            };
            defaultFitBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            defaultFitBox.Items.Add(new Choice { Id = "keep", Text = "Keep Windows fit" });
            defaultFitBox.Items.Add(new Choice { Id = "fill", Text = "Fill the screen" });
            defaultFitBox.Items.Add(new Choice { Id = "fit", Text = "Fit the whole picture" });
            defaultFitBox.Items.Add(new Choice { Id = "stretch", Text = "Stretch" });
            defaultFitBox.Items.Add(new Choice { Id = "center", Text = "Center" });
            defaultFitBox.Items.Add(new Choice { Id = "tile", Text = "Tile" });
            defaultFitBox.Items.Add(new Choice { Id = "span", Text = "Span across monitors" });
            defaultFitBox.SelectedIndexChanged += delegate { OnDefaultFitChanged(); };
            var defaultBrowse = Ui.Secondary("Browse…", 96);
            defaultClearButton = Ui.Secondary("Clear", 80);
            defaultBrowse.Click += delegate { BrowseDefault(); };
            defaultClearButton.Click += delegate { ClearDefault(); };
            tips.SetToolTip(defaultBrowse, "Choose the picture used when a monitor has no schedule.");
            tips.SetToolTip(defaultClearButton, "Remove the default. Monitors go back to the wallpaper from before this program changed them.");
            tips.SetToolTip(defaultFitBox, "How the default picture is placed. A schedule can use a different fit while it is up.");
            defaultHost.Controls.Add(defaultPreview);
            defaultHost.Controls.Add(defaultTitle);
            defaultHost.Controls.Add(defaultPathLabel);
            defaultHost.Controls.Add(defaultFitBox);
            defaultHost.Controls.Add(defaultBrowse);
            defaultHost.Controls.Add(defaultClearButton);
            defaultHost.Resize += delegate { LayoutDefault(defaultHost, defaultTitle, defaultBrowse); };
            defaultHost.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Line))
                {
                    e.Graphics.DrawLine(pen, 0, 0, defaultHost.Width, 0);
                    e.Graphics.DrawLine(pen, 0, defaultHost.Height - 1, defaultHost.Width, defaultHost.Height - 1);
                }
            };
            LayoutDefault(defaultHost, defaultTitle, defaultBrowse);

            var tools = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White, Padding = new Padding(12, 8, 12, 0) };
            var addButton = Ui.Primary("Add schedule", 130);
            editButton = Ui.Secondary("Edit", 80);
            removeButton = Ui.Secondary("Remove", 90);
            var applyButton = Ui.Secondary("Apply now", 110);
            addButton.Location = new Point(12, 8);
            editButton.Location = new Point(150, 8);
            removeButton.Location = new Point(238, 8);
            applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            tools.Controls.Add(addButton);
            tools.Controls.Add(editButton);
            tools.Controls.Add(removeButton);
            tools.Controls.Add(applyButton);
            tools.Resize += delegate { applyButton.Left = tools.ClientSize.Width - applyButton.Width - 12; };
            addButton.Click += delegate { AddSchedule(null); };
            editButton.Click += delegate { EditSelected(); };
            removeButton.Click += delegate { RemoveSelected(); };
            applyButton.Click += delegate { RunApply(); };
            tips.SetToolTip(addButton, "Add a schedule (Ctrl+N)");
            tips.SetToolTip(applyButton, "Check the dates and update the desktop now (F5)");

            grid = BuildGrid();
            emptyLabel = new Label
            {
                Text = "No wallpapers scheduled yet.\r\nClick Add schedule, then choose the picture, the monitor, the date, and how long it stays up.",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = Theme.Muted,
                Visible = false
            };

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 0, 12, 0) };
            gridHost.Controls.Add(grid);
            gridHost.Controls.Add(emptyLabel);

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ForeColor = Theme.Muted,
                Padding = new Padding(12, 4, 12, 0),
                Text = "If two schedules cover the same monitor, the one that started later is used. Spotlight and the wallpaper slideshow can override this; turn them off in Settings, Personalization, Background."
            };

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            right.Controls.Add(gridHost);
            right.Controls.Add(note);
            right.Controls.Add(tools);

            startupBox = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Margin = new Padding(0, 6, 24, 0)
            };
            backgroundBox = new CheckBox
            {
                Text = "Keep working after this program is closed",
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };
            startupBox.CheckedChanged += delegate { OnStartupChanged(); };
            backgroundBox.CheckedChanged += delegate { OnBackgroundChanged(); };
            tips.SetToolTip(startupBox, "Opens this program in the tray when you sign in to Windows.");
            tips.SetToolTip(backgroundBox, "Checks about once a minute, so dates still change after you exit.");

            var checks = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            checks.Controls.Add(startupBox);
            checks.Controls.Add(backgroundBox);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "Ready."
            };
            var logLink = new LinkLabel { Text = "Log", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };
            var dataLink = new LinkLabel { Text = "Data folder", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            logLink.LinkClicked += delegate { OpenLog(); };
            dataLink.LinkClicked += delegate { OpenData(); };
            var links = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 150,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            links.Controls.Add(logLink);
            links.Controls.Add(dataLink);
            var statusRow = new Panel { Dock = DockStyle.Fill };
            statusRow.Controls.Add(statusLabel);
            statusRow.Controls.Add(links);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Color.White, Padding = new Padding(16, 8, 16, 8) };
            bottom.Controls.Add(statusRow);
            bottom.Controls.Add(checks);
            bottom.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Line))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.AppBack, Padding = new Padding(0, 0, 0, 8) };
            body.Controls.Add(right);
            body.Controls.Add(defaultHost);
            body.Controls.Add(monitorHost);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            if (startHidden)
            {
                ShowInTaskbar = false;
                if (!IsHandleCreated)
                    CreateHandle();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(allowShow && value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (startHidden)
                Start();
        }

        void Start()
        {
            if (started)
                return;
            started = true;
            Directory.CreateDirectory(AppPaths.Root);
            settings = Store.LoadSettings();
            schedules = Store.LoadSchedules();

            suppress = true;
            startupBox.Checked = settings.StartWithWindows;
            backgroundBox.Checked = settings.BackgroundTask;
            SelectDefaultFit(string.IsNullOrWhiteSpace(settings.DefaultFit) ? "fill" : settings.DefaultFit);
            suppress = false;
            RefreshDefaultStrip();

            string setupError = ApplyStartupSettings();
            CreateTray();
            FillGrid();
            UpdateButtons();

            timer = new System.Windows.Forms.Timer { Interval = 20000 };
            timer.Tick += delegate { RunApply(); };
            timer.Start();

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.TimeChanged += OnTimeChanged;

            showEvent = Program.CreateShowEvent();
            showThread = new Thread(WaitForShow);
            showThread.IsBackground = true;
            showThread.Start();

            RunApply();
            if (!string.IsNullOrEmpty(setupError))
                SetStatus("Background setup needs a look: " + setupError, true);
        }

        string ApplyStartupSettings()
        {
            var errors = new List<string>();
            try { StartupManager.SetStartWithWindows(settings.StartWithWindows); }
            catch (Exception ex) { errors.Add(ex.Message); }
            try
            {
                if (settings.BackgroundTask) StartupManager.EnableTask();
                else StartupManager.DisableTask();
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            try { Store.SaveSettings(settings); } catch { }
            return errors.Count == 0 ? null : string.Join(" ", errors);
        }

        void CreateTray()
        {
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (icon == null) icon = SystemIcons.Application;
            Icon = icon;

            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Open Wallpaper Scheduler");
            var apply = new ToolStripMenuItem("Apply schedules now");
            var exit = new ToolStripMenuItem("Exit");
            open.Click += delegate { ShowFromTray(); };
            apply.Click += delegate { RunApply(); };
            exit.Click += delegate { ExitFromTray(); };
            menu.Items.Add(open);
            menu.Items.Add(apply);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            tray = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = "Wallpaper Scheduler",
                ContextMenuStrip = menu
            };
            tray.DoubleClick += delegate { ShowFromTray(); };
        }

        void WaitForShow()
        {
            while (!stopping && showEvent != null)
            {
                bool signaled;
                try { signaled = showEvent.WaitOne(500); }
                catch { break; }
                if (!signaled || stopping)
                    continue;
                try { BeginInvoke(new Action(ShowFromTray)); }
                catch { }
            }
        }

        void ShowFromTray()
        {
            allowShow = true;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void HideToTray()
        {
            allowShow = false;
            ShowInTaskbar = false;
            Hide();
            if (tray != null && settings != null && !settings.BalloonShown)
            {
                tray.BalloonTipTitle = "Wallpaper Scheduler is still running";
                tray.BalloonTipText = "Your dates will still change the wallpaper. Right-click this icon and choose Exit to quit.";
                tray.ShowBalloonTip(4000);
                settings.BalloonShown = true;
                try { Store.SaveSettings(settings); } catch { }
            }
        }

        void ExitFromTray()
        {
            if (!ConfirmExit())
                return;
            reallyExit = true;
            Close();
        }

        bool ConfirmExit()
        {
            if (settings == null || settings.BackgroundTask || settings.ExitNoteShown)
                return true;
            bool any = false;
            foreach (var item in schedules)
            {
                if (item.Enabled) { any = true; break; }
            }
            if (!any)
                return true;

            ShowFromTray();
            var answer = MessageBox.Show(this,
                "Your schedules pause while this program is closed.\r\n\r\nYes: keep changing wallpapers in the background, then close.\r\nNo: close and pause the schedules.\r\nCancel: stay open.",
                "Wallpaper Scheduler",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                return false;
            if (answer == DialogResult.Yes)
            {
                backgroundBox.Checked = true;
                if (!settings.BackgroundTask)
                    return false;
            }
            settings.ExitNoteShown = true;
            try { Store.SaveSettings(settings); } catch { }
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !reallyExit)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            stopping = true;
            if (timer != null) timer.Stop();
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.TimeChanged -= OnTimeChanged;
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            if (defaultPreview != null && defaultPreview.Image != null)
            {
                defaultPreview.Image.Dispose();
                defaultPreview.Image = null;
            }
            base.OnFormClosing(e);
        }

        void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && IsHandleCreated)
            {
                try { BeginInvoke(new Action(RunApply)); } catch { }
            }
        }

        void OnTimeChanged(object sender, EventArgs e)
        {
            if (IsHandleCreated)
            {
                try { BeginInvoke(new Action(RunApply)); } catch { }
            }
        }

        void OnStartupChanged()
        {
            if (suppress || settings == null)
                return;
            settings.StartWithWindows = startupBox.Checked;
            try
            {
                StartupManager.SetStartWithWindows(settings.StartWithWindows);
                Store.SaveSettings(settings);
                SetStatus(settings.StartWithWindows
                    ? "This program will start with Windows."
                    : "This program will not start with Windows.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't update Windows startup: " + ex.Message, true);
            }
        }

        void OnBackgroundChanged()
        {
            if (suppress || settings == null)
                return;
            bool turnOn = backgroundBox.Checked;
            try
            {
                if (turnOn) StartupManager.EnableTask();
                else StartupManager.DisableTask();
                settings.BackgroundTask = turnOn;
                Store.SaveSettings(settings);
                SetStatus(settings.BackgroundTask
                    ? "Wallpapers will still change when this program is closed."
                    : "Wallpapers change only while this program is open.", false);
            }
            catch (Exception ex)
            {
                suppress = true;
                backgroundBox.Checked = settings.BackgroundTask;
                suppress = false;
                SetStatus("Couldn't update the background check: " + ex.Message, true);
            }
        }

        void RunApply()
        {
            if (Interlocked.Exchange(ref applying, 1) == 1)
                return;
            try
            {
                var result = Engine.Apply();
                if (result.Monitors != null && result.Monitors.Count > 0)
                    monitors = result.Monitors;
                plan = result.Plan ?? new Plan();
                schedules = Store.LoadSchedules();
                FillGrid();
                UpdateCards(result.Changed);
                string text = plan.Summary ?? "Ready.";
                bool error = false;
                if (plan.Problems != null)
                {
                    foreach (var problem in plan.Problems)
                    {
                        if (problem.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0
                            || problem.StartsWith("Couldn't", StringComparison.Ordinal)
                            || problem.StartsWith("The default wallpaper picture", StringComparison.Ordinal))
                        {
                            text = problem;
                            error = true;
                            break;
                        }
                    }
                }
                if (!error && result.Actions.Count > 0 && result.Changed)
                    text = result.Actions[result.Actions.Count - 1];
                SetStatus(text, error);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex.ToString());
                SetStatus("Couldn't update the wallpaper: " + ex.Message, true);
            }
            finally
            {
                applying = 0;
            }
        }

        void RefreshMonitors()
        {
            WallpaperApi.ForgetMonitorNames();
            RunApply();
        }

        void AddSchedule(string imagePath)
        {
            var now = DateTime.Now;
            var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            var item = new ScheduleItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "",
                ImagePath = imagePath ?? "",
                MonitorId = ScheduleItem.AllMonitors,
                MonitorKey = ScheduleItem.AllMonitors,
                MonitorLabel = "All monitors",
                Start = start.ToString("yyyy-MM-ddTHH:mm:ss"),
                DurationValue = 1,
                DurationUnit = "days",
                RepeatYearly = false,
                Fit = "fill",
                Enabled = true
            };
            using (var dialog = new EditForm(item, monitors, true))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
            }
            schedules.Add(item);
            Store.SaveSchedules(schedules);
            RunApply();
        }

        void EditSelected()
        {
            var item = SelectedItem();
            if (item == null)
            {
                SetStatus("Select a schedule to edit.", false);
                return;
            }
            var copy = item.Clone();
            using (var dialog = new EditForm(copy, monitors, false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
            }
            int index = schedules.FindIndex(delegate (ScheduleItem candidate) { return candidate.Id == copy.Id; });
            if (index >= 0) schedules[index] = copy;
            else schedules.Add(copy);
            Store.SaveSchedules(schedules);
            RunApply();
        }

        void RemoveSelected()
        {
            var selected = SelectedItems();
            if (selected.Count == 0)
            {
                SetStatus("Select a schedule to remove.", false);
                return;
            }
            string prompt = selected.Count == 1
                ? "Remove \"" + selected[0].Name + "\"?"
                : "Remove " + selected.Count + " schedules?";
            string restore = HasDefaultPicture()
                ? "If one of these wallpapers is up right now, the default wallpaper is put back."
                : "If one of these wallpapers is up right now, the previous wallpaper is put back.";
            var answer = MessageBox.Show(this,
                prompt + "\r\n\r\n" + restore,
                "Wallpaper Scheduler",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
                return;
            foreach (var item in selected)
                schedules.RemoveAll(delegate (ScheduleItem candidate) { return candidate.Id == item.Id; });
            Store.SaveSchedules(schedules);
            RunApply();
        }

        ScheduleItem SelectedItem()
        {
            var items = SelectedItems();
            return items.Count == 1 ? items[0] : null;
        }

        List<ScheduleItem> SelectedItems()
        {
            var list = new List<ScheduleItem>();
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                var id = row.Tag as string;
                var item = FindSchedule(id);
                if (item != null) list.Add(item);
            }
            return list;
        }

        ScheduleItem FindSchedule(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (var item in schedules)
            {
                if (item.Id == id) return item;
            }
            return null;
        }

        void FillGrid()
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                var id = row.Tag as string;
                if (!string.IsNullOrEmpty(id)) selected.Add(id);
            }

            var ordered = new List<ScheduleItem>(schedules);
            var now = DateTime.Now;
            ordered.Sort(delegate (ScheduleItem a, ScheduleItem b)
            {
                int rank = Rank(a, now).CompareTo(Rank(b, now));
                if (rank != 0) return rank;
                DateTime aStart, bStart;
                bool aHas = ScheduleMath.TryNextOrCurrent(a, now, out aStart);
                bool bHas = ScheduleMath.TryNextOrCurrent(b, now, out bStart);
                if (aHas && bHas) return aStart.CompareTo(bStart);
                if (aHas) return -1;
                if (bHas) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            suppress = true;
            grid.Rows.Clear();
            foreach (var item in ordered)
            {
                int index = grid.Rows.Add(
                    item.Enabled,
                    string.IsNullOrWhiteSpace(item.Name) ? PictureName(item.ImagePath) : item.Name,
                    StartsText(item),
                    item.DurationText,
                    MonitorText(item),
                    PictureName(item.ImagePath),
                    StatusText(item, now));
                var row = grid.Rows[index];
                row.Tag = item.Id;
                row.Cells[ColPicture].ToolTipText = item.ImagePath ?? "";
                row.Cells[ColStatus].ToolTipText = row.Cells[ColStatus].Value as string;
            }
            suppress = false;

            emptyLabel.Visible = schedules.Count == 0;
            grid.Visible = schedules.Count > 0;
            if (selected.Count > 0)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    var id = row.Tag as string;
                    row.Selected = id != null && selected.Contains(id);
                }
            }
            UpdateButtons();
        }

        int Rank(ScheduleItem item, DateTime now)
        {
            if (item == null || !item.Enabled) return 5;
            if (IsAssigned(item)) return 0;
            DateTime start, end;
            if (ScheduleMath.TryActive(item, now, out start, out end)) return 2;
            DateTime future;
            if (ScheduleMath.TryNextFuture(item, now, out future)) return 1;
            if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath)) return 3;
            return 4;
        }

        bool IsAssigned(ScheduleItem item)
        {
            if (plan == null || plan.Assignment == null) return false;
            foreach (var assignment in plan.Assignment.Values)
            {
                if (assignment.Item != null && assignment.Item.Id == item.Id)
                    return true;
            }
            return false;
        }

        string StartsText(ScheduleItem item)
        {
            DateTime start;
            if (!item.TryGetStart(out start))
                return "Invalid date";
            if (item.RepeatYearly)
                return "Every year · " + start.ToString("MMM d, h:mm tt");
            return start.ToString("ddd, MMM d, yyyy, h:mm tt");
        }

        string MonitorText(ScheduleItem item)
        {
            if (item.MonitorId == ScheduleItem.AllMonitors)
                return "All monitors";
            var monitor = ScheduleMath.FindMonitor(item, monitors);
            if (monitor != null)
                return monitor.Model + " (" + monitor.Placement + ")";
            if (!string.IsNullOrWhiteSpace(item.MonitorLabel))
                return item.MonitorLabel + " (not connected)";
            return "Monitor not connected";
        }

        string StatusText(ScheduleItem item, DateTime now)
        {
            if (!item.Enabled)
                return "Off";
            if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
                return "Picture not found";
            bool monitorMissing = item.MonitorId != ScheduleItem.AllMonitors && ScheduleMath.FindMonitor(item, monitors) == null;
            if (IsAssigned(item))
            {
                foreach (var assignment in plan.Assignment.Values)
                {
                    if (assignment.Item != null && assignment.Item.Id == item.Id)
                        return "Active until " + assignment.End.ToString("ddd, MMM d, h:mm tt");
                }
            }
            DateTime start, end;
            if (ScheduleMath.TryActive(item, now, out start, out end))
            {
                if (monitorMissing) return "Monitor not connected";
                return "Covered by a later schedule";
            }
            DateTime future;
            if (ScheduleMath.TryNextFuture(item, now, out future))
            {
                string when = "Starts " + future.ToString("ddd, MMM d, h:mm tt");
                return monitorMissing ? "Monitor not connected · " + when : when;
            }
            if (monitorMissing)
                return "Monitor not connected";
            return "Ended " + ScheduleMath.LastEnd(item, now).ToString("MMM d, yyyy");
        }

        static string PictureName(string path)
        {
            try
            {
                string name = Path.GetFileName(path ?? "");
                return string.IsNullOrWhiteSpace(name) ? "" : name;
            }
            catch { return path ?? ""; }
        }

        void UpdateCards(bool reloadThumbs)
        {
            var existing = new Dictionary<string, MonitorCard>(StringComparer.OrdinalIgnoreCase);
            foreach (Control control in monitorStrip.Controls)
            {
                var card = control as MonitorCard;
                if (card != null) existing[card.DevicePath] = card;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int insert = 0;
            foreach (var monitor in monitors)
            {
                seen.Add(monitor.DevicePath);
                string active = "Nothing scheduled right now";
                Color stateColor = Theme.Muted;
                Assignment assignment;
                if (plan != null && plan.Assignment != null && plan.Assignment.TryGetValue(monitor.DevicePath, out assignment))
                {
                    active = "Showing: " + assignment.Item.Name;
                    stateColor = Theme.Good;
                }
                else if (HasDefaultPicture())
                {
                    active = "Default: " + PictureName(settings.DefaultImagePath);
                    stateColor = Theme.Text;
                }
                else if (settings != null && !string.IsNullOrWhiteSpace(settings.DefaultImagePath))
                {
                    active = "Default picture not found";
                    stateColor = Theme.Bad;
                }
                MonitorCard card;
                if (!existing.TryGetValue(monitor.DevicePath, out card))
                {
                    card = new MonitorCard();
                    monitorStrip.Controls.Add(card);
                    monitorStrip.Controls.SetChildIndex(card, insert);
                }
                else
                {
                    monitorStrip.Controls.SetChildIndex(card, insert);
                }
                card.Bind(monitor, active, stateColor, reloadThumbs);
                insert++;
            }

            var stale = new List<MonitorCard>();
            foreach (var pair in existing)
            {
                if (!seen.Contains(pair.Key)) stale.Add(pair.Value);
            }
            foreach (var card in stale)
            {
                monitorStrip.Controls.Remove(card);
                card.Dispose();
            }
            if (monitorStrip.Parent != null)
                monitorStrip.Width = Math.Max(200, monitorStrip.Parent.ClientSize.Width - 32);
        }

        DataGridView BuildGrid()
        {
            var view = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.Line,
                Font = Ui.BaseFont,
                RowTemplate = { Height = 34 },
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            view.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 244);
            view.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Header;
            view.ColumnHeadersDefaultCellStyle.Font = Ui.BoldFont;
            view.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 238, 244);
            view.DefaultCellStyle.SelectionBackColor = Theme.Select;
            view.DefaultCellStyle.SelectionForeColor = Theme.Text;
            view.DefaultCellStyle.ForeColor = Theme.Text;
            view.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 251);
            view.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.Select;

            var on = new DataGridViewCheckBoxColumn { HeaderText = "On", FillWeight = 8, MinimumWidth = 44 };
            var name = new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 18, MinimumWidth = 90, ReadOnly = true };
            var starts = new DataGridViewTextBoxColumn { HeaderText = "Starts", FillWeight = 24, MinimumWidth = 140, ReadOnly = true };
            var lasts = new DataGridViewTextBoxColumn { HeaderText = "Lasts", FillWeight = 12, MinimumWidth = 70, ReadOnly = true };
            var monitor = new DataGridViewTextBoxColumn { HeaderText = "Monitor", FillWeight = 20, MinimumWidth = 120, ReadOnly = true };
            var picture = new DataGridViewTextBoxColumn { HeaderText = "Picture", FillWeight = 18, MinimumWidth = 100, ReadOnly = true };
            var status = new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 24, MinimumWidth = 140, ReadOnly = true };
            view.Columns.AddRange(on, name, starts, lasts, monitor, picture, status);

            view.CurrentCellDirtyStateChanged += delegate
            {
                if (suppress) return;
                if (view.IsCurrentCellDirty && view.CurrentCell is DataGridViewCheckBoxCell)
                    view.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            view.CellContentClick += delegate (object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == ColOn)
                    view.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            view.CellValueChanged += delegate (object sender, DataGridViewCellEventArgs e)
            {
                if (suppress || e.RowIndex < 0 || e.ColumnIndex != ColOn)
                    return;
                var item = FindSchedule(view.Rows[e.RowIndex].Tag as string);
                if (item == null) return;
                item.Enabled = Convert.ToBoolean(view.Rows[e.RowIndex].Cells[ColOn].Value ?? false);
                Store.SaveSchedules(schedules);
                BeginInvoke(new Action(RunApply));
            };
            view.CellDoubleClick += delegate (object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex != ColOn)
                    EditSelected();
            };
            view.SelectionChanged += delegate { UpdateButtons(); };
            view.CellFormatting += delegate (object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != ColStatus || e.Value == null)
                    return;
                string text = Convert.ToString(e.Value);
                if (text.StartsWith("Active", StringComparison.Ordinal))
                    e.CellStyle.ForeColor = Theme.Good;
                else if (text.StartsWith("Picture", StringComparison.Ordinal) || text.StartsWith("Monitor", StringComparison.Ordinal))
                    e.CellStyle.ForeColor = Theme.Bad;
                else if (text.StartsWith("Off", StringComparison.Ordinal) || text.StartsWith("Ended", StringComparison.Ordinal))
                    e.CellStyle.ForeColor = Theme.Muted;
            };
            return view;
        }

        void UpdateButtons()
        {
            int count = grid.SelectedRows.Count;
            editButton.Enabled = count == 1;
            removeButton.Enabled = count > 0;
        }

        void LayoutDefault(Panel host, Label title, Button browse)
        {
            defaultPreview.SetBounds(16, 16, 72, 44);
            int right = host.ClientSize.Width - 16;
            defaultClearButton.SetBounds(right - 80, 22, 80, 32);
            browse.SetBounds(defaultClearButton.Left - 8 - 96, 22, 96, 32);
            defaultFitBox.SetBounds(browse.Left - 12 - 190, 22, 190, 28);
            int textLeft = 100;
            int textWidth = Math.Max(40, defaultFitBox.Left - 12 - textLeft);
            title.SetBounds(textLeft, 14, textWidth, 20);
            defaultPathLabel.SetBounds(textLeft, 36, textWidth, 22);
        }

        void SelectDefaultFit(string id)
        {
            for (int i = 0; i < defaultFitBox.Items.Count; i++)
            {
                var choice = defaultFitBox.Items[i] as Choice;
                if (choice != null && string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    defaultFitBox.SelectedIndex = i;
                    return;
                }
            }
            if (!string.Equals(id, "fill", StringComparison.OrdinalIgnoreCase))
                SelectDefaultFit("fill");
        }

        void RefreshDefaultStrip()
        {
            string path = settings == null ? "" : (settings.DefaultImagePath ?? "").Trim();
            defaultClearButton.Enabled = path.Length > 0;
            if (path.Length == 0)
            {
                defaultPathLabel.Text = "None. A monitor keeps its previous wallpaper when nothing is scheduled.";
                defaultPathLabel.ForeColor = Theme.Muted;
            }
            else if (!File.Exists(path))
            {
                defaultPathLabel.Text = "Not found: " + path;
                defaultPathLabel.ForeColor = Theme.Bad;
            }
            else
            {
                defaultPathLabel.Text = Path.GetFileName(path);
                defaultPathLabel.ForeColor = Theme.Text;
            }
            tips.SetToolTip(defaultPathLabel, path.Length == 0 ? "Choose a picture to show whenever a monitor has no schedule." : path);

            var old = defaultPreview.Image;
            defaultPreview.Image = path.Length > 0 && File.Exists(path) ? Ui.LoadCover(path, 144, 88) : null;
            if (old != null)
                old.Dispose();
        }

        bool HasDefaultPicture()
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.DefaultImagePath))
                return false;
            return File.Exists(settings.DefaultImagePath.Trim());
        }

        void BrowseDefault()
        {
            if (settings == null)
                return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = PictureFiles.Filter;
                dialog.Title = "Choose the default wallpaper";
                dialog.CheckFileExists = true;
                string current = (settings.DefaultImagePath ?? "").Trim();
                if (current.Length > 0)
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(current);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                            dialog.InitialDirectory = folder;
                    }
                    catch { }
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                settings.DefaultImagePath = dialog.FileName;
                var choice = defaultFitBox.SelectedItem as Choice;
                if (string.IsNullOrWhiteSpace(settings.DefaultFit))
                    settings.DefaultFit = choice == null ? "fill" : choice.Id;
                SaveDefaultAndApply();
            }
        }

        void ClearDefault()
        {
            if (settings == null)
                return;
            settings.DefaultImagePath = "";
            SaveDefaultAndApply();
        }

        void OnDefaultFitChanged()
        {
            if (suppress || settings == null)
                return;
            var choice = defaultFitBox.SelectedItem as Choice;
            string fit = choice == null ? "fill" : choice.Id;
            if (string.Equals(settings.DefaultFit ?? "", fit, StringComparison.OrdinalIgnoreCase))
                return;
            settings.DefaultFit = fit;
            SaveDefaultAndApply();
        }

        void SaveDefaultAndApply()
        {
            try
            {
                Store.SaveSettings(settings);
                RefreshDefaultStrip();
                RunApply();
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't save the default wallpaper: " + ex.Message, true);
            }
        }

        void SetStatus(string text, bool error)
        {
            if (string.IsNullOrWhiteSpace(text))
                text = "Ready.";
            statusLabel.Text = text;
            statusLabel.ForeColor = error ? Theme.Bad : Theme.Text;
            tips.SetToolTip(statusLabel, text);
        }

        void OpenLog()
        {
            try
            {
                if (!File.Exists(AppPaths.Log))
                {
                    SetStatus("There's nothing in the log yet.", false);
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppPaths.Log,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't open the log: " + ex.Message, true);
            }
        }

        void OpenData()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppPaths.Root,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't open the data folder: " + ex.Message, true);
            }
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;
            if (!PictureFiles.IsKnown(files[0]))
            {
                SetStatus("Drop a JPG, PNG, BMP, GIF, or TIFF.", true);
                return;
            }
            if (files.Length > 1)
                SetStatus("Using the first picture. Add the others one at a time.", false);
            AddSchedule(files[0]);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N))
            {
                AddSchedule(null);
                return true;
            }
            if (keyData == Keys.F5)
            {
                RunApply();
                return true;
            }
            if (keyData == Keys.Delete && grid.ContainsFocus)
            {
                RemoveSelected();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    sealed class MonitorCard : Panel
    {
        readonly PictureBox picture;
        readonly Label title;
        readonly Label detail;
        readonly Label stateLabel;
        string wallpaperPath;

        public string DevicePath { get; private set; }

        public MonitorCard()
        {
            Width = 370;
            Height = 100;
            Margin = new Padding(0, 0, 12, 8);
            BackColor = Color.White;
            picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(232, 236, 240) };
            title = new Label { Font = Ui.BoldFont, ForeColor = Theme.Text, AutoEllipsis = true };
            detail = new Label { ForeColor = Theme.Muted, AutoEllipsis = true };
            stateLabel = new Label { AutoEllipsis = true };
            Controls.Add(picture);
            Controls.Add(title);
            Controls.Add(detail);
            Controls.Add(stateLabel);
            Resize += delegate { LayoutChildren(); };
            Paint += delegate (object sender, PaintEventArgs e)
            {
                var rect = ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                using (var pen = new Pen(Theme.CardBorder))
                    e.Graphics.DrawRectangle(pen, rect);
            };
            LayoutChildren();
        }

        void LayoutChildren()
        {
            picture.SetBounds(10, 14, 112, 72);
            int left = 134;
            int width = Math.Max(40, ClientSize.Width - left - 12);
            title.SetBounds(left, 14, width, 22);
            detail.SetBounds(left, 38, width, 20);
            stateLabel.SetBounds(left, 62, width, 22);
        }

        public void Bind(MonitorInfo monitor, string activeText, Color stateColor, bool reloadThumb)
        {
            DevicePath = monitor.DevicePath;
            title.Text = monitor.Model;
            detail.Text = monitor.DetailLine;
            stateLabel.Text = activeText;
            stateLabel.ForeColor = stateColor;
            if (reloadThumb || !string.Equals(wallpaperPath, monitor.CurrentWallpaper, StringComparison.OrdinalIgnoreCase))
            {
                wallpaperPath = monitor.CurrentWallpaper;
                var old = picture.Image;
                picture.Image = Ui.LoadCover(monitor.CurrentWallpaper, 224, 144);
                if (old != null) old.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picture.Image != null)
            {
                picture.Image.Dispose();
                picture.Image = null;
            }
            base.Dispose(disposing);
        }
    }
}
