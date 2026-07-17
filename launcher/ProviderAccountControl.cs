using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>Paints one provider balance or quota summary as a compact instrument panel.</summary>
internal sealed class ProviderAccountControl : Control
{
    private static readonly Color Ink = Color.FromArgb(24, 28, 33);
    private static readonly Color Muted = Color.FromArgb(101, 110, 120);
    private static readonly Color Border = Color.FromArgb(228, 231, 235);
    private static readonly Color Track = Color.FromArgb(234, 237, 240);
    private static readonly Color Signal = Color.FromArgb(34, 176, 108);
    private static readonly Color Warning = Color.FromArgb(214, 146, 42);
    private static readonly Color Danger = Color.FromArgb(199, 72, 65);

    private static readonly Font TitleFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private static readonly Font StatusFont = new("Bahnschrift", 7.5F, FontStyle.Bold);
    private static readonly Font AmountFont = new("Bahnschrift SemiBold", 22F);
    private static readonly Font CaptionFont = new("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
    private static readonly Font DetailFont = new("Microsoft YaHei UI", 8.5F);
    private static readonly Font TierLabelFont = new("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
    private static readonly Font TierValueFont = new("Bahnschrift SemiBold", 10F);
    private static readonly Font ResetTextFont = new("Microsoft YaHei UI", 7.5F);

    private readonly MotionTicker ticker;
    private readonly double[] displayedPercents = new double[2];
    private readonly double[] targetPercents = new double[2];
    private ProviderAccountSnapshot? snapshot;

    public ProviderAccountControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Width = 356;
        Height = 126;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Color.Transparent;
        ticker = new MotionTicker();
        ticker.Tick += dt => StepMotion(dt);
    }

    public void SetSnapshot(ProviderAccountSnapshot value)
    {
        snapshot = value;
        for (var index = 0; index < targetPercents.Length; index++)
        {
            targetPercents[index] = value.Tiers is { Count: > 0 } && index < value.Tiers.Count
                ? Math.Clamp(value.Tiers[index].RemainingPercent, 0, 100)
                : 0;
        }
        Height = value.Tiers is { Count: > 1 } ? 160 : 126;
        if (!IsSettled()) ticker.EnsureRunning();
        Invalidate();
    }

    private bool IsSettled()
    {
        for (var index = 0; index < targetPercents.Length; index++)
        {
            if (Math.Abs(displayedPercents[index] - targetPercents[index]) > 0.05) return false;
        }
        return true;
    }

    private void StepMotion(double dt)
    {
        for (var index = 0; index < targetPercents.Length; index++)
        {
            displayedPercents[index] = MotionStep.Approach(displayedPercents[index], targetPercents[index], dt, 9);
        }
        if (IsSettled())
        {
            Array.Copy(targetPercents, displayedPercents, targetPercents.Length);
            ticker.Stop();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var surfacePath = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using var surface = new SolidBrush(Color.White);
        using var borderPen = new Pen(Border);
        graphics.FillPath(surface, surfacePath);
        graphics.DrawPath(borderPen, surfacePath);

        if (snapshot is null) return;
        DrawHeader(graphics, snapshot);
        if (snapshot.Status != "available")
        {
            DrawUnavailable(graphics, snapshot);
        }
        else if (snapshot.Balances is { Count: > 0 })
        {
            DrawBalance(graphics, snapshot);
        }
        else
        {
            DrawQuota(graphics, snapshot);
        }
    }

    private void DrawHeader(Graphics graphics, ProviderAccountSnapshot value)
    {
        var statusColor = value.Status switch
        {
            "available" => Signal,
            "error" => Danger,
            _ => Color.FromArgb(158, 165, 174),
        };
        using var dotBrush = new SolidBrush(statusColor);
        graphics.FillEllipse(dotBrush, 18, 19, 7, 7);
        using var titleBrush = new SolidBrush(Ink);
        graphics.DrawString(value.DisplayName, TitleFont, titleBrush, 32, 12);

        var statusText = value.Status switch
        {
            "available" => "LIVE",
            "error" => "ERROR",
            _ => "NOT SET",
        };
        using var statusBrush = new SolidBrush(statusColor);
        var statusSize = graphics.MeasureString(statusText, StatusFont);
        graphics.DrawString(statusText, StatusFont, statusBrush, Width - statusSize.Width - 18, 14);
    }

    private static void DrawUnavailable(Graphics graphics, ProviderAccountSnapshot value)
    {
        using var messageBrush = new SolidBrush(value.Status == "error" ? Danger : Muted);
        graphics.DrawString(value.Message ?? "暂时无法查询", DetailFont, messageBrush, new RectangleF(18, 52, 320, 55));
    }

    private static void DrawBalance(Graphics graphics, ProviderAccountSnapshot value)
    {
        var balance = value.Balances![0];
        var symbol = balance.Currency switch { "CNY" => "¥", "USD" => "$", _ => $"{balance.Currency} " };
        using var amountBrush = new SolidBrush(Ink);
        graphics.DrawString($"{symbol}{balance.Total:N2}", AmountFont, amountBrush, 16, 42);

        var availability = value.IsAvailable == false ? "余额不足" : "可用余额";
        using var captionBrush = new SolidBrush(value.IsAvailable == false ? Danger : Signal);
        graphics.DrawString(availability, CaptionFont, captionBrush, 19, 91);

        using var detailBrush = new SolidBrush(Muted);
        graphics.DrawString($"充值 {symbol}{balance.ToppedUp:N2}   赠送 {symbol}{balance.Granted:N2}", DetailFont, detailBrush, 100, 91);
    }

    private void DrawQuota(Graphics graphics, ProviderAccountSnapshot value)
    {
        if (value.Tiers is not { Count: > 0 })
        {
            DrawUnavailable(graphics, value with { Status = "not_configured" });
            return;
        }

        var y = 48;
        var index = 0;
        foreach (var tier in value.Tiers.Take(2))
        {
            DrawQuotaTier(graphics, tier, displayedPercents[index], y);
            index++;
            y += 60;
        }
    }

    private void DrawQuotaTier(Graphics graphics, QuotaTierSnapshot tier, double remaining, int y)
    {
        var signalColor = remaining switch
        {
            > 50 => Signal,
            > 20 => Warning,
            _ => Danger,
        };
        using var labelBrush = new SolidBrush(Ink);
        graphics.DrawString(tier.Label, TierLabelFont, labelBrush, 18, y);

        using var valueBrush = new SolidBrush(signalColor);
        var percentageText = $"{remaining:0.#}%";
        var valueSize = graphics.MeasureString(percentageText, TierValueFont);
        graphics.DrawString(percentageText, TierValueFont, valueBrush, Width - valueSize.Width - 18, y - 1);

        var track = new Rectangle(18, y + 23, Width - 36, 6);
        using var trackPath = RoundedRectangle(track, 3);
        using var trackBrush = new SolidBrush(Track);
        graphics.FillPath(trackBrush, trackPath);
        var fillWidth = (int)Math.Round(track.Width * remaining / 100D);
        if (fillWidth > 0)
        {
            using var fillPath = RoundedRectangle(new Rectangle(track.X, track.Y, Math.Max(6, fillWidth), track.Height), 3);
            using var fillBrush = new SolidBrush(signalColor);
            graphics.FillPath(fillBrush, fillPath);
        }

        if (DateTimeOffset.TryParse(tier.ResetsAt, out var resetAt))
        {
            using var resetBrush = new SolidBrush(Muted);
            graphics.DrawString($"重置 {resetAt.ToLocalTime():MM-dd HH:mm}", ResetTextFont, resetBrush, 18, y + 34);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ticker.Dispose();
        base.Dispose(disposing);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
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
