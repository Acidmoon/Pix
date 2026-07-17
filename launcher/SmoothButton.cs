using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>Flat button whose hover and press colors ease instead of snapping.</summary>
internal sealed class SmoothButton : Button
{
    private readonly MotionTicker ticker;
    private double blend;
    private double target;
    private bool pressed;
    private bool interactable = true;

    private Color baseBack = Color.White;
    private Color hoverBack = Color.White;
    private Color pressBack = Color.White;
    private Color baseFore = Color.Black;
    private Color hoverFore = Color.Black;
    private Color disabledBack = Color.White;
    private Color disabledFore = Color.Black;

    public int CornerRadius { get; set; } = 9;

    public SmoothButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        Cursor = Cursors.Hand;
        ticker = new MotionTicker();
        ticker.Tick += dt => StepBlend(dt);
        MouseEnter += (_, _) => SetTarget(1);
        MouseLeave += (_, _) => SetTarget(0);
        MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left && interactable)
            {
                pressed = true;
                ApplyColors();
            }
        };
        MouseUp += (_, _) =>
        {
            pressed = false;
            ApplyColors();
        };
        EnabledChanged += (_, _) =>
        {
            pressed = false;
            target = 0;
            blend = 0;
            ticker.Stop();
            ApplyColors();
        };
    }

    public void SetPalette(
        Color normalBack,
        Color hoverBackColor,
        Color pressedBackColor,
        Color normalFore,
        Color hoverForeColor,
        Color? disabledBackColor = null,
        Color? disabledForeColor = null)
    {
        baseBack = normalBack;
        hoverBack = hoverBackColor;
        pressBack = pressedBackColor;
        baseFore = normalFore;
        hoverFore = hoverForeColor;
        disabledBack = disabledBackColor ?? PaintLerp.LerpColor(normalBack, normalFore, 0.06);
        disabledFore = disabledForeColor ?? PaintLerp.LerpColor(normalFore, normalBack, 0.55);
        ApplyColors();
    }

    /// <summary>
    /// WinForms paints disabled Flat buttons with the system GrayText color, which
    /// is unreadable on a dark surface. Keep the button enabled and emulate the
    /// disabled state with a muted palette plus click suppression instead.
    /// </summary>
    public void SetInteractable(bool value)
    {
        if (interactable == value) return;
        interactable = value;
        pressed = false;
        target = 0;
        blend = 0;
        ticker.Stop();
        Cursor = value ? Cursors.Hand : Cursors.Default;
        ApplyColors();
    }

    protected override void OnClick(EventArgs eventArgs)
    {
        if (!interactable) return;
        base.OnClick(eventArgs);
    }

    private void SetTarget(double value)
    {
        target = value;
        if (Math.Abs(blend - target) > 0.001) ticker.EnsureRunning();
    }

    private void StepBlend(double dt)
    {
        blend = MotionStep.Approach(blend, target, dt, 16);
        if (Math.Abs(blend - target) < 0.01)
        {
            blend = target;
            ticker.Stop();
        }
        ApplyColors();
    }

    private void ApplyColors()
    {
        if (!interactable)
        {
            if (BackColor != disabledBack) BackColor = disabledBack;
            if (ForeColor != disabledFore) ForeColor = disabledFore;
            return;
        }

        var back = PaintLerp.LerpColor(baseBack, hoverBack, blend);
        if (pressed) back = PaintLerp.LerpColor(back, pressBack, 0.85);
        if (BackColor != back) BackColor = back;
        var fore = PaintLerp.LerpColor(baseFore, hoverFore, blend);
        if (ForeColor != fore) ForeColor = fore;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        if (Width <= 0 || Height <= 0 || CornerRadius <= 0)
        {
            Region = null;
            return;
        }

        using var path = RoundedRectangle(new Rectangle(Point.Empty, Size), CornerRadius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
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
