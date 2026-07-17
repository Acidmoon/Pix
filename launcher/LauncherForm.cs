using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Pix.Launcher;

internal sealed class LauncherForm : Form
{
    private static readonly Size CollapsedSize = new(64, 64);
    private static readonly Size ExpandedSize = new(420, 600);

    private static readonly Color Canvas = Color.FromArgb(245, 246, 248);
    private static readonly Color Ink = Color.FromArgb(24, 28, 33);
    private static readonly Color InkHover = Color.FromArgb(38, 44, 52);
    private static readonly Color InkPress = Color.FromArgb(12, 15, 19);
    private static readonly Color Muted = Color.FromArgb(101, 110, 120);
    private static readonly Color Faint = Color.FromArgb(154, 161, 170);
    private static readonly Color Hairline = Color.FromArgb(227, 230, 234);
    private static readonly Color ButtonHover = Color.FromArgb(239, 241, 244);
    private static readonly Color ButtonPress = Color.FromArgb(231, 234, 238);
    private static readonly Color ButtonBorder = Color.FromArgb(214, 219, 224);
    private static readonly Color Signal = Color.FromArgb(34, 176, 108);
    private static readonly Color Amber = Color.FromArgb(211, 141, 42);
    private static readonly Color Danger = Color.FromArgb(194, 68, 62);

    private static readonly Font TitleFont = new("Bahnschrift SemiBold", 19F, FontStyle.Bold);
    private static readonly Font StatusFont = new("Microsoft YaHei UI", 8.5F);
    private static readonly Font MicroFont = new("Bahnschrift", 8F, FontStyle.Bold);
    private static readonly Font HintFont = new("Bahnschrift", 7.5F);
    private static readonly Font PanelTitleFont = new("Microsoft YaHei UI", 16F, FontStyle.Bold);
    private static readonly Font MetaFont = new("Bahnschrift", 8.5F);
    private static readonly Font BodyBoldFont = new("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
    private static readonly Font SectionFont = new("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
    private static readonly Font SyncFont = new("Bahnschrift", 7.5F);
    private static readonly Font ButtonFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);

    private const int ContentSlideOffset = 10;

    private readonly PiWebProcessManager service = new();
    private readonly BrowserApp browser = new();
    private readonly FloatingIconControl floatingIcon = new();
    private readonly Panel contentPanel = new();
    private readonly Panel stoppedPanel = new();
    private readonly Panel runningPanel = new();
    private readonly Label titleLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label serviceMetaLabel = new();
    private readonly Label agentLabel = new();
    private readonly Label accountsUpdatedLabel = new();
    private readonly FlowLayoutPanel accountsList = new();
    private readonly SmoothButton collapseButton = new();
    private readonly SmoothButton startButton = new();
    private readonly SmoothButton openButton = new();
    private readonly SmoothButton stopButton = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
    private readonly ContextMenuStrip contextMenu = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem trayStartItem = new("启动 Pi Web");
    private readonly ToolStripMenuItem trayOpenItem = new("打开界面");
    private readonly ToolStripMenuItem trayStopItem = new("停止 Pi Web");

    private readonly ValueAnimator boundsAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(230),
        Ease = Easing.OutQuart,
    };
    private readonly ValueAnimator contentAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(150),
        Ease = Easing.OutCubic,
    };
    private readonly ValueAnimator fadeAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Easing.OutCubic,
    };

    private Rectangle boundsStart;
    private Rectangle boundsTarget;
    private Point iconAnchorScreen;
    private bool expanding;
    private bool expanded;
    private bool expandsLeft;
    private bool closing;
    private bool busy;
    private bool refreshInProgress;
    private bool pointerMoved;
    private Point pointerDownScreen;
    private Point formDownLocation;
    private DateTimeOffset nextAccountRefresh = DateTimeOffset.MinValue;

    public LauncherForm()
    {
        Text = "Pix Launcher";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Canvas;
        Font = new Font("Microsoft YaHei UI", 9F);
        KeyPreview = true;
        Size = CollapsedSize;
        Opacity = 0;

        BuildLayout();
        BuildTrayIcon();
        WireEvents();
        RestoreInitialPosition();
        ApplyWindowShape();
        RenderStopped();
        refreshTimer.Start();
    }

    public void ShowControlPanel()
    {
        if (closing || IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(ShowControlPanel); }
            catch (InvalidOperationException) { /* Window handle is being destroyed. */ }
            return;
        }

        Expand();
        Show();
        Activate();
        TopMost = false;
        TopMost = true;
    }

    private void BuildLayout()
    {
        floatingIcon.Location = new Point(4, 4);
        floatingIcon.ContextMenuStrip = contextMenu;
        Controls.Add(floatingIcon);

        contentPanel.Location = Point.Empty;
        contentPanel.Size = ExpandedSize;
        contentPanel.BackColor = Canvas;
        contentPanel.Visible = false;
        contentPanel.Controls.Add(new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(4, ExpandedSize.Height),
            BackColor = Color.FromArgb(30, 35, 41),
        });

        titleLabel.Text = "PIX";
        titleLabel.Font = TitleFont;
        titleLabel.ForeColor = Ink;
        titleLabel.AutoSize = true;
        statusLabel.AutoSize = true;
        statusLabel.ForeColor = Muted;
        statusLabel.Font = StatusFont;

        ConfigureButton(collapseButton, "收起", new Size(56, 30), primary: false);
        collapseButton.Click += (_, _) => Collapse();

        BuildStoppedPanel();
        BuildRunningPanel();
        contentPanel.Controls.AddRange([titleLabel, statusLabel, collapseButton, stoppedPanel, runningPanel]);
        Controls.Add(contentPanel);
        floatingIcon.BringToFront();

        contextMenu.Items.Add("展开", null, (_, _) => ShowControlPanel());
        contextMenu.Items.Add("退出", null, async (_, _) => await ExitAsync());
    }

    private void BuildStoppedPanel()
    {
        stoppedPanel.Location = new Point(24, 92);
        stoppedPanel.Size = new Size(372, 482);

        var stateCode = new Label
        {
            Text = "CONTROL PLANE · OFFLINE",
            Font = MicroFont,
            ForeColor = Faint,
            AutoSize = true,
            Location = new Point(0, 6),
        };
        var stoppedTitle = new Label
        {
            Text = "Pi Web 尚未启动",
            Font = PanelTitleFont,
            ForeColor = Ink,
            AutoSize = true,
            Location = new Point(-1, 34),
        };
        var readyLine = new Panel
        {
            Location = new Point(0, 78),
            Size = new Size(372, 1),
            BackColor = Hairline,
        };
        ConfigureButton(startButton, "启动 Pi Web", new Size(372, 46), primary: true);
        startButton.Location = new Point(0, 100);
        startButton.CornerRadius = 10;

        var footnote = new Label
        {
            Text = "LOCAL  ·  ISOLATED  ·  GUARDED",
            Font = HintFont,
            ForeColor = Faint,
            AutoSize = true,
            Location = new Point(0, 164),
        };
        stoppedPanel.Controls.AddRange([stateCode, stoppedTitle, readyLine, startButton, footnote]);
    }

    private void BuildRunningPanel()
    {
        runningPanel.Location = new Point(24, 88);
        runningPanel.Size = new Size(372, 496);

        serviceMetaLabel.AutoSize = true;
        serviceMetaLabel.Location = new Point(0, 2);
        serviceMetaLabel.ForeColor = Muted;
        serviceMetaLabel.Font = MetaFont;
        agentLabel.AutoSize = true;
        agentLabel.Location = new Point(0, 27);
        agentLabel.ForeColor = Ink;
        agentLabel.Font = BodyBoldFont;

        ConfigureButton(openButton, "打开界面", new Size(238, 36), primary: true);
        openButton.Location = new Point(0, 55);
        ConfigureButton(stopButton, "停止", new Size(124, 36), primary: false);
        stopButton.Location = new Point(248, 55);
        stopButton.SetPalette(Color.White, ButtonHover, ButtonPress, Danger, Danger);

        var separator = new Panel
        {
            BackColor = Hairline,
            Location = new Point(0, 110),
            Size = new Size(372, 1),
        };
        var accountsTitle = new Label
        {
            Text = "余额与额度",
            Font = SectionFont,
            ForeColor = Ink,
            AutoSize = true,
            Location = new Point(0, 127),
        };
        accountsUpdatedLabel.AutoSize = true;
        accountsUpdatedLabel.ForeColor = Muted;
        accountsUpdatedLabel.Font = SyncFont;
        accountsUpdatedLabel.Location = new Point(0, 158);

        accountsList.Location = new Point(0, 184);
        accountsList.Size = new Size(372, 309);
        accountsList.AutoScroll = true;
        accountsList.FlowDirection = FlowDirection.TopDown;
        accountsList.WrapContents = false;
        accountsList.Padding = Padding.Empty;

        runningPanel.Controls.AddRange([
            serviceMetaLabel,
            agentLabel,
            openButton,
            stopButton,
            separator,
            accountsTitle,
            accountsUpdatedLabel,
            accountsList,
        ]);
    }

    private void BuildTrayIcon()
    {
        trayMenu.Items.Add("打开控制面板", null, (_, _) => ShowControlPanel());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(trayStartItem);
        trayMenu.Items.Add(trayOpenItem);
        trayMenu.Items.Add(trayStopItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, async (_, _) => await ExitAsync());

        trayStartItem.Click += async (_, _) => { ShowControlPanel(); await StartServiceAsync(); };
        trayOpenItem.Click += (_, _) => OpenBrowser();
        trayStopItem.Click += async (_, _) => await StopServiceAsync();

        trayIcon.Icon = CreateTrayIcon();
        trayIcon.Text = "Pix Launcher";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => ShowControlPanel();
    }

    private void WireEvents()
    {
        floatingIcon.MouseDown += OnIconMouseDown;
        floatingIcon.MouseMove += OnIconMouseMove;
        floatingIcon.MouseUp += OnIconMouseUp;
        startButton.Click += async (_, _) => await StartServiceAsync();
        openButton.Click += (_, _) => OpenBrowser();
        stopButton.Click += async (_, _) => await StopServiceAsync();
        refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        FormClosing += OnFormClosing;
        Resize += (_, _) => ApplyWindowShape();
        KeyDown += (_, eventArgs) => { if (eventArgs.KeyCode == Keys.Escape) Collapse(); };
        Shown += (_, _) => fadeAnimator.Start();

        fadeAnimator.Progressed += t => Opacity = t;
        boundsAnimator.Progressed += OnBoundsProgress;
        boundsAnimator.Completed += OnBoundsCompleted;
        contentAnimator.Progressed += t => contentPanel.Top = PaintLerp.Lerp(ContentSlideOffset, 0, t);
    }

    private void RestoreInitialPosition()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var saved = LauncherPlacement.Load();
        Location = saved is Point point
            ? ClampCollapsedLocation(point)
            : new Point(workingArea.Right - CollapsedSize.Width - 24, workingArea.Top + (workingArea.Height - CollapsedSize.Height) / 2);
    }

    private void OnIconMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || boundsAnimator.IsRunning) return;
        pointerMoved = false;
        pointerDownScreen = Cursor.Position;
        formDownLocation = Location;
        floatingIcon.Capture = true;
    }

    private void OnIconMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!floatingIcon.Capture || eventArgs.Button != MouseButtons.Left || boundsAnimator.IsRunning) return;
        var delta = new Size(Cursor.Position.X - pointerDownScreen.X, Cursor.Position.Y - pointerDownScreen.Y);
        if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4) pointerMoved = true;
        Location = new Point(formDownLocation.X + delta.Width, formDownLocation.Y + delta.Height);
    }

    private void OnIconMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left) return;
        floatingIcon.Capture = false;
        if (!pointerMoved)
        {
            if (expanded) Collapse(); else Expand();
        }
        else
        {
            KeepWindowOnScreen();
            LauncherPlacement.Save(GetPersistedCollapsedLocation());
        }
    }

    private void Expand()
    {
        if (expanded || boundsAnimator.IsRunning) return;
        var iconScreenLocation = GetCollapsedIconScreenLocation();
        var area = Screen.FromPoint(iconScreenLocation).WorkingArea;
        var collapsedLeft = iconScreenLocation.X - 4;
        var collapsedRight = iconScreenLocation.X + 60;
        var roomRight = area.Right - collapsedLeft;
        var roomLeft = collapsedRight - area.Left;
        expandsLeft = roomRight < ExpandedSize.Width && roomLeft >= roomRight;

        expanding = true;
        expanded = true;
        iconAnchorScreen = iconScreenLocation;
        var iconOffset = expandsLeft ? new Point(ExpandedSize.Width - 60, 4) : new Point(4, 4);
        var targetLocation = new Point(iconAnchorScreen.X - iconOffset.X, iconAnchorScreen.Y - iconOffset.Y);
        targetLocation.X = Math.Clamp(targetLocation.X, area.Left, Math.Max(area.Left, area.Right - ExpandedSize.Width));
        targetLocation.Y = Math.Clamp(targetLocation.Y, area.Top, Math.Max(area.Top, area.Bottom - ExpandedSize.Height));
        boundsStart = Bounds;
        boundsTarget = new Rectangle(targetLocation, ExpandedSize);
        LayoutHeaderForDirection();
        contentPanel.Visible = false;
        boundsAnimator.Start();
    }

    private void Collapse()
    {
        if (!expanded || boundsAnimator.IsRunning) return;
        expanding = false;
        expanded = false;
        contentAnimator.Stop();
        contentPanel.Visible = false;
        iconAnchorScreen = PointToScreen(floatingIcon.Location);
        boundsStart = Bounds;
        boundsTarget = new Rectangle(new Point(iconAnchorScreen.X - 4, iconAnchorScreen.Y - 4), CollapsedSize);
        boundsAnimator.Start();
    }

    private void OnBoundsProgress(double t)
    {
        var x = PaintLerp.Lerp(boundsStart.X, boundsTarget.X, t);
        var y = PaintLerp.Lerp(boundsStart.Y, boundsTarget.Y, t);
        var width = PaintLerp.Lerp(boundsStart.Width, boundsTarget.Width, t);
        var height = PaintLerp.Lerp(boundsStart.Height, boundsTarget.Height, t);
        SetBounds(x, y, width, height);
        floatingIcon.Location = new Point(iconAnchorScreen.X - x, iconAnchorScreen.Y - y);
        if (expanding && !contentPanel.Visible && t >= 0.6)
        {
            ShowContentAnimated();
        }
    }

    private void OnBoundsCompleted()
    {
        SetBounds(boundsTarget.X, boundsTarget.Y, boundsTarget.Width, boundsTarget.Height);
        if (expanding)
        {
            if (!contentPanel.Visible) ShowContentAnimated();
            nextAccountRefresh = DateTimeOffset.MinValue;
            _ = RefreshStatusAsync();
        }
        else
        {
            floatingIcon.Location = new Point(4, 4);
            Location = ClampCollapsedLocation(boundsTarget.Location);
            LauncherPlacement.Save(Location);
        }
    }

    private void ShowContentAnimated()
    {
        contentPanel.Top = ContentSlideOffset;
        contentPanel.Visible = true;
        contentPanel.SendToBack();
        floatingIcon.BringToFront();
        contentAnimator.Start();
    }

    private void LayoutHeaderForDirection()
    {
        if (expandsLeft)
        {
            titleLabel.Location = new Point(24, 14);
            statusLabel.Location = new Point(26, 50);
            collapseButton.Location = new Point(282, 17);
        }
        else
        {
            titleLabel.Location = new Point(78, 14);
            statusLabel.Location = new Point(80, 50);
            collapseButton.Location = new Point(338, 17);
        }
        floatingIcon.BringToFront();
    }

    private async Task StartServiceAsync()
    {
        SetBusy(true);
        statusLabel.Text = "正在启动服务";
        floatingIcon.VisualState = FloatingIconControl.ServiceVisualState.Starting;
        try
        {
            await service.StartAsync();
            RenderRunning(null);
            OpenBrowser();
            nextAccountRefresh = DateTimeOffset.MinValue;
            await RefreshStatusAsync();
        }
        catch (Exception error)
        {
            RenderStopped();
            MessageBox.Show(this, error.Message, "Pix 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopServiceAsync()
    {
        SetBusy(true);
        statusLabel.Text = "正在停止服务";
        browser.Stop();
        await service.StopAsync();
        RenderStopped();
        SetBusy(false);
    }

    private void OpenBrowser()
    {
        if (service.Url is null) return;
        try { browser.Open(service.Url); }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "无法打开浏览器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (refreshInProgress || closing) return;
        refreshInProgress = true;
        try
        {
            if (!service.IsRunning)
            {
                RenderStopped();
                return;
            }

            var health = await service.GetHealthAsync();
            RenderRunning(health);
            if (expanded && health is not null && DateTimeOffset.UtcNow >= nextAccountRefresh)
            {
                nextAccountRefresh = DateTimeOffset.UtcNow.AddSeconds(60);
                RenderAccounts(await service.GetLauncherStatusAsync());
            }
        }
        catch (Exception error)
        {
            accountsUpdatedLabel.Text = $"SYNC ERROR · {error.Message}";
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    private void RenderRunning(HealthSnapshot? health)
    {
        floatingIcon.VisualState = health is null
            ? FloatingIconControl.ServiceVisualState.Starting
            : FloatingIconControl.ServiceVisualState.Running;
        statusLabel.Text = health is null ? "CONNECTING" : "SERVICE ONLINE";
        statusLabel.ForeColor = health is null ? Amber : Signal;
        stoppedPanel.Visible = false;
        runningPanel.Visible = true;
        serviceMetaLabel.Text = $"PID {service.ProcessId ?? 0}   PORT {service.Port ?? 0}";
        agentLabel.Text = health is null
            ? "Agent 状态  ·  检查中"
            : health.AgentRunning ? $"Agent 状态  ·  运行中 {health.RunningAgentCount}" : "Agent 状态  ·  空闲";
        openButton.Enabled = !busy && health is not null;
        stopButton.Enabled = !busy;
        startButton.Enabled = false;
        UpdateTrayCommands();
    }

    private void RenderStopped()
    {
        floatingIcon.VisualState = FloatingIconControl.ServiceVisualState.Stopped;
        statusLabel.Text = "SERVICE OFFLINE";
        statusLabel.ForeColor = Muted;
        stoppedPanel.Visible = true;
        runningPanel.Visible = false;
        startButton.Enabled = !busy;
        openButton.Enabled = false;
        stopButton.Enabled = false;
        UpdateTrayCommands();
    }

    private void RenderAccounts(LauncherStatusSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            accountsUpdatedLabel.Text = "SYNC FAILED · 自动重试";
            return;
        }

        accountsUpdatedLabel.Text = $"SYNC {DateTime.Now:HH:mm:ss}";
        accountsList.SuspendLayout();
        var reused = accountsList.Controls.OfType<ProviderAccountControl>()
            .ToDictionary(control => (string)control.Tag!);
        var ordered = new List<ProviderAccountControl>(snapshot.Providers.Count);
        foreach (var provider in snapshot.Providers)
        {
            if (!reused.TryGetValue(provider.Id, out var control))
            {
                control = new ProviderAccountControl { Tag = provider.Id };
            }
            else
            {
                reused.Remove(provider.Id);
            }
            control.SetSnapshot(provider);
            ordered.Add(control);
        }
        foreach (var leftover in reused.Values) leftover.Dispose();
        accountsList.Controls.Clear();
        accountsList.Controls.AddRange(ordered.ToArray());
        accountsList.ResumeLayout();
    }

    private void SetBusy(bool value)
    {
        busy = value;
        UseWaitCursor = value;
        startButton.Enabled = !value && !service.IsRunning;
        openButton.Enabled = !value && service.IsRunning;
        stopButton.Enabled = !value && service.IsRunning;
        UpdateTrayCommands();
    }

    private void UpdateTrayCommands()
    {
        trayStartItem.Enabled = !busy && !service.IsRunning;
        trayOpenItem.Enabled = !busy && service.IsRunning;
        trayStopItem.Enabled = !busy && service.IsRunning;
        trayIcon.Text = service.IsRunning ? "Pix Launcher - 运行中" : "Pix Launcher - 未启动";
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(24, 28, 33));
            graphics.FillEllipse(background, 1, 1, 30, 30);
            using var signal = new SolidBrush(Color.FromArgb(67, 229, 143));
            graphics.FillEllipse(signal, 23, 23, 6, 6);
            using var font = new Font("Bahnschrift SemiBold", 17F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var foreground = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("P", font, foreground, new RectangleF(1, 0, 30, 30), format);
        }

        var iconHandle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(iconHandle).Clone(); }
        finally { DestroyIcon(iconHandle); }
    }

    private Point GetCollapsedIconScreenLocation() => expanded
        ? PointToScreen(floatingIcon.Location)
        : new Point(Location.X + 4, Location.Y + 4);

    private Point GetPersistedCollapsedLocation()
    {
        var iconLocation = GetCollapsedIconScreenLocation();
        return ClampCollapsedLocation(new Point(iconLocation.X - 4, iconLocation.Y - 4));
    }

    private void KeepWindowOnScreen()
    {
        var area = Screen.FromPoint(GetCollapsedIconScreenLocation()).WorkingArea;
        Location = new Point(
            Math.Clamp(Location.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    private static Point ClampCollapsedLocation(Point point)
    {
        var area = Screen.FromPoint(point).WorkingArea;
        return new Point(
            Math.Clamp(point.X, area.Left, area.Right - CollapsedSize.Width),
            Math.Clamp(point.Y, area.Top, area.Bottom - CollapsedSize.Height));
    }

    private void ApplyWindowShape()
    {
        var t = Math.Clamp(
            (Width - CollapsedSize.Width) / (double)(ExpandedSize.Width - CollapsedSize.Width), 0, 1);
        using var path = new GraphicsPath();
        if (t <= 0.001)
        {
            path.AddEllipse(ClientRectangle);
        }
        else
        {
            var radius = PaintLerp.Lerp(Math.Min(Width, Height) / 2, 13, t);
            var diameter = Math.Min(radius * 2, Math.Min(Width, Height) - 1);
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
            path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
        }

        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private static void ConfigureButton(SmoothButton button, string text, Size size, bool primary)
    {
        button.Text = text;
        button.Size = size;
        button.Font = ButtonFont;
        if (primary)
        {
            button.FlatAppearance.BorderSize = 0;
            button.SetPalette(Ink, InkHover, InkPress, Color.White, Color.White);
        }
        else
        {
            button.FlatAppearance.BorderColor = ButtonBorder;
            button.SetPalette(Color.White, ButtonHover, ButtonPress, Color.FromArgb(70, 77, 87), Ink);
        }
    }

    private async Task ExitAsync()
    {
        if (closing) return;
        closing = true;
        boundsAnimator.Stop();
        contentAnimator.Stop();
        fadeAnimator.Stop();
        refreshTimer.Stop();
        Enabled = false;
        LauncherPlacement.Save(GetPersistedCollapsedLocation());
        browser.Stop();
        await service.StopAsync();
        browser.Dispose();
        service.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        FormClosing -= OnFormClosing;
        Close();
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (closing) return;
        eventArgs.Cancel = true;
        await ExitAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundsAnimator.Dispose();
            contentAnimator.Dispose();
            fadeAnimator.Dispose();
            refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
