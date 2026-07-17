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

    private static readonly Color ShellBase = Color.FromArgb(23, 27, 32);
    private static readonly Color ShellHover = Color.FromArgb(39, 45, 53);
    private static readonly Color RingColor = Color.FromArgb(84, 93, 104);
    private static readonly Color SignalStopped = Color.FromArgb(148, 157, 167);
    private static readonly Color SignalStarting = Color.FromArgb(240, 176, 66);
    private static readonly Color SignalRunning = Color.FromArgb(63, 224, 146);
    private static readonly Font LetterFont = new("Bahnschrift SemiBold", 22F, FontStyle.Bold, GraphicsUnit.Pixel);

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
        var outer = new Rectangle(1, 1, Width - 3, Height - 3);
        var inner = Rectangle.Inflate(outer, -4, -4);
        var signalColor = Color.FromArgb((int)signalR, (int)signalG, (int)signalB);

        using var shadow = new SolidBrush(Color.FromArgb(34, 0, 0, 0));
        graphics.FillEllipse(shadow, outer.X + 1, outer.Y + 2, outer.Width, outer.Height);
        using var shell = new SolidBrush(PaintLerp.LerpColor(ShellBase, ShellHover, hoverBlend));
        graphics.FillEllipse(shell, outer);
        using var ringPen = new Pen(RingColor, 1F);
        graphics.DrawEllipse(ringPen, inner);

        using var signalPen = new Pen(signalColor, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(signalPen, inner, arcStart, arcSweep);

        using var textBrush = new SolidBrush(Color.FromArgb(244, 246, 248));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("P", LetterFont, textBrush, outer, format);

        using var stateBrush = new SolidBrush(signalColor);
        graphics.FillEllipse(stateBrush, Width - 14, Height - 14, 7, 7);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ticker.Dispose();
        base.Dispose(disposing);
    }
}
