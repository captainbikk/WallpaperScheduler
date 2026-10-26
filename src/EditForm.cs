using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperScheduler
{
    sealed class Choice
    {
        public string Id;
        public string Text;
        public string Key;
        public override string ToString() { return Text; }
    }

    sealed class EditForm : Form
    {
        readonly ScheduleItem item;
        readonly List<MonitorInfo> monitors;
        readonly bool isNew;
        readonly TextBox nameBox;
        readonly TextBox pathBox;
        readonly PictureBox preview;
        readonly ComboBox monitorBox;
        readonly DateTimePicker datePicker;
        readonly DateTimePicker timePicker;
        readonly NumericUpDown lengthBox;
        readonly ComboBox unitBox;
        readonly CheckBox yearlyBox;
        readonly ComboBox fitBox;
        readonly CheckBox enabledBox;
        readonly Label summary;
        readonly Label previewNote;
        bool suppress;

        public EditForm(ScheduleItem schedule, List<MonitorInfo> monitors, bool isNew)
        {
            item = schedule;
            this.monitors = monitors ?? new List<MonitorInfo>();
            this.isNew = isNew;

            Text = isNew ? "Add schedule" : "Edit schedule";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Ui.BaseFont;
            BackColor = Color.White;
            ClientSize = new Size(560, 740);

            nameBox = new TextBox { Dock = DockStyle.Top };
            pathBox = new TextBox { Dock = DockStyle.Fill };
            var browse = Ui.Secondary("Browse…", 90);
            browse.Dock = DockStyle.Fill;
            browse.Click += delegate { Browse(); };

            var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, ColumnCount = 2 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            pathRow.Controls.Add(pathBox, 0, 0);
            pathRow.Controls.Add(browse, 1, 0);
            pathRow.Margin = new Padding(0, 0, 0, 8);

            preview = new PictureBox
            {
                Height = 150,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(232, 236, 240),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 0, 0, 4)
            };
            previewNote = new Label
            {
                AutoSize = false,
                Height = 18,
                Dock = DockStyle.Top,
                ForeColor = Theme.Muted,
                Text = ""
            };

            monitorBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Height = 28 };
            datePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dddd, MMMM d, yyyy",
                Width = 280,
                Margin = new Padding(0, 0, 8, 0)
            };
            timePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "h:mm tt",
                ShowUpDown = true,
                Width = 120
            };
            var whenRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            whenRow.Controls.Add(datePicker);
            whenRow.Controls.Add(timePicker);

            lengthBox = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 100000,
                Width = 90,
                Margin = new Padding(0, 0, 8, 0)
            };
            unitBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            var lengthRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            lengthRow.Controls.Add(lengthBox);
            lengthRow.Controls.Add(unitBox);

            summary = new Label
            {
                AutoSize = false,
                Height = 52,
                Dock = DockStyle.Top,
                BackColor = Theme.InfoBack,
                ForeColor = Theme.Header,
                Padding = new Padding(10, 4, 10, 4),
                TextAlign = ContentAlignment.MiddleLeft
            };

            yearlyBox = new CheckBox
            {
                Text = "Repeat every year on this month and day",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 4)
            };
            fitBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
            enabledBox = new CheckBox { Text = "Enabled", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };

            var fitNote = new Label
            {
                AutoSize = false,
                Height = 36,
                Dock = DockStyle.Top,
                ForeColor = Theme.Muted,
                Text = "Fill, fit, stretch, center, tile, and span are one setting for the whole desktop. It is put back when the schedule ends, unless the default wallpaper has its own fit."
            };

            preview.Dock = DockStyle.Fill;
            var previewHost = new Panel
            {
                Height = 120,
                MinimumSize = new Size(10, 120),
                MaximumSize = new Size(4000, 120)
            };
            previewHost.Controls.Add(preview);
            summary.Height = 52;
            summary.MinimumSize = new Size(10, 52);
            pathRow.MinimumSize = new Size(10, 32);
            whenRow.MinimumSize = new Size(10, 32);
            lengthRow.MinimumSize = new Size(10, 32);
            fitNote.MinimumSize = new Size(10, 36);

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                Padding = new Padding(16, 8, 16, 8)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(stack, Ui.Caption("Name"));
            AddRow(stack, nameBox);
            AddRow(stack, Ui.Caption("Picture"));
            AddRow(stack, pathRow);
            AddRow(stack, previewHost);
            AddRow(stack, previewNote);
            AddRow(stack, Ui.Caption("Monitor"));
            AddRow(stack, monitorBox);
            AddRow(stack, Ui.Caption("When it starts"));
            AddRow(stack, whenRow);
            AddRow(stack, Ui.Caption("How long it stays"));
            AddRow(stack, lengthRow);
            AddRow(stack, summary);
            AddRow(stack, yearlyBox);
            AddRow(stack, Ui.Caption("Wallpaper fit"));
            AddRow(stack, fitBox);
            AddRow(stack, fitNote);
            AddRow(stack, enabledBox);

            var save = Ui.Primary(isNew ? "Add schedule" : "Save", 120);
            var cancel = Ui.Secondary("Cancel", 90);
            save.Click += delegate { SaveAndClose(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save;
            CancelButton = cancel;

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.White };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Resize += delegate
            {
                save.Location = new Point(buttons.ClientSize.Width - 16 - save.Width, 12);
                cancel.Location = new Point(save.Left - 8 - cancel.Width, 12);
            };

            Controls.Add(stack);
            Controls.Add(buttons);

            nameBox.TextChanged += delegate { if (!suppress) RefreshSummary(); };
            pathBox.TextChanged += delegate { if (!suppress) ShowPreview(); RefreshSummary(); };
            monitorBox.SelectedIndexChanged += delegate { if (!suppress) RefreshSummary(); };
            datePicker.ValueChanged += delegate { if (!suppress) RefreshSummary(); };
            timePicker.ValueChanged += delegate { if (!suppress) RefreshSummary(); };
            lengthBox.ValueChanged += delegate { if (!suppress) RefreshSummary(); };
            unitBox.SelectedIndexChanged += delegate { if (!suppress) { ApplyUnitCap(); RefreshSummary(); } };
            yearlyBox.CheckedChanged += delegate { if (!suppress) RefreshSummary(); };
            fitBox.SelectedIndexChanged += delegate { if (!suppress) RefreshSummary(); };

            LoadChoices();
            LoadItem();
        }

        static void AddRow(TableLayoutPanel stack, Control control)
        {
            int row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 3, 0, 3);
            stack.Controls.Add(control, 0, row);
        }

        void LoadChoices()
        {
            monitorBox.Items.Clear();
            monitorBox.Items.Add(new Choice { Id = ScheduleItem.AllMonitors, Key = ScheduleItem.AllMonitors, Text = "All monitors" });
            foreach (var monitor in monitors)
            {
                monitorBox.Items.Add(new Choice
                {
                    Id = monitor.DevicePath,
                    Key = monitor.Key,
                    Text = monitor.ChoiceLabel
                });
            }

            unitBox.Items.Clear();
            unitBox.Items.Add(new Choice { Id = "minutes", Text = "Minutes" });
            unitBox.Items.Add(new Choice { Id = "hours", Text = "Hours" });
            unitBox.Items.Add(new Choice { Id = "days", Text = "Days" });
            unitBox.Items.Add(new Choice { Id = "weeks", Text = "Weeks" });

            fitBox.Items.Clear();
            fitBox.Items.Add(new Choice { Id = "keep", Text = "Keep the current Windows setting" });
            fitBox.Items.Add(new Choice { Id = "fill", Text = "Fill the screen" });
            fitBox.Items.Add(new Choice { Id = "fit", Text = "Fit the whole picture" });
            fitBox.Items.Add(new Choice { Id = "stretch", Text = "Stretch" });
            fitBox.Items.Add(new Choice { Id = "center", Text = "Center" });
            fitBox.Items.Add(new Choice { Id = "tile", Text = "Tile" });
            fitBox.Items.Add(new Choice { Id = "span", Text = "Span across monitors" });
        }

        void LoadItem()
        {
            suppress = true;
            nameBox.Text = item.Name ?? "";
            pathBox.Text = item.ImagePath ?? "";
            SelectChoice(monitorBox, string.IsNullOrEmpty(item.MonitorId) ? ScheduleItem.AllMonitors : item.MonitorId);
            if (monitorBox.SelectedIndex < 0 && !string.IsNullOrEmpty(item.MonitorKey))
            {
                for (int i = 0; i < monitorBox.Items.Count; i++)
                {
                    var choice = (Choice)monitorBox.Items[i];
                    if (string.Equals(choice.Key, item.MonitorKey, StringComparison.OrdinalIgnoreCase))
                    {
                        monitorBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (monitorBox.SelectedIndex < 0)
                monitorBox.SelectedIndex = 0;

            DateTime start;
            if (!item.TryGetStart(out start))
            {
                var now = DateTime.Now;
                start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            }
            datePicker.Value = start;
            timePicker.Value = start;

            string unit = (item.DurationUnit ?? "days").ToLowerInvariant();
            if (unit != "minutes" && unit != "hours" && unit != "days" && unit != "weeks")
                unit = "days";
            SelectChoice(unitBox, unit);
            ApplyUnitCap();
            decimal length = item.DurationValue < 1 ? 1 : item.DurationValue;
            if (length > lengthBox.Maximum) length = lengthBox.Maximum;
            lengthBox.Value = length;

            yearlyBox.Checked = item.RepeatYearly;
            SelectChoice(fitBox, string.IsNullOrEmpty(item.Fit) ? "fill" : item.Fit);
            if (fitBox.SelectedIndex < 0) fitBox.SelectedIndex = 1;
            enabledBox.Checked = item.Enabled;
            suppress = false;
            ShowPreview();
            RefreshSummary();
        }

        void ApplyUnitCap()
        {
            var choice = unitBox.SelectedItem as Choice;
            string unit = choice == null ? "days" : choice.Id;
            decimal max = 3650;
            if (unit == "minutes") max = 1000000;
            else if (unit == "hours") max = 24000;
            else if (unit == "weeks") max = 520;
            lengthBox.Maximum = max;
            if (lengthBox.Value > max) lengthBox.Value = max;
            if (lengthBox.Value < 1) lengthBox.Value = 1;
        }

        void SelectChoice(ComboBox box, string id)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                var choice = box.Items[i] as Choice;
                if (choice != null && string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
        }

        void Browse()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = PictureFiles.Filter;
                dialog.Title = "Choose a wallpaper";
                dialog.CheckFileExists = true;
                if (!string.IsNullOrWhiteSpace(pathBox.Text))
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(pathBox.Text);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                            dialog.InitialDirectory = folder;
                    }
                    catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dialog.FileName;
            }
        }

        void ShowPreview()
        {
            var old = preview.Image;
            string path = pathBox.Text.Trim();
            Bitmap bitmap = null;
            if (File.Exists(path))
                bitmap = Ui.LoadCover(path, 1024, 300);
            preview.Image = bitmap;
            if (old != null) old.Dispose();
            if (path.Length == 0)
                previewNote.Text = "";
            else if (!File.Exists(path))
                previewNote.Text = "That file was not found.";
            else if (bitmap == null)
                previewNote.Text = "Preview isn't available. Windows may still accept the file.";
            else
                previewNote.Text = Path.GetFileName(path);
        }

        ScheduleItem Draft()
        {
            var draft = item.Clone();
            draft.Name = nameBox.Text.Trim();
            draft.ImagePath = pathBox.Text.Trim();
            var monitor = monitorBox.SelectedItem as Choice;
            draft.MonitorId = monitor == null ? ScheduleItem.AllMonitors : monitor.Id;
            draft.MonitorKey = monitor == null ? ScheduleItem.AllMonitors : monitor.Key;
            draft.MonitorLabel = monitor == null ? "All monitors" : monitor.Text;
            var date = datePicker.Value;
            var time = timePicker.Value;
            var start = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Local);
            draft.Start = start.ToString("yyyy-MM-ddTHH:mm:ss");
            draft.DurationValue = (int)lengthBox.Value;
            var unit = unitBox.SelectedItem as Choice;
            draft.DurationUnit = unit == null ? "days" : unit.Id;
            draft.RepeatYearly = yearlyBox.Checked;
            var fit = fitBox.SelectedItem as Choice;
            draft.Fit = fit == null ? "fill" : fit.Id;
            draft.Enabled = enabledBox.Checked;
            if (string.IsNullOrWhiteSpace(draft.Name))
                draft.Name = NameFromPath(draft.ImagePath);
            return draft;
        }

        static string NameFromPath(string path)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(path ?? "");
                return string.IsNullOrWhiteSpace(name) ? "Wallpaper" : name;
            }
            catch { return "Wallpaper"; }
        }

        void RefreshSummary()
        {
            try { summary.Text = ScheduleMath.Describe(Draft(), DateTime.Now); }
            catch { summary.Text = "Choose a picture, a monitor, a date, and a length of time."; }
        }

        void SaveAndClose()
        {
            var draft = Draft();
            if (string.IsNullOrWhiteSpace(draft.ImagePath) || !File.Exists(draft.ImagePath))
            {
                MessageBox.Show(this, "Choose a picture that is still at that path.", "Wallpaper Scheduler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DateTime start;
            if (!draft.TryGetStart(out start))
            {
                MessageBox.Show(this, "Choose a start date.", "Wallpaper Scheduler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!PictureFiles.IsKnown(draft.ImagePath))
            {
                var use = MessageBox.Show(this,
                    "Windows usually wants a JPG, PNG, BMP, GIF, or TIFF.\r\n\r\nUse this file anyway?",
                    "Wallpaper Scheduler",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (use != DialogResult.Yes)
                    return;
            }
            DateTime end = start.AddMinutes(draft.DurationMinutes);
            if (!draft.RepeatYearly && end <= DateTime.Now)
            {
                var keep = MessageBox.Show(this,
                    "That date has already passed, so this wallpaper will not show.\r\n\r\nSave it anyway?",
                    "Wallpaper Scheduler",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (keep != DialogResult.Yes)
                    return;
            }

            item.Name = draft.Name;
            item.ImagePath = draft.ImagePath;
            item.MonitorId = draft.MonitorId;
            item.MonitorKey = draft.MonitorKey;
            item.MonitorLabel = draft.MonitorLabel;
            item.Start = draft.Start;
            item.DurationValue = draft.DurationValue;
            item.DurationUnit = draft.DurationUnit;
            item.RepeatYearly = draft.RepeatYearly;
            item.Fit = draft.Fit;
            item.Enabled = draft.Enabled;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
