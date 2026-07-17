using System.Drawing.Drawing2D;

namespace Pix.Launcher;

internal sealed class FloatingIconControl : Control
{
    public enum ServiceVisualState { Stopped, Starting, Running }

    private const float RestArcStart = -58F;
    private const float StoppedArcSweep = 64F;
    private const float RunningArcSweep = 126F;
    private const float SpinArcSweep = 70F;
    private const float SpinDegreesPerSecond = 260F;

    private static readonly Color ShellTopBase = Color.FromArgb(43, 48, 56);
    private static readonly Color ShellTopHover = Color.FromArgb(58, 64, 74);
    private static readonly Color ShellBottomBase = Color.FromArgb(16, 19, 24);
    private static readonly Color ShellBottomHover = Color.FromArgb(26, 30, 37);
    private static readonly Color SignalStopped = Color.FromArgb(122, 130, 140);
    private static readonly Color SignalStarting = Color.FromArgb(238, 190, 94);
    private static readonly Color SignalRunning = Color.FromArgb(74, 224, 158);
    private static readonly Font LetterFont = new("Bahnschrift SemiBold", 20F, FontStyle.Bold, GraphicsUnit.Pixel);

    private readonly MotionTicker ticker;
    private ServiceVisualState visualState;
    private double hoverBlend;
    private double hoverTarget;
    private double signalR = SignalStopped.R;
    private double signalG = SignalStopped.G;
    private double signalB = SignalStopped.B;
    private float arcStart = RestArcStart;
    private float arcSweep = StoppedArcSweep;

    public ServiceVisualState VisualState
    {
        get => visualState;
        set
        {
            if (visualState == value) return;
            visualState = value;
            ticker.EnsureRunning();
        }
    }

    public FloatingIconControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.SizeAll;
        Size = new Size(56, 56);
        ticker = new MotionTicker();
        ticker.Tick += dt => StepMotion(dt);
        MouseEnter += (_, _) => { hoverTarget = 1; ticker.EnsureRunning(); };
        MouseLeave += (_, _) => { hoverTarget = 0; ticker.EnsureRunning(); };
    }

    private Color SignalTarget => visualState switch
    {
        ServiceVisualState.Running => SignalRunning,
        ServiceVisualState.Starting => SignalStarting,
        _ => SignalStopped,
    };

    private float RestArcSweep => visualState == ServiceVisualState.Running ? RunningArcSweep : StoppedArcSweep;

    private void StepMotion(double dt)
    {
        const double rate = 14;
        hoverBlend = MotionStep.Approach(hoverBlend, hoverTarget, dt, rate);
        var target = SignalTarget;
        signalR = MotionStep.Approach(signalR, target.R, dt, rate);
        signalG = MotionStep.Approach(signalG, target.G, dt, rate);
        signalB = MotionStep.Approach(signalB, target.B, dt, rate);

        if (visualState == ServiceVisualState.Starting)
        {
            arcStart = (arcStart + (float)(dt * SpinDegreesPerSecond)) % 360F;
            arcSweep = MotionStep.Approach(arcSweep, SpinArcSweep, dt, rate);
        }
        else
        {
            // Normalize so the arc glides back along the shortest path.
            while (arcStart > 180F) arcStart -= 360F;
            while (arcStart < -180F) arcStart += 360F;
            arcStart = MotionStep.Approach(arcStart, RestArcStart, dt, rate);
            arcSweep = MotionStep.Approach(arcSweep, RestArcSweep, dt, rate);
        }

        if (IsSettled(target))
        {
            hoverBlend = hoverTarget;
            signalR = target.R;
            signalG = target.G;
            signalB = target.B;
            arcStart = RestArcStart;
            arcSweep = RestArcSweep;
            ticker.Stop();
        }
        Invalidate();
    }

    private bool IsSettled(Color target)
        => visualState != ServiceVisualState.Starting
            && Math.Abs(hoverBlend - hoverTarget) < 0.01
            && Math.Abs(signalR - target.R) < 1
            && Math.Abs(signalG - target.G) < 1
            && Math.Abs(signalB - target.B) < 1
            && Math.Abs(arcStart - RestArcStart) < 0.5
            && Math.Abs(arcSweep - RestArcSweep) < 0.5;

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Hover lifts the whole mark with a subtle scale.
        var scale = 1F + 0.05F * (float)hoverBlend;
        graphics.TranslateTransform(Width / 2F, Height / 2F);
        graphics.ScaleTransform(scale, scale);
        graphics.TranslateTransform(-Width / 2F, -Height / 2F);

        var outer = new Rectangle(3, 3, Width - 7, Height - 7);
        var inner = Rectangle.Inflate(outer, -4, -4);
        var signalColor = Color.FromArgb((int)signalR, (int)signalG, (int)signalB);

        // Layered soft shadow.
        using var shadowFar = new SolidBrush(Color.FromArgb(13, 12, 15, 20));
        graphics.FillEllipse(shadowFar, outer.X, outer.Y + 4, outer.Width, outer.Height);
        using var shadowNear = new SolidBrush(Color.FromArgb(26, 12, 15, 20));
        graphics.FillEllipse(shadowNear, outer.X + 1, outer.Y + 2, outer.Width - 1, outer.Height - 1);

        // Gradient shell with a specular sweep.
        using var shell = new LinearGradientBrush(
            outer,
            PaintLerp.LerpColor(ShellTopBase, ShellTopHover, hoverBlend),
            PaintLerp.LerpColor(ShellBottomBase, ShellBottomHover, hoverBlend),
            LinearGradientMode.Vertical);
        graphics.FillEllipse(shell, outer);
        using var specular = new Pen(Color.FromArgb(34, 255, 255, 255), 1.2F);
        graphics.DrawArc(specular, Rectangle.Inflate(outer, -2, -2), 200, 140);

        // Hairline track ring with the state arc on top.
        using var trackPen = new Pen(Color.FromArgb(20 + (int)(14 * hoverBlend), 255, 255, 255), 1F);
        graphics.DrawEllipse(trackPen, inner);
        using var signalPen = new Pen(signalColor, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(signalPen, inner, arcStart, arcSweep);

        using var textBrush = new SolidBrush(Color.FromArgb(230, 233, 237));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("P", LetterFont, textBrush, outer, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ticker.Dispose();
        base.Dispose(disposing);
    }
}
