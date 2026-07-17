using System.Runtime.InteropServices;

namespace Pix.Launcher;

/// <summary>FlowLayoutPanel that keeps native scrolling but never paints its scrollbars.</summary>
internal sealed class HiddenScrollFlowPanel : FlowLayoutPanel
{
    private const int WmNcpaint = 0x0085;
    private const int WmHscroll = 0x0114;
    private const int WmVscroll = 0x0115;
    private const int WmMouseWheel = 0x020A;
    private const int SbBoth = 3;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr handle, int bar, bool show);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideBars();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideBars();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WmNcpaint or WmHscroll or WmVscroll or WmMouseWheel) HideBars();
    }

    private void HideBars()
    {
        if (IsHandleCreated) ShowScrollBar(Handle, SbBoth, false);
    }
}
