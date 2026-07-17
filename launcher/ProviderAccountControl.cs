using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>Paints one provider balance or quota summary as a dark instrument card.</summary>
internal sealed class ProviderAccountControl : Control
{
    private static readonly Color CardBg = Color.FromArgb(30, 33, 38);
    private static readonly Color CardBorder = Color.FromArgb(46, 50, 57);
    private static readonly Color Ink = Color.FromArgb(232, 234, 238);
    private static readonly Color Muted = Color.FromArgb(146, 152, 161);
    private static readonly Color Faint = Color.FromArgb(118, 124, 133);
    private static readonly Color Track = Color.FromArgb(45, 49, 56);
    private static readonly Color Signal = Color.FromArgb(48, 205, 141);
    private static readonly Color Warning = Color.FromArgb(228, 182, 92);
    private static readonly Color Danger = Color.FromArgb(224, 108, 100);
    private static readonly Color NotSet = Color.FromArgb(120, 126, 135);

    private static readonly Font TitleFont = new("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    private static readonly Font StatusFont = new("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
    private static readonly Font AmountFont = new("Bahnschrift SemiBold", 21F);
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
        Width = 320;
        Height = 118;
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
        Height = value.Tiers is { Count: > 1 } ? 150 : 118;
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
        using var surfacePath = PanelForm.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using var surface = new SolidBrush(CardBg);
        using var borderPen = new Pen(CardBorder);
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
            _ => NotSet,
        };
        using var titleBrush = new SolidBrush(Ink);
        graphics.DrawString(value.DisplayName, TitleFont, titleBrush, 16, 12);

        var statusText = value.Status switch
        {
            "available" => "已连接",
            "error" => "异常",
            _ => "未配置",
        };
        var statusSize = graphics.MeasureString(statusText, StatusFont);
        var pill = new RectangleF(
            Width - statusSize.Width - 16 - 14,
            12,
            statusSize.Width + 14,
            17);
        using var pillPath = PanelForm.RoundedRectangle(new Rectangle((int)pill.X, (int)pill.Y, (int)pill.Width, (int)pill.Height), 8);
        using var pillBrush = new SolidBrush(Color.FromArgb(26, statusColor));
        graphics.FillPath(pillBrush, pillPath);
        using var statusBrush = new SolidBrush(statusColor);
        graphics.DrawString(statusText, StatusFont, statusBrush, pill.X + 7, pill.Y + 2);
    }

    private static void DrawUnavailable(Graphics graphics, ProviderAccountSnapshot value)
    {
        using var messageBrush = new SolidBrush(value.Status == "error" ? Danger : Muted);
        graphics.DrawString(value.Message ?? "暂时无法查询", DetailFont, messageBrush, new RectangleF(16, 50, 292, 52));
    }

    private static void DrawBalance(Graphics graphics, ProviderAccountSnapshot value)
    {
        var balance = value.Balances![0];
        var symbol = balance.Currency switch { "CNY" => "¥", "USD" => "$", _ => $"{balance.Currency} " };
        using var amountBrush = new SolidBrush(Ink);
        graphics.DrawString($"{symbol}{balance.Total:N2}", AmountFont, amountBrush, 14, 38);

        var availability = value.IsAvailable == false ? "余额不足" : "可用余额";
        using var captionBrush = new SolidBrush(value.IsAvailable == false ? Danger : Signal);
        graphics.DrawString(availability, CaptionFont, captionBrush, 17, 84);

        using var detailBrush = new SolidBrush(Muted);
        graphics.DrawString($"充值 {symbol}{balance.ToppedUp:N2}   赠送 {symbol}{balance.Granted:N2}", DetailFont, detailBrush, 92, 84);
    }

    private void DrawQuota(Graphics graphics, ProviderAccountSnapshot value)
    {
        if (value.Tiers is not { Count: > 0 })
        {
            DrawUnavailable(graphics, value with { Status = "not_configured" });
            return;
        }

        var y = 44;
        var index = 0;
        foreach (var tier in value.Tiers.Take(2))
        {
            DrawQuotaTier(graphics, tier, displayedPercents[index], y);
            index++;
            y += 58;
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
        graphics.DrawString(tier.Label, TierLabelFont, labelBrush, 16, y);

        using var valueBrush = new SolidBrush(signalColor);
        var percentageText = $"{remaining:0.#}%";
        var valueSize = graphics.MeasureString(percentageText, TierValueFont);
        graphics.DrawString(percentageText, TierValueFont, valueBrush, Width - valueSize.Width - 16, y - 1);

        var track = new Rectangle(16, y + 21, Width - 32, 5);
        using var trackPath = PanelForm.RoundedRectangle(track, 2);
        using var trackBrush = new SolidBrush(Track);
        graphics.FillPath(trackBrush, trackPath);
        var fillWidth = (int)Math.Round(track.Width * remaining / 100D);
        if (fillWidth > 0)
        {
            using var fillPath = PanelForm.RoundedRectangle(new Rectangle(track.X, track.Y, Math.Max(5, fillWidth), track.Height), 2);
            using var fillBrush = new SolidBrush(signalColor);
            graphics.FillPath(fillBrush, fillPath);
        }

        if (DateTimeOffset.TryParse(tier.ResetsAt, out var resetAt))
        {
            using var resetBrush = new SolidBrush(Faint);
            graphics.DrawString($"重置 {resetAt.ToLocalTime():MM-dd HH:mm}", ResetTextFont, resetBrush, 16, y + 31);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ticker.Dispose();
        base.Dispose(disposing);
    }
}
