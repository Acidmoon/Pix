using System.Drawing.Drawing2D;

namespace Pix.Launcher;

internal sealed class FloatingIconControl : Control
{
    public enum ServiceVisualState { Stopped, Starting, Running }

    private ServiceVisualState visualState;
    private bool hovered;

    public ServiceVisualState VisualState
    {
        get => visualState;
        set { visualState = value; Invalidate(); }
    }

    public FloatingIconControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.SizeAll;
        Size = new Size(56, 56);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new Rectangle(1, 1, Width - 3, Height - 3);
        var inner = Rectangle.Inflate(outer, -4, -4);

        using var shadow = new SolidBrush(Color.FromArgb(42, 0, 0, 0));
        graphics.FillEllipse(shadow, outer.X + 1, outer.Y + 2, outer.Width, outer.Height);
        using var shell = new SolidBrush(hovered ? Color.FromArgb(39, 44, 51) : Color.FromArgb(24, 28, 33));
        graphics.FillEllipse(shell, outer);
        using var ringPen = new Pen(Color.FromArgb(92, 101, 112), 1F);
        graphics.DrawEllipse(ringPen, inner);

        var signalColor = visualState switch
        {
            ServiceVisualState.Running => Color.FromArgb(67, 229, 143),
            ServiceVisualState.Starting => Color.FromArgb(244, 178, 64),
            _ => Color.FromArgb(151, 160, 170),
        };
        using var signalPen = new Pen(signalColor, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(signalPen, inner, -58, visualState == ServiceVisualState.Running ? 126 : 64);

        using var font = new Font("Bahnschrift SemiBold", 22F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.FromArgb(244, 246, 248));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("P", font, textBrush, outer, format);

        using var stateBrush = new SolidBrush(signalColor);
        graphics.FillEllipse(stateBrush, Width - 14, Height - 14, 7, 7);
    }
}
