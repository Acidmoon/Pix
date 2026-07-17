using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>The satellite control panel drawn beside the floating icon on demand.</summary>
internal sealed class PanelForm : Form
{
    public static readonly Size PanelSize = new(380, 560);

    private static readonly Color PanelBg = Color.FromArgb(22, 24, 28);
    private static readonly Color Border = Color.FromArgb(46, 50, 57);
    private static readonly Color TextPrimary = Color.FromArgb(229, 231, 235);
    private static readonly Color TextSecondary = Color.FromArgb(152, 158, 167);
    private static readonly Color TextFaint = Color.FromArgb(107, 114, 124);
    private static readonly Color Accent = Color.FromArgb(48, 205, 141);
    private static readonly Color AccentHover = Color.FromArgb(66, 222, 160);
    private static readonly Color AccentPress = Color.FromArgb(38, 178, 124);
    private static readonly Color AccentInk = Color.FromArgb(10, 16, 13);
    private static readonly Color GhostHover = Color.FromArgb(33, 36, 42);
    private static readonly Color GhostPress = Color.FromArgb(28, 31, 36);
    private static readonly Color Amber = Color.FromArgb(228, 182, 92);
    private static readonly Color Danger = Color.FromArgb(224, 108, 100);

    private static readonly Font TitleFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold);
    private static readonly Font StatusFont = new("Microsoft YaHei UI", 8.5F);
    private static readonly Font StoppedTitleFont = new("Microsoft YaHei UI", 15F, FontStyle.Bold);
    private static readonly Font DescFont = new("Microsoft YaHei UI", 8.5F);
    private static readonly Font HintFont = new("Microsoft YaHei UI", 7.5F);
    private static readonly Font MetaFont = new("Bahnschrift", 8.5F);
    private static readonly Font BodyBoldFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
    private static readonly Font SectionFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private static readonly Font SyncFont = new("Microsoft YaHei UI", 7.5F);
    private static readonly Font ButtonFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);

    private readonly Panel statusDot = new();
    private readonly Label statusLabel = new();
    private readonly Label serviceMetaLabel = new();
    private readonly Label agentLabel = new();
    private readonly Label accountsUpdatedLabel = new();
    private readonly HiddenScrollFlowPanel accountsList = new();
    private readonly Panel stoppedPanel = new();
    private readonly Panel runningPanel = new();
    private readonly SmoothButton collapseButton = new();
    private readonly SmoothButton startButton = new();
    private readonly SmoothButton openButton = new();
    private readonly SmoothButton stopButton = new();

    private readonly ValueAnimator showAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(140),
        Ease = Easing.OutCubic,
    };
    private readonly ValueAnimator hideAnimator = new()
    {
        Duration = TimeSpan.FromMilliseconds(100),
        Ease = Easing.OutCubic,
    };

    private Point showTarget;
    private int showStartTop;
    private bool busy;
    private bool serviceRunning;
    private bool serviceOnline;

    public event Func<Task>? StartRequested;
    public event Action? OpenRequested;
    public event Func<Task>? StopRequested;
    public event Action? CollapseRequested;

    public PanelForm()
    {
        Text = "Pix Panel";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = PanelBg;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Size = PanelSize;
        Opacity = 0;

        BuildLayout();
        ApplyShape();

        showAnimator.Progressed += t =>
        {
            Opacity = t;
            Top = PaintLerp.Lerp(showStartTop, showTarget.Y, t);
        };
        hideAnimator.Progressed += t => Opacity = 1 - t;
        hideAnimator.Completed += () => base.Hide();

        collapseButton.Click += (_, _) => CollapseRequested?.Invoke();
        startButton.Click += async (_, _) => { if (StartRequested is not null) await StartRequested(); };
        openButton.Click += (_, _) => OpenRequested?.Invoke();
        stopButton.Click += async (_, _) => { if (StopRequested is not null) await StopRequested(); };
        KeyDown += (_, eventArgs) => { if (eventArgs.KeyCode == Keys.Escape) CollapseRequested?.Invoke(); };
    }

    /// <summary>Show the panel next to the icon with a short fade-and-slide.</summary>
    public void ShowAt(Point location)
    {
        if (Visible)
        {
            Location = location;
            return;
        }

        hideAnimator.Stop();
        showTarget = location;
        showStartTop = location.Y + 6;
        Opacity = 0;
        Location = new Point(location.X, showStartTop);
        base.Show();
        showAnimator.Start();
    }

    /// <summary>Fade the panel out; the floating icon is never touched.</summary>
    public void HideAnimated()
    {
        if (!Visible) return;
        showAnimator.Stop();
        hideAnimator.Start();
    }

    public void RenderStopped()
    {
        statusDot.BackColor = TextFaint;
        statusLabel.Text = "服务已停止";
        statusLabel.ForeColor = TextSecondary;
        stoppedPanel.Visible = true;
        runningPanel.Visible = false;
        serviceRunning = false;
        serviceOnline = false;
        ApplyEnabledStates();
    }

    public void RenderRunning(HealthSnapshot? health, int? processId, int? port)
    {
        statusDot.BackColor = health is null ? Amber : Accent;
        statusLabel.Text = health is null ? "正在连接…" : "运行中";
        statusLabel.ForeColor = health is null ? Amber : Accent;
        stoppedPanel.Visible = false;
        runningPanel.Visible = true;
        serviceMetaLabel.Text = $"PID {processId ?? 0} · PORT {port ?? 0}";
        agentLabel.Text = health is null
            ? "Agent 会话 · 检查中…"
            : health.AgentRunning ? $"Agent 会话 · 运行中 {health.RunningAgentCount}" : "Agent 会话 · 空闲";
        serviceRunning = true;
        serviceOnline = health is not null;
        ApplyEnabledStates();
    }

    public void SetBusy(bool value, bool running)
    {
        busy = value;
        serviceRunning = running;
        ApplyEnabledStates();
    }

    public void RenderAccounts(LauncherStatusSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            accountsUpdatedLabel.Text = "同步失败 · 自动重试";
            return;
        }

        accountsUpdatedLabel.Text = $"已同步 {DateTime.Now:HH:mm:ss}";
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

    public void RenderSyncError(string message)
    {
        accountsUpdatedLabel.Text = $"同步出错 · {message}";
    }

    /// <summary>Transient header note while the service is starting or stopping.</summary>
    public void RenderBusyStatus(string message)
    {
        statusDot.BackColor = Amber;
        statusLabel.Text = message;
        statusLabel.ForeColor = Amber;
    }

    private void ApplyEnabledStates()
    {
        startButton.SetInteractable(!busy && !serviceRunning);
        openButton.SetInteractable(!busy && serviceRunning && serviceOnline);
        stopButton.SetInteractable(!busy && serviceRunning);
    }

    private void BuildLayout()
    {
        var titleLabel = new Label
        {
            Text = "Pi Web",
            Font = TitleFont,
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(36, 14),
        };
        statusLabel.Font = StatusFont;
        statusLabel.ForeColor = TextSecondary;
        statusLabel.AutoSize = true;
        statusLabel.Location = new Point(37, 38);

        statusDot.Size = new Size(8, 8);
        statusDot.Location = new Point(20, 24);
        using (var dotPath = new GraphicsPath())
        {
            dotPath.AddEllipse(statusDot.ClientRectangle);
            statusDot.Region = new Region(dotPath);
        }

        ConfigureGhostButton(collapseButton, "收起", new Size(48, 26));
        collapseButton.Location = new Point(312, 16);
        collapseButton.Font = new Font("Microsoft YaHei UI", 8F);

        var headerLine = new Panel
        {
            BackColor = Border,
            Location = new Point(20, 64),
            Size = new Size(340, 1),
        };

        BuildStoppedPanel();
        BuildRunningPanel();
        Controls.AddRange([statusDot, titleLabel, statusLabel, collapseButton, headerLine, stoppedPanel, runningPanel]);
    }

    private void BuildStoppedPanel()
    {
        stoppedPanel.Location = new Point(20, 84);
        stoppedPanel.Size = new Size(340, 456);
        stoppedPanel.BackColor = PanelBg;

        var stoppedTitle = new Label
        {
            Text = "尚未启动",
            Font = StoppedTitleFont,
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(0, 18),
        };
        var description = new Label
        {
            Text = "本地 Pi Web 服务已停止。启动后会自动打开隔离的工作台窗口。",
            Font = DescFont,
            ForeColor = TextSecondary,
            AutoSize = true,
            Location = new Point(0, 50),
        };
        ConfigureAccentButton(startButton, "启动 Pi Web", new Size(340, 44));
        startButton.Location = new Point(0, 88);

        var hint = new Label
        {
            Text = "本地回环 · 独立浏览器配置 · 进程守护",
            Font = HintFont,
            ForeColor = TextFaint,
            AutoSize = true,
            Location = new Point(0, 146),
        };
        stoppedPanel.Controls.AddRange([stoppedTitle, description, startButton, hint]);
    }

    private void BuildRunningPanel()
    {
        runningPanel.Location = new Point(20, 80);
        runningPanel.Size = new Size(340, 460);
        runningPanel.BackColor = PanelBg;

        serviceMetaLabel.AutoSize = true;
        serviceMetaLabel.Location = new Point(0, 4);
        serviceMetaLabel.ForeColor = TextSecondary;
        serviceMetaLabel.Font = MetaFont;
        agentLabel.AutoSize = true;
        agentLabel.Location = new Point(0, 26);
        agentLabel.ForeColor = TextPrimary;
        agentLabel.Font = BodyBoldFont;

        ConfigureAccentButton(openButton, "打开界面", new Size(232, 38));
        openButton.Location = new Point(0, 56);
        ConfigureGhostButton(stopButton, "停止", new Size(98, 38));
        stopButton.Location = new Point(242, 56);
        stopButton.SetPalette(PanelBg, GhostHover, GhostPress, Danger, Danger, PanelBg, TextFaint);

        var separator = new Panel
        {
            BackColor = Border,
            Location = new Point(0, 114),
            Size = new Size(340, 1),
        };
        var accountsTitle = new Label
        {
            Text = "余额与额度",
            Font = SectionFont,
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(0, 130),
        };
        accountsUpdatedLabel.AutoSize = true;
        accountsUpdatedLabel.ForeColor = TextFaint;
        accountsUpdatedLabel.Font = SyncFont;
        accountsUpdatedLabel.Location = new Point(0, 156);

        accountsList.Location = new Point(0, 178);
        accountsList.Size = new Size(340, 282);
        accountsList.AutoScroll = true;
        accountsList.FlowDirection = FlowDirection.TopDown;
        accountsList.WrapContents = false;
        accountsList.Padding = Padding.Empty;
        accountsList.BackColor = PanelBg;

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

    private void ApplyShape()
    {
        const int radius = 14;
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 14);
        using var pen = new Pen(Border);
        graphics.DrawPath(pen, path);
    }

    private static void ConfigureAccentButton(SmoothButton button, string text, Size size)
    {
        button.Text = text;
        button.Size = size;
        button.Font = ButtonFont;
        button.CornerRadius = size.Height / 2;
        button.FlatAppearance.BorderSize = 0;
        button.SetPalette(
            Accent,
            AccentHover,
            AccentPress,
            AccentInk,
            AccentInk,
            PaintLerp.LerpColor(Accent, PanelBg, 0.72),
            PaintLerp.LerpColor(Accent, PanelBg, 0.45));
    }

    private static void ConfigureGhostButton(SmoothButton button, string text, Size size)
    {
        button.Text = text;
        button.Size = size;
        button.Font = ButtonFont;
        button.CornerRadius = size.Height / 2;
        button.FlatAppearance.BorderColor = Border;
        button.SetPalette(PanelBg, GhostHover, GhostPress, TextSecondary, TextPrimary, PanelBg, TextFaint);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            showAnimator.Dispose();
            hideAnimator.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
