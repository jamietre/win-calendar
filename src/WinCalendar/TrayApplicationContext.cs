using Microsoft.Toolkit.Uwp.Notifications;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WinCalendar;

// Double-buffered form to prevent flicker during updates
public class DoubleBufferedForm : Form
{
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }
}

public class TrayApplicationContext : ApplicationContext
{
    private const string Version = "1.1.1";

    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ReminderService _reminderService;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _statusMenuItem;
    private Form? _meetingsForm;
    private readonly Dictionary<string, bool> _initialCalendarSourceStates;

    public TrayApplicationContext()
    {
        _reminderService = new ReminderService();

        // Capture initial calendar source states
        _initialCalendarSourceStates = AppConfig.Instance.CalendarSources
            .ToDictionary(s => s.DisplayName, s => s.Enabled);

        // Create context menu
        _contextMenu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem("Status: Starting...");
        _statusMenuItem.Enabled = false;
        _contextMenu.Items.Add(_statusMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var checkNowItem = new ToolStripMenuItem("Check Now", null, (s, e) => CheckMeetingsManual());
        _contextMenu.Items.Add(checkNowItem);

        var testToastUpdateItem = new ToolStripMenuItem("Test Toast Update", null, (s, e) => ToastUpdateTest.RunTest());
        _contextMenu.Items.Add(testToastUpdateItem);

        var clearNotificationsItem = new ToolStripMenuItem("Clear Notifications", null, (s, e) => ClearNotifications());
        _contextMenu.Items.Add(clearNotificationsItem);

        var resetRemindersItem = new ToolStripMenuItem("Reset Reminders", null, (s, e) => ResetReminders());
        _contextMenu.Items.Add(resetRemindersItem);

        var openFolderItem = new ToolStripMenuItem("Open Data Folder", null, (s, e) => OpenDataFolder());
        _contextMenu.Items.Add(openFolderItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var showMeetingsItem = new ToolStripMenuItem("Show Meetings", null, (s, e) => ShowMeetingsDialog());
        _contextMenu.Items.Add(showMeetingsItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Exit());
        _contextMenu.Items.Add(exitItem);

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "WinCalendar"
        };

        _trayIcon.DoubleClick += (s, e) => ShowMeetingsDialog();

        // Set up timer for reminder checks (every 5 seconds)
        // This is cheap - just checks in-memory data against current time
        // Actual data refresh from sources happens at each source's configured refreshMinutes
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 5000
        };
        _timer.Tick += (s, e) => CheckMeetings();
        _timer.Start();

        // Initial check
        _reminderService.Log("=== Application started ===");
        CheckMeetings();


        // Handle toast activation (button clicks)
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            HandleToastAction(toastArgs.Argument);
        };
    }

    private Icon CreateTrayIcon()
    {
        // Create a simple calendar-like icon programmatically
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.DodgerBlue, 0, 0, 16, 4);
            g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
            g.FillRectangle(Brushes.DodgerBlue, 4, 7, 3, 3);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void CheckMeetings()
    {
        try
        {
            _reminderService.CheckAndNotify();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _reminderService.Log($"ERROR: {ex.Message}");
        }
    }

    private void CheckMeetingsManual()
    {
        CheckMeetings();

        // Show a test toast to confirm notifications are working
        var meetings = _reminderService.GetUpcomingMeetings();
        var message = meetings.Count == 0
            ? "No upcoming meetings in the next hour."
            : $"{meetings.Count} meeting{(meetings.Count == 1 ? "" : "s")} in the next hour.";

        new ToastContentBuilder()
            .AddText("Check Complete")
            .AddText(message)
            .AddAttributionText("Notification Test")
            .Show();

        _reminderService.Log($"Manual check: {message}");
    }

    private void UpdateStatus()
    {
        var meetings = _reminderService.GetUpcomingMeetings();
        var count = meetings.Count;
        _statusMenuItem.Text = $"Status: {count} upcoming meeting{(count == 1 ? "" : "s")}";
        _trayIcon.Text = $"WinCalendar - {count} upcoming";
    }

    private void ClearNotifications()
    {
        ToastNotificationManagerCompat.History.Clear();
        _trayIcon.ShowBalloonTip(2000, "Notifications Cleared", "All notifications have been cleared.", ToolTipIcon.Info);
    }

    private void ResetReminders()
    {
        var count = _reminderService.ResetUpcomingReminders();
        _trayIcon.ShowBalloonTip(2000, "Reminders Reset", $"Reset {count} upcoming meeting reminders.", ToolTipIcon.Info);
        CheckMeetings();
    }

    private void OpenDataFolder()
    {
        var folder = _reminderService.DataFolder;
        if (Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
    }

    private void ShowStatus()
    {
        var meetings = _reminderService.GetUpcomingMeetings();
        var message = meetings.Count == 0
            ? "No upcoming meetings in the next hour."
            : $"Upcoming meetings:\n\n" + string.Join("\n", meetings.Select(m => $"- {m.Subject} at {m.Start:HH:mm}"));

        MessageBox.Show(message, "WinCalendar Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowMeetingsDialog()
    {
        // If form already exists and is open, bring it to front
        if (_meetingsForm != null && !_meetingsForm.IsDisposed)
        {
            _meetingsForm.WindowState = FormWindowState.Normal;
            _meetingsForm.Activate();
            _meetingsForm.BringToFront();
            return;
        }

        var config = AppConfig.Instance;
        const int formWidth = 700;
        const int chromeHeight = 80; // title bar + menu + tabs

        var form = new DoubleBufferedForm
        {
            Text = $"Meetings - v{Version}",
            ClientSize = new Size(formWidth, 400),
            MinimumSize = new Size(formWidth + 16, 300),
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = true,
            FormBorderStyle = FormBorderStyle.Sizable,
            KeyPreview = true
        };

        // Restart required message label (initially hidden)
        var restartLabel = new Label
        {
            Text = "Restart required to update calendar feed",
            BackColor = Color.FromArgb(255, 220, 220),
            ForeColor = Color.DarkRed,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 30,
            Visible = HasCalendarSourcesChanged()
        };

        // Track the form and clear reference when closed
        _meetingsForm = form;
        form.FormClosed += (s, e) => _meetingsForm = null;

        // Menu strip
        var menuStrip = new MenuStrip();
        var optionsMenu = new ToolStripMenuItem("Options");

        // Sources submenu
        var sourcesMenu = new ToolStripMenuItem("Calendar Sources");
        foreach (var sourceConfig in config.CalendarSources)
        {
            var sourceItem = new ToolStripMenuItem(sourceConfig.DisplayName)
            {
                Checked = sourceConfig.Enabled,
                CheckOnClick = true,
                Tag = sourceConfig
            };
            sourceItem.CheckedChanged += (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is CalendarSourceConfig cfg)
                {
                    cfg.Enabled = item.Checked;
                    config.Save();
                    restartLabel.Visible = HasCalendarSourcesChanged();
                }
            };
            sourcesMenu.DropDownItems.Add(sourceItem);
        }
        optionsMenu.DropDownItems.Add(sourcesMenu);

        // Separator between sources and view options
        optionsMenu.DropDownItems.Add(new ToolStripSeparator());

        // View options
        var increaseFontItem = new ToolStripMenuItem("Increase Font Size", null, null, Keys.Control | Keys.Oemplus);
        var decreaseFontItem = new ToolStripMenuItem("Decrease Font Size", null, null, Keys.Control | Keys.OemMinus);
        var resetFontItem = new ToolStripMenuItem("Reset Font Size", null, null, Keys.Control | Keys.D0);

        optionsMenu.DropDownItems.Add(increaseFontItem);
        optionsMenu.DropDownItems.Add(decreaseFontItem);
        optionsMenu.DropDownItems.Add(new ToolStripSeparator());
        optionsMenu.DropDownItems.Add(resetFontItem);

        // Separator before Restart
        optionsMenu.DropDownItems.Add(new ToolStripSeparator());

        // Restart option
        var restartItem = new ToolStripMenuItem("Restart Application", null, (s, e) => Restart());
        optionsMenu.DropDownItems.Add(restartItem);

        menuStrip.Items.Add(optionsMenu);

        // Help menu
        var helpMenu = new ToolStripMenuItem("Help");
        var aboutItem = new ToolStripMenuItem("About", null, (s, e) =>
        {
            MessageBox.Show(
                $"WinCalendar\nVersion {Version}\n\nA Windows calendar reminder app with support for Outlook and Google Calendar.",
                "About WinCalendar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        helpMenu.DropDownItems.Add(aboutItem);
        menuStrip.Items.Add(helpMenu);

        form.MainMenuStrip = menuStrip;

        // Tab control
        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
            Padding = new Point(20, 6),
            ItemSize = new Size(120, 32),
            SizeMode = TabSizeMode.Fixed,
            Appearance = TabAppearance.Buttons
        };

        var todayTab = new TabPage("Today");
        var nextBusinessDayTab = new TabPage(GetNextBusinessDayLabel(DateTime.Today));
        tabControl.TabPages.Add(todayTab);
        tabControl.TabPages.Add(nextBusinessDayTab);

        // Add controls in docking order: TabControl, RestartLabel, MenuStrip (WinForms docks in reverse order)
        form.Controls.Add(tabControl);
        form.Controls.Add(restartLabel);
        form.Controls.Add(menuStrip);

        void RebuildTabs()
        {
            form.SuspendLayout();
            tabControl.SuspendLayout();
            try
            {
                BuildMeetingsPanel(todayTab, DateTime.Today, config, formWidth - 50);
                BuildMeetingsPanel(nextBusinessDayTab, GetNextBusinessDay(DateTime.Today), config, formWidth - 50);
            }
            finally
            {
                tabControl.ResumeLayout();
                form.ResumeLayout();
            }
        }

        int MeasureTabContentHeight(TabPage tab)
        {
            var flowPanel = tab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (flowPanel == null) return 150;

            // Calculate total height of all controls in the flow panel
            var height = flowPanel.Padding.Top + flowPanel.Padding.Bottom;
            foreach (Control ctrl in flowPanel.Controls)
            {
                height += ctrl.Height + ctrl.Margin.Top + ctrl.Margin.Bottom;
            }
            return Math.Max(height, 150);
        }

        void ResizeFormToFit()
        {
            // Measure actual rendered content
            var todayHeight = MeasureTabContentHeight(todayTab);
            var nextDayHeight = MeasureTabContentHeight(nextBusinessDayTab);
            var contentHeight = Math.Max(todayHeight, nextDayHeight);
            var maxHeight = (int)(Screen.PrimaryScreen!.WorkingArea.Height * 0.8);
            var newHeight = Math.Min(contentHeight + chromeHeight, maxHeight);
            form.ClientSize = new Size(formWidth, newHeight);
        }

        increaseFontItem.Click += (s, e) => { config.IncreaseFontSize(); RebuildTabs(); };
        decreaseFontItem.Click += (s, e) => { config.DecreaseFontSize(); RebuildTabs(); };
        resetFontItem.Click += (s, e) => { config.ResetFontSize(); RebuildTabs(); };

        form.KeyDown += (s, e) =>
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)
                {
                    config.IncreaseFontSize();
                    RebuildTabs();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)
                {
                    config.DecreaseFontSize();
                    RebuildTabs();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)
                {
                    config.ResetFontSize();
                    RebuildTabs();
                    e.Handled = true;
                }
            }
        };

        RebuildTabs();
        ResizeFormToFit();

        // Timer to update the dialog as time passes (check every second, rebuild on minute change)
        var updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        var lastUpdateDate = DateTime.Today;
        var lastUpdateMinute = DateTime.Now.Minute;

        updateTimer.Tick += (s, e) =>
        {
            var now = DateTime.Now;
            var currentDate = DateTime.Today;
            var currentMinute = now.Minute;

            // Only update if the minute has changed
            if (currentMinute == lastUpdateMinute && currentDate == lastUpdateDate)
                return;

            lastUpdateMinute = currentMinute;

            if (currentDate != lastUpdateDate)
            {
                // Day changed - rebuild tabs with fresh data
                lastUpdateDate = currentDate;
                RebuildTabs();
                ResizeFormToFit();

                // Update tab labels
                todayTab.Text = "Today";
                nextBusinessDayTab.Text = GetNextBusinessDayLabel(DateTime.Today);
            }
            else
            {
                // Same day - just rebuild to update time indicator
                RebuildTabs();
            }
        };
        updateTimer.Start();

        // Clean up timer when form is closed
        form.FormClosed += (s, e) =>
        {
            updateTimer.Stop();
            updateTimer.Dispose();
        };

        // Scroll to current time when form is shown
        form.Shown += (s, e) =>
        {
            ScrollToCurrentTime(todayTab);
        };

        form.Show();
    }

    private void BuildMeetingsPanel(TabPage tab, DateTime date, AppConfig config, int cardWidth)
    {
        tab.Controls.Clear();

        var meetings = _reminderService.GetMeetingsForDate(date);

        // Filter out hidden meetings for overlap detection
        var visibleMeetings = meetings
            .Where(m => !_reminderService.IsMeetingHidden(ReminderService.GetMeetingKey(m)))
            .ToList();

        // Find overlapping meetings (only among visible meetings)
        var overlappingMeetings = new HashSet<Meeting>();
        for (int i = 0; i < visibleMeetings.Count; i++)
        {
            for (int j = i + 1; j < visibleMeetings.Count; j++)
            {
                var iStart = new DateTime(visibleMeetings[i].Start.Year, visibleMeetings[i].Start.Month, visibleMeetings[i].Start.Day,
                                          visibleMeetings[i].Start.Hour, visibleMeetings[i].Start.Minute, 0);
                var iEnd = new DateTime(visibleMeetings[i].End.Year, visibleMeetings[i].End.Month, visibleMeetings[i].End.Day,
                                        visibleMeetings[i].End.Hour, visibleMeetings[i].End.Minute, 0);
                var jStart = new DateTime(visibleMeetings[j].Start.Year, visibleMeetings[j].Start.Month, visibleMeetings[j].Start.Day,
                                          visibleMeetings[j].Start.Hour, visibleMeetings[j].Start.Minute, 0);
                var jEnd = new DateTime(visibleMeetings[j].End.Year, visibleMeetings[j].End.Month, visibleMeetings[j].End.Day,
                                        visibleMeetings[j].End.Hour, visibleMeetings[j].End.Minute, 0);

                bool overlaps = iStart < jEnd && jStart < iEnd;
                if (overlaps)
                {
                    overlappingMeetings.Add(visibleMeetings[i]);
                    overlappingMeetings.Add(visibleMeetings[j]);
                }
            }
        }

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        // Enable double-buffering to prevent flicker
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?
            .SetValue(panel, true);

        if (meetings.Count == 0)
        {
            var noMeetingsLabel = new Label
            {
                Text = $"No meetings scheduled for {date:dddd, MMMM d}.",
                AutoSize = true,
                Font = config.GetFont(2),
                Padding = new Padding(5)
            };
            panel.Controls.Add(noMeetingsLabel);
        }
        else
        {
            var headerLabel = new Label
            {
                Text = $"{meetings.Count} meeting{(meetings.Count == 1 ? "" : "s")} on {date:dddd, MMMM d}",
                AutoSize = true,
                Font = config.GetFont(2, FontStyle.Bold),
                Padding = new Padding(5, 5, 5, 15)
            };
            panel.Controls.Add(headerLabel);

            var now = DateTime.Now;
            var isToday = date.Date == DateTime.Today;
            var timeLineAdded = false;

            // Colors for past meetings
            var pastBackColor = Color.FromArgb(240, 240, 240);
            var pastFontColor = Color.FromArgb(160, 160, 160);
            var pastOverlapBackColor = Color.FromArgb(250, 235, 235);

            // Colors for hidden/pending-delete meetings
            var hiddenBackColor = Color.FromArgb(220, 220, 220);
            var hiddenFontColor = Color.FromArgb(180, 180, 180);

            foreach (var meeting in meetings)
            {
                var meetingKey = ReminderService.GetMeetingKey(meeting);

                // Skip hidden meetings
                if (_reminderService.IsMeetingHidden(meetingKey))
                    continue;

                var isOverlapping = overlappingMeetings.Contains(meeting);
                var isPast = isToday && meeting.End <= now;
                var isInProgress = isToday && meeting.Start <= now && meeting.End > now;
                var isFuture = !isToday || meeting.Start > now;

                // Add time line before future meetings (if we haven't added it yet)
                var shouldMarkForScroll = false;
                if (isToday && !timeLineAdded && (isFuture || isInProgress))
                {
                    if (isFuture)
                    {
                        // Add time line between meetings with next meeting info
                        var timeLine = CreateTimeLine(cardWidth, now, meeting.Start);
                        timeLine.Tag = "CurrentTimeLine"; // Mark for scrolling
                        panel.Controls.Add(timeLine);
                        timeLineAdded = true;
                    }
                    else if (isInProgress)
                    {
                        // Mark this meeting for scrolling (set below after containerPanel is created)
                        shouldMarkForScroll = true;
                        timeLineAdded = true;
                    }
                }

                // Determine colors
                Color backColor, fontColor;
                if (isPast)
                {
                    backColor = isOverlapping ? pastOverlapBackColor : pastBackColor;
                    fontColor = pastFontColor;
                }
                else if (isOverlapping)
                {
                    backColor = Color.FromArgb(255, 230, 230);
                    fontColor = SystemColors.ControlText;
                }
                else
                {
                    backColor = SystemColors.ControlLight;
                    fontColor = SystemColors.ControlText;
                }

                // Create a container panel for the meeting card (to support time line overlay)
                var containerPanel = new Panel
                {
                    Width = cardWidth,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 12),
                    Tag = shouldMarkForScroll ? "CurrentTimeLine" : null
                };

                var meetingPanel = new TableLayoutPanel
                {
                    Width = cardWidth,
                    MinimumSize = new Size(cardWidth, 0),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowOnly,
                    ColumnCount = 1,
                    RowCount = isPast ? 2 : 4,
                    BackColor = backColor,
                    Margin = new Padding(0),
                    Padding = new Padding(12, isPast ? 6 : 10, 12, isPast ? 8 : 14)
                };

                // Trash/Undo button in top-right corner
                System.Windows.Forms.Timer? hideTimer = null;
                var isPendingHide = false;
                var trashButton = new Label
                {
                    Text = "🗑",
                    AutoSize = true,
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 12),
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0)
                };

                var originalBackColor = backColor;
                var originalFontColor = fontColor;

                trashButton.Click += (s, e) =>
                {
                    if (!isPendingHide)
                    {
                        // Start pending hide
                        isPendingHide = true;
                        trashButton.Text = "↩";
                        meetingPanel.BackColor = hiddenBackColor;
                        foreach (Control ctrl in meetingPanel.Controls)
                        {
                            if (ctrl is Label lbl && ctrl != trashButton)
                                lbl.ForeColor = hiddenFontColor;
                            if (ctrl is LinkLabel link)
                                link.LinkColor = hiddenFontColor;
                        }

                        // Start 5-second timer
                        hideTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                        hideTimer.Tick += (ts, te) =>
                        {
                            hideTimer.Stop();
                            hideTimer.Dispose();
                            if (isPendingHide)
                            {
                                _reminderService.HideMeeting(meetingKey);
                                containerPanel.Visible = false;
                            }
                        };
                        hideTimer.Start();
                    }
                    else
                    {
                        // Undo - cancel pending hide
                        isPendingHide = false;
                        hideTimer?.Stop();
                        hideTimer?.Dispose();
                        hideTimer = null;

                        trashButton.Text = "🗑";
                        meetingPanel.BackColor = originalBackColor;
                        foreach (Control ctrl in meetingPanel.Controls)
                        {
                            if (ctrl is Label lbl && ctrl != trashButton)
                                lbl.ForeColor = originalFontColor;
                            if (ctrl is LinkLabel link)
                                link.LinkColor = SystemColors.HotTrack;
                        }
                    }
                };

                var timeLabel = new Label
                {
                    Text = $"{meeting.Start:h:mm tt} - {meeting.End:h:mm tt}",
                    AutoSize = true,
                    Font = config.GetFont(1, FontStyle.Bold),
                    ForeColor = fontColor,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, 0, 6)
                };
                meetingPanel.Controls.Add(timeLabel);

                var subjectLabel = new Label
                {
                    Text = meeting.Subject,
                    AutoSize = true,
                    MaximumSize = new Size(cardWidth - 30, 0),
                    Font = config.GetFont(1),
                    ForeColor = fontColor,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, 0, isPast ? 0 : 4)
                };
                meetingPanel.Controls.Add(subjectLabel);

                // Only show organizer and links for non-past meetings
                if (!isPast)
                {
                    // Organizer and attendees info
                    var organizerText = meeting.Organizer ?? "Unknown";
                    if (meeting.TotalAttendees > 0)
                    {
                        organizerText += $" + {meeting.RequiredAttendees} required ({meeting.TotalAttendees} total)";
                    }
                    var organizerLabel = new Label
                    {
                        Text = organizerText,
                        AutoSize = true,
                        MaximumSize = new Size(cardWidth - 30, 0),
                        Font = config.GetFont(0),
                        ForeColor = Color.Gray,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0, 0, 0, 8)
                    };
                    meetingPanel.Controls.Add(organizerLabel);

                    var linksPanel = new FlowLayoutPanel
                    {
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        FlowDirection = FlowDirection.LeftToRight,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0, 0, 0, 0)
                    };

                    var meetingUrl = ReminderService.GetMeetingUrlPublic(meeting.Location);
                    if (meetingUrl != null)
                    {
                        var joinLink = new LinkLabel
                        {
                            Text = "Join Meeting",
                            AutoSize = true,
                            BackColor = Color.Transparent,
                            Font = config.GetFont(1),
                            Margin = new Padding(0, 0, 15, 0)
                        };
                        var url = meetingUrl;
                        joinLink.Click += (s, e) =>
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                        };
                        linksPanel.Controls.Add(joinLink);
                    }

                    if (!string.IsNullOrEmpty(meeting.EntryId))
                    {
                        var outlookLink = new LinkLabel
                        {
                            Text = "Open in Outlook",
                            AutoSize = true,
                            BackColor = Color.Transparent,
                            Font = config.GetFont(1)
                        };
                        var entryId = meeting.EntryId;
                        outlookLink.Click += (s, e) =>
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "outlook.exe",
                                    Arguments = $"/select \"outlook:{entryId}\"",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                _reminderService.Log($"Error opening Outlook: {ex.Message}");
                            }
                        };
                        linksPanel.Controls.Add(outlookLink);
                    }

                    meetingPanel.Controls.Add(linksPanel);
                }
                containerPanel.Controls.Add(meetingPanel);

                // Add trash button (positioned in top-right corner)
                trashButton.BackColor = backColor; // Match card background
                containerPanel.Controls.Add(trashButton);
                trashButton.BringToFront();

                // Position trash button when meeting panel is rendered
                meetingPanel.Resize += (s, e) =>
                {
                    trashButton.Location = new Point(meetingPanel.Width - trashButton.Width - 12, 8);
                };
                // Initial position
                trashButton.Location = new Point(cardWidth - 35, 8);

                // Add time line through in-progress meetings
                if (isInProgress)
                {
                    var duration = (meeting.End - meeting.Start).TotalMinutes;
                    var elapsed = (now - meeting.Start).TotalMinutes;
                    var progress = Math.Max(0, Math.Min(1, elapsed / duration));

                    // We need to add the line after the panel is sized, so use Paint event
                    meetingPanel.Paint += (s, e) =>
                    {
                        var yPos = (int)(meetingPanel.Height * progress);
                        using var pen = new Pen(Color.FromArgb(160, 82, 45), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                        e.Graphics.DrawLine(pen, 0, yPos, meetingPanel.Width, yPos);
                    };
                    timeLineAdded = true;
                }

                panel.Controls.Add(containerPanel);
            }

            // Add time line at the end if all meetings are past
            if (isToday && !timeLineAdded)
            {
                var timeLine = CreateTimeLine(cardWidth, now);
                panel.Controls.Add(timeLine);
            }
        }

        tab.Controls.Add(panel);
    }

    private Panel CreateTimeLine(int width, DateTime time, DateTime? nextMeetingStart = null)
    {
        var hasNextMeeting = nextMeetingStart.HasValue && nextMeetingStart.Value > time;
        var linePanel = new Panel
        {
            Width = width,
            Height = hasNextMeeting ? 38 : 20,
            Margin = new Padding(0, 4, 0, 4)
        };

        var lineColor = Color.FromArgb(160, 82, 45); // Sienna/reddish-brown
        var timeLabel = new Label
        {
            Text = time.ToString("h:mm tt"),
            AutoSize = true,
            ForeColor = lineColor,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8, FontStyle.Bold),
            Location = new Point(0, 2)
        };
        linePanel.Controls.Add(timeLabel);

        if (hasNextMeeting)
        {
            var timeUntil = nextMeetingStart!.Value - time;
            var nextMeetingLabel = new Label
            {
                Text = $"Next meeting in {FormatTimeSpan(timeUntil)}",
                AutoSize = true,
                ForeColor = lineColor,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 8, FontStyle.Italic),
                Location = new Point(0, 20)
            };
            linePanel.Controls.Add(nextMeetingLabel);
        }

        linePanel.Paint += (s, e) =>
        {
            var labelWidth = timeLabel.Width + 8;
            using var pen = new Pen(lineColor, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawLine(pen, labelWidth, 10, width, 10);
        };

        return linePanel;
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = span.Minutes;

        // 24+ hours: show days
        if (days >= 1)
        {
            if (hours == 0)
                return days == 1 ? "1 day" : $"{days} days";
            if (days == 1 && hours == 1)
                return "1 day and 1 hour";
            if (days == 1)
                return $"1 day and {hours} hours";
            if (hours == 1)
                return $"{days} days and 1 hour";
            return $"{days} days and {hours} hours";
        }

        // Less than 24 hours: show hours/minutes
        var totalHours = (int)span.TotalHours;
        if (totalHours == 0)
        {
            if (minutes == 0)
                return "<1 minute";
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        if (totalHours == 1 && minutes == 0)
            return "1 hour";
        if (totalHours == 1)
            return $"1 hour and {minutes} minute{(minutes == 1 ? "" : "s")}";
        if (minutes == 0)
            return $"{totalHours} hours";
        return $"{totalHours} hours and {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    private void ScrollToCurrentTime(TabPage tab)
    {
        // Find the FlowLayoutPanel in the tab
        var flowPanel = tab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (flowPanel == null) return;

        // Find the control marked with Tag="CurrentTimeLine"
        Control? targetControl = null;
        foreach (Control ctrl in flowPanel.Controls)
        {
            if (ctrl.Tag?.ToString() == "CurrentTimeLine")
            {
                targetControl = ctrl;
                break;
            }
        }

        if (targetControl == null) return;

        // Calculate position to center the target control in the view
        var targetY = targetControl.Top;
        var viewportHeight = flowPanel.ClientSize.Height;
        var scrollY = targetY - (viewportHeight / 2) + (targetControl.Height / 2);

        // Clamp to valid scroll range
        scrollY = Math.Max(0, scrollY);

        // Set scroll position (note: AutoScrollPosition uses negative values)
        flowPanel.AutoScrollPosition = new Point(0, scrollY);
    }

    private void HandleToastAction(string args)
    {
        try
        {
            if (string.IsNullOrEmpty(args)) return;

            _reminderService.Log($"Toast action: {args}");

            // Parse the action argument: "action=actionType/meetingKey[/extraData]"
            // The AddArgument method creates "key=value" format
            var actionValue = args;
            if (args.StartsWith("action="))
            {
                actionValue = args.Substring(7); // Remove "action=" prefix
            }

            var parts = actionValue.Split('/', 3);
            if (parts.Length < 2)
            {
                _reminderService.Log($"Toast action parse failed: not enough parts");
                return;
            }

            var action = parts[0];
            // Double-decode because the key gets double-encoded through the toast system
            var meetingKey = Uri.UnescapeDataString(Uri.UnescapeDataString(parts[1]));
            var extraData = parts.Length > 2 ? Uri.UnescapeDataString(Uri.UnescapeDataString(parts[2])) : null;

            _reminderService.Log($"Processing action: {action} for key: {meetingKey}");

            switch (action)
            {
                case "dismiss":
                    _reminderService.DismissMeeting(meetingKey);
                    break;
                case "skip":
                    _reminderService.SkipToStart(meetingKey);
                    break;
                case "snooze":
                    if (extraData != null)
                        _reminderService.SnoozeMeeting(meetingKey, extraData);
                    break;
                case "join":
                    _reminderService.DismissMeeting(meetingKey);
                    if (extraData != null)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(extraData) { UseShellExecute = true });
                    break;
                default:
                    _reminderService.Log($"Unknown action: {action}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _reminderService.Log($"ERROR in HandleToastAction: {ex.Message}");
        }
    }

    private static DateTime GetNextBusinessDay(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }

    private static string GetNextBusinessDayLabel(DateTime from)
    {
        var next = GetNextBusinessDay(from);
        var daysAhead = (next - from).Days;
        return daysAhead == 1 ? "Tomorrow" : next.DayOfWeek.ToString();
    }

    private bool HasCalendarSourcesChanged()
    {
        var currentSources = AppConfig.Instance.CalendarSources;

        // Check if any source's enabled state has changed
        foreach (var source in currentSources)
        {
            if (_initialCalendarSourceStates.TryGetValue(source.DisplayName, out var initialEnabled))
            {
                if (source.Enabled != initialEnabled)
                    return true;
            }
        }

        return false;
    }

    private void Restart()
    {
        _timer.Stop();
        _trayIcon.Visible = false;
        ToastNotificationManagerCompat.History.Clear();

        // Start a new instance after a short delay (to let mutex release)
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exePath))
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 1 /nobreak >nul && \"{exePath}\"",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }

        ToastNotificationManagerCompat.Uninstall();
        Application.Exit();
    }

    private void Exit()
    {
        _timer.Stop();
        _trayIcon.Visible = false;
        ToastNotificationManagerCompat.History.Clear();
        ToastNotificationManagerCompat.Uninstall();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
