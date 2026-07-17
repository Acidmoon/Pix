using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>Paints one provider balance or quota summary as a compact instrument panel.</summary>
internal sealed class ProviderAccountControl : Control
{
    private static readonly Color Ink = Color.FromArgb(27, 31, 36);
    private static readonly Color Muted = Color.FromArgb(105, 112, 121);
    private static readonly Color Border = Color.FromArgb(222, 226, 231);
    private static readonly Color Signal = Color.FromArgb(44, 190, 116);
    private static readonly Color Warning = Color.FromArgb(221, 151, 42);
    private static readonly Color Danger = Color.FromArgb(207, 74, 69);

    private ProviderAccountSnapshot? snapshot;

    public ProviderAccountControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Width = 356;
        Height = 126;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Color.Transparent;
    }

    public void SetSnapshot(ProviderAccountSnapshot value)
    {
        snapshot = value;
        Height = value.Tiers is { Count: > 1 } ? 160 : 126;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var surfacePath = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 8);
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

    private static void DrawHeader(Graphics graphics, ProviderAccountSnapshot value)
    {
        var statusColor = value.Status switch
        {
            "available" => Signal,
            "error" => Danger,
            _ => Color.FromArgb(158, 165, 174),
        };
        using var dotBrush = new SolidBrush(statusColor);
        graphics.FillEllipse(dotBrush, 17, 18, 7, 7);
        using var titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Ink);
        graphics.DrawString(value.DisplayName, titleFont, titleBrush, 31, 12);

        var statusText = value.Status switch
        {
            "available" => "LIVE",
            "error" => "ERROR",
            _ => "NOT SET",
        };
        using var statusFont = new Font("Bahnschrift", 8F, FontStyle.Bold);
        using var statusBrush = new SolidBrush(statusColor);
        var statusSize = graphics.MeasureString(statusText, statusFont);
        graphics.DrawString(statusText, statusFont, statusBrush, WidthFor(graphics) - statusSize.Width - 16, 13);
    }

    private static void DrawUnavailable(Graphics graphics, ProviderAccountSnapshot value)
    {
        using var messageFont = new Font("Microsoft YaHei UI", 9F);
        using var messageBrush = new SolidBrush(value.Status == "error" ? Danger : Muted);
        graphics.DrawString(value.Message ?? "暂时无法查询", messageFont, messageBrush, new RectangleF(17, 52, 320, 55));
    }

    private static void DrawBalance(Graphics graphics, ProviderAccountSnapshot value)
    {
        var balance = value.Balances![0];
        var symbol = balance.Currency switch { "CNY" => "¥", "USD" => "$", _ => $"{balance.Currency} " };
        using var amountFont = new Font("Bahnschrift SemiBold", 24F);
        using var amountBrush = new SolidBrush(Ink);
        graphics.DrawString($"{symbol}{balance.Total:N2}", amountFont, amountBrush, 15, 42);

        var availability = value.IsAvailable == false ? "余额不足" : "可用余额";
        using var captionFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        using var captionBrush = new SolidBrush(value.IsAvailable == false ? Danger : Signal);
        graphics.DrawString(availability, captionFont, captionBrush, 18, 91);

        using var detailFont = new Font("Microsoft YaHei UI", 8.5F);
        using var detailBrush = new SolidBrush(Muted);
        graphics.DrawString($"充值 {symbol}{balance.ToppedUp:N2}   赠送 {symbol}{balance.Granted:N2}", detailFont, detailBrush, 99, 91);
    }

    private static void DrawQuota(Graphics graphics, ProviderAccountSnapshot value)
    {
        if (value.Tiers is not { Count: > 0 })
        {
            DrawUnavailable(graphics, value with { Status = "not_configured" });
            return;
        }

        var y = 48;
        foreach (var tier in value.Tiers.Take(2))
        {
            DrawQuotaTier(graphics, tier, y);
            y += 60;
        }
    }

    private static void DrawQuotaTier(Graphics graphics, QuotaTierSnapshot tier, int y)
    {
        var remaining = Math.Clamp(tier.RemainingPercent, 0, 100);
        var signalColor = remaining switch
        {
            > 50 => Signal,
            > 20 => Warning,
            _ => Danger,
        };
        using var labelFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Ink);
        graphics.DrawString(tier.Label, labelFont, labelBrush, 17, y);

        using var valueFont = new Font("Bahnschrift SemiBold", 10F);
        using var valueBrush = new SolidBrush(signalColor);
        var percentageText = $"{remaining:0.#}%";
        var valueSize = graphics.MeasureString(percentageText, valueFont);
        graphics.DrawString(percentageText, valueFont, valueBrush, WidthFor(graphics) - valueSize.Width - 17, y - 1);

        var track = new Rectangle(17, y + 23, WidthFor(graphics) - 34, 6);
        using var trackPath = RoundedRectangle(track, 3);
        using var trackBrush = new SolidBrush(Color.FromArgb(233, 236, 239));
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
            using var resetFont = new Font("Microsoft YaHei UI", 7.5F);
            using var resetBrush = new SolidBrush(Muted);
            graphics.DrawString($"重置 {resetAt.ToLocalTime():MM-dd HH:mm}", resetFont, resetBrush, 17, y + 34);
        }
    }

    private static int WidthFor(Graphics graphics) => (int)graphics.VisibleClipBounds.Width;

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
