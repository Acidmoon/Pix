using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>The floating ball. It never repaints or reshapes when the panel opens.</summary>
internal sealed class LauncherForm : Form
{
    private static readonly Size CollapsedSize = new(56, 56);
    private const int PanelGap = 12;
    private const int BrowserAnchorFromTop = 96;

    private readonly PiWebProcessManager service = new();
    private readonly BrowserApp browser = new();
    private readonly FloatingIconControl floatingIcon = new();
    private readonly PanelForm panel = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
    private readonly ContextMenuStrip contextMenu = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem trayStartItem = new("启动 Pi Web");
    private readonly ToolStripMenuItem trayOpenItem = new("打开界面");
    private readonly ToolStripMenuItem trayStopItem = new("停止 Pi Web");

    private readonly ValueAnimator fadeAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Easing.OutCubic,
    };
    private readonly MotionTicker followTicker = new();

    private Point followTarget;
    private double followPollAccumulator;
    private bool attachedToBrowser;
    private bool expanded;
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
        BackColor = Color.FromArgb(16, 19, 24);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        // Windows enforces SM_CXMINTRACK (~170px at 120 DPI) as the minimum
        // window width unless WinForms sees an explicit MinimumSize.
        MinimumSize = new Size(1, 1);
        Size = CollapsedSize;
        Icon = LoadAppIcon();
        Opacity = 0;

        floatingIcon.Dock = DockStyle.Fill;
        floatingIcon.ContextMenuStrip = contextMenu;
        Controls.Add(floatingIcon);

        BuildTrayIcon();
        WireEvents();
        WirePanel();
        RestoreInitialPosition();
        ApplyBallShape();
        panel.RenderStopped();
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

    private void WirePanel()
    {
        panel.Owner = this;
        panel.StartRequested += async () => await StartServiceAsync();
        panel.StopRequested += async () => await StopServiceAsync();
        panel.OpenRequested += () => OpenBrowser();
        panel.CollapseRequested += () => Collapse();
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

        trayIcon.Icon = Icon;
        trayIcon.Text = "Pix Launcher";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => ShowControlPanel();

        contextMenu.Items.Add("展开", null, (_, _) => ShowControlPanel());
        contextMenu.Items.Add("退出", null, async (_, _) => await ExitAsync());
    }

    private void WireEvents()
    {
        floatingIcon.MouseDown += OnIconMouseDown;
        floatingIcon.MouseMove += OnIconMouseMove;
        floatingIcon.MouseUp += OnIconMouseUp;
        refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        FormClosing += OnFormClosing;
        KeyDown += (_, eventArgs) => { if (eventArgs.KeyCode == Keys.Escape) Collapse(); };
        Shown += (_, _) => fadeAnimator.Start();

        fadeAnimator.Progressed += t => Opacity = t;
        followTicker.Tick += dt => FollowBrowserTick(dt);
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
        if (eventArgs.Button != MouseButtons.Left) return;
        pointerMoved = false;
        pointerDownScreen = Cursor.Position;
        formDownLocation = Location;
        floatingIcon.Capture = true;
    }

    private void OnIconMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!floatingIcon.Capture || eventArgs.Button != MouseButtons.Left) return;
        var delta = new Size(Cursor.Position.X - pointerDownScreen.X, Cursor.Position.Y - pointerDownScreen.Y);
        if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4 && !pointerMoved)
        {
            pointerMoved = true;
            // A deliberate drag releases the icon from the browser window edge.
            attachedToBrowser = false;
            // The panel stays behind when the ball travels.
            Collapse();
        }
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
            LauncherPlacement.Save(Location);
        }
    }

    private void Expand()
    {
        if (expanded || closing) return;
        expanded = true;
        panel.ShowAt(ComputePanelLocation());
        nextAccountRefresh = DateTimeOffset.MinValue;
        _ = RefreshStatusAsync();
    }

    private void Collapse()
    {
        if (!expanded) return;
        expanded = false;
        panel.HideAnimated();
    }

    private Point ComputePanelLocation()
    {
        var size = PanelForm.PanelSize;
        var area = Screen.FromPoint(new Point(Location.X + CollapsedSize.Width, Location.Y)).WorkingArea;
        var roomRight = area.Right - (Location.X + CollapsedSize.Width) - PanelGap;
        var x = roomRight >= size.Width
            ? Location.X + CollapsedSize.Width + PanelGap
            : Location.X - PanelGap - size.Width;
        x = Math.Clamp(x, area.Left + 8, Math.Max(area.Left + 8, area.Right - size.Width - 8));
        var y = Math.Clamp(Location.Y, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - size.Height - 8));
        return new Point(x, y);
    }

    /// <summary>Dock the floating icon to the browser window edge until the user drags it away.</summary>
    private void AttachToBrowser()
    {
        if (!browser.IsOpen) return;
        attachedToBrowser = true;
        followPollAccumulator = 1; // Poll the window bounds on the first tick.
        followTarget = Location;
        followTicker.EnsureRunning();
    }

    private void FollowBrowserTick(double dt)
    {
        if (!attachedToBrowser || closing)
        {
            followTicker.Stop();
            return;
        }
        // Manual dragging or an open panel pauses following.
        if (expanded || floatingIcon.Capture) return;

        followPollAccumulator += dt;
        if (followPollAccumulator >= 0.05)
        {
            followPollAccumulator = 0;
            if (!browser.TryGetWindowBounds(out var rect))
            {
                if (!browser.IsOpen) attachedToBrowser = false;
                return;
            }
            var targetY = Math.Clamp(
                rect.Top + BrowserAnchorFromTop - CollapsedSize.Height / 2,
                rect.Top + 4,
                Math.Max(rect.Top + 4, rect.Bottom - CollapsedSize.Height - 4));
            followTarget = new Point(rect.Right - CollapsedSize.Width / 2, targetY);
        }

        var nextLocation = new Point(
            (int)Math.Round(MotionStep.Approach(Location.X, followTarget.X, dt, 16)),
            (int)Math.Round(MotionStep.Approach(Location.Y, followTarget.Y, dt, 16)));
        if (nextLocation != Location) Location = nextLocation;
    }

    private async Task StartServiceAsync()
    {
        SetBusy(true);
        panel.RenderBusyStatus("正在启动…");
        floatingIcon.VisualState = FloatingIconControl.ServiceVisualState.Starting;
        try
        {
            await service.StartAsync();
            RenderRunning(null);
            OpenBrowser();
            AttachToBrowser();
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
        panel.RenderBusyStatus("正在停止…");
        attachedToBrowser = false;
        followTicker.Stop();
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
                panel.RenderAccounts(await service.GetLauncherStatusAsync());
            }
        }
        catch (Exception error)
        {
            panel.RenderSyncError(error.Message);
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
        panel.RenderRunning(health, service.ProcessId, service.Port);
        UpdateTrayCommands();
    }

    private void RenderStopped()
    {
        floatingIcon.VisualState = FloatingIconControl.ServiceVisualState.Stopped;
        panel.RenderStopped();
        UpdateTrayCommands();
    }

    private void SetBusy(bool value)
    {
        busy = value;
        UseWaitCursor = value;
        panel.SetBusy(value, service.IsRunning);
        UpdateTrayCommands();
    }

    private void UpdateTrayCommands()
    {
        trayStartItem.Enabled = !busy && !service.IsRunning;
        trayOpenItem.Enabled = !busy && service.IsRunning;
        trayStopItem.Enabled = !busy && service.IsRunning;
        trayIcon.Text = service.IsRunning ? "Pix Launcher - 运行中" : "Pix Launcher - 未启动";
    }

    internal static Icon LoadAppIcon()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("pix.ico", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return SystemIcons.Application;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        return new Icon(stream!);
    }

    private void KeepWindowOnScreen()
    {
        var area = Screen.FromPoint(Location).WorkingArea;
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

    private void ApplyBallShape()
    {
        using var path = new GraphicsPath();
        path.AddEllipse(ClientRectangle);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private async Task ExitAsync()
    {
        if (closing) return;
        closing = true;
        fadeAnimator.Stop();
        followTicker.Stop();
        refreshTimer.Stop();
        Enabled = false;
        if (!attachedToBrowser) LauncherPlacement.Save(Location);
        browser.Stop();
        await service.StopAsync();
        browser.Dispose();
        service.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        panel.Dispose();
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
            fadeAnimator.Dispose();
            followTicker.Dispose();
            refreshTimer.Dispose();
            panel.Dispose();
        }
        base.Dispose(disposing);
    }
}
