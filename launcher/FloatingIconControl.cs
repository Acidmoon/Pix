using System.Drawing.Drawing2D;

namespace Pix.Launcher;

/// <summary>
/// 悬浮球：一枚深色玻璃质感的轨道球。
/// 视觉层级：投影 → 径向渐变球体（光源在左上）→ 前光高光 → 底部轮廓光
/// → 状态光环 → π 字符。状态表达：停止=灰环，运行=绿环带呼吸微光，
/// 启动中=琥珀色旋转弧。
/// </summary>
internal sealed class FloatingIconControl : Control
{
    public enum ServiceVisualState { Stopped, Starting, Running }

    private const float SpinArcSweep = 80F;
    private const float SpinDegreesPerSecond = 240F;
    private const double PulseRadiansPerSecond = Math.PI * 2 / 2.6; // 运行态呼吸周期 2.6s

    private static readonly Color OrbCenterBase = Color.FromArgb(63, 71, 84);
    private static readonly Color OrbCenterHover = Color.FromArgb(80, 90, 105);
    private static readonly Color OrbEdgeBase = Color.FromArgb(19, 22, 28);
    private static readonly Color OrbEdgeHover = Color.FromArgb(29, 33, 41);
    private static readonly Color SignalStopped = Color.FromArgb(128, 136, 148);
    private static readonly Color SignalStarting = Color.FromArgb(240, 194, 96);
    private static readonly Color SignalRunning = Color.FromArgb(78, 226, 160);
    private static readonly Font PiFont = new("Bahnschrift SemiBold", 22F, FontStyle.Bold, GraphicsUnit.Pixel);

    private readonly MotionTicker ticker;
    private ServiceVisualState visualState;
    private double hoverBlend;
    private double hoverTarget;
    private double signalR = SignalStopped.R;
    private double signalG = SignalStopped.G;
    private double signalB = SignalStopped.B;
    private float spinStart = -90F;
    private double pulsePhase;

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
            spinStart = (spinStart + (float)(dt * SpinDegreesPerSecond)) % 360F;
        }
        else if (visualState == ServiceVisualState.Running)
        {
            pulsePhase = (pulsePhase + dt * PulseRadiansPerSecond) % (Math.PI * 2);
        }

        // 只有 Stopped 状态可以在静止后停掉动画时钟；
        // Running 要维持呼吸，Starting 要维持旋转
        if (visualState == ServiceVisualState.Stopped && IsSettled(target))
        {
            hoverBlend = hoverTarget;
            signalR = target.R;
            signalG = target.G;
            signalB = target.B;
            ticker.Stop();
        }
        Invalidate();
    }

    private bool IsSettled(Color target)
        => Math.Abs(hoverBlend - hoverTarget) < 0.01
            && Math.Abs(signalR - target.R) < 1
            && Math.Abs(signalG - target.G) < 1
            && Math.Abs(signalB - target.B) < 1;

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // 悬停轻微放大
        var scale = 1F + 0.05F * (float)hoverBlend;
        graphics.TranslateTransform(Width / 2F, Height / 2F);
        graphics.ScaleTransform(scale, scale);
        graphics.TranslateTransform(-Width / 2F, -Height / 2F);

        var outer = new Rectangle(4, 4, Width - 9, Height - 9);
        var ring = Rectangle.Inflate(outer, -7, -7);
        var signalColor = Color.FromArgb((int)signalR, (int)signalG, (int)signalB);

        DrawShadow(graphics, outer);
        DrawOrb(graphics, outer);
        DrawSignal(graphics, ring, signalColor);
        DrawPi(graphics, outer);
    }

    /// <summary>两层柔和投影，让球体浮在桌面上。</summary>
    private static void DrawShadow(Graphics graphics, Rectangle outer)
    {
        using var far = new SolidBrush(Color.FromArgb(14, 10, 12, 18));
        graphics.FillEllipse(far, outer.X, outer.Y + 5, outer.Width, outer.Height);
        using var near = new SolidBrush(Color.FromArgb(28, 10, 12, 18));
        graphics.FillEllipse(near, outer.X + 1, outer.Y + 2, outer.Width - 2, outer.Height - 1);
    }

    /// <summary>径向渐变球体 + 左上前光高光 + 底部轮廓光。</summary>
    private void DrawOrb(Graphics graphics, Rectangle outer)
    {
        // 球体：PathGradient 中心亮、边缘暗，光源点偏向左上
        using var path = new GraphicsPath();
        path.AddEllipse(outer);
        using var orb = new PathGradientBrush(path);
        orb.CenterColor = PaintLerp.LerpColor(OrbCenterBase, OrbCenterHover, hoverBlend);
        orb.SurroundColors = new[] { PaintLerp.LerpColor(OrbEdgeBase, OrbEdgeHover, hoverBlend) };
        orb.CenterPoint = new PointF(outer.Left + outer.Width * 0.38F, outer.Top + outer.Height * 0.30F);
        orb.FocusScales = new PointF(0.55F, 0.55F);
        graphics.FillEllipse(orb, outer);

        // 前光高光：左上两团叠加水光
        var highlight = new Rectangle(
            outer.X + (int)(outer.Width * 0.18F), outer.Y + (int)(outer.Height * 0.12F),
            (int)(outer.Width * 0.44F), (int)(outer.Height * 0.26F));
        using var glowSoft = new SolidBrush(Color.FromArgb(26 + (int)(14 * hoverBlend), 255, 255, 255));
        graphics.FillEllipse(glowSoft, highlight);
        using var glowCore = new SolidBrush(Color.FromArgb(48 + (int)(20 * hoverBlend), 255, 255, 255));
        graphics.FillEllipse(glowCore,
            highlight.X + highlight.Width / 5, highlight.Y + highlight.Height / 6,
            (int)(highlight.Width * 0.45F), (int)(highlight.Height * 0.5F));

        // 底部轮廓光：一道右下弯月形反光，增强玻璃感
        using var rim = new Pen(Color.FromArgb(22, 255, 255, 255), 1.4F);
        graphics.DrawArc(rim, Rectangle.Inflate(outer, -3, -3), 35, 105);
    }

    /// <summary>状态光环：停止灰环 / 运行呼吸绿环 / 启动旋转琥珀弧。</summary>
    private void DrawSignal(Graphics graphics, Rectangle ring, Color signalColor)
    {
        switch (visualState)
        {
            case ServiceVisualState.Starting:
                using (var pen = new Pen(signalColor, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawArc(pen, ring, spinStart, SpinArcSweep);
                break;

            case ServiceVisualState.Running:
                // 呼吸强度 0.72~1.0
                var breath = 0.72 + 0.28 * (Math.Sin(pulsePhase) + 1) / 2;
                using (var halo = new Pen(Color.FromArgb((int)(36 * breath), signalColor), 5F))
                    graphics.DrawEllipse(halo, ring);
                using (var mid = new Pen(Color.FromArgb((int)(90 * breath), signalColor), 3F))
                    graphics.DrawEllipse(mid, ring);
                using (var core = new Pen(Color.FromArgb((int)(210 * breath), signalColor), 1.8F))
                    graphics.DrawEllipse(core, ring);
                break;

            default:
                using (var pen = new Pen(Color.FromArgb(150, signalColor), 2F))
                    graphics.DrawEllipse(pen, ring);
                break;
        }
    }

    /// <summary>居中 π 字符。</summary>
    private static void DrawPi(Graphics graphics, Rectangle outer)
    {
        using var brush = new SolidBrush(Color.FromArgb(232, 236, 241));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("π", PiFont, brush, outer, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ticker.Dispose();
        base.Dispose(disposing);
    }
}
