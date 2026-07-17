using System.Diagnostics;

namespace Pix.Launcher;

internal static class Easing
{
    public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double OutQuart(double t) => 1 - Math.Pow(1 - t, 4);
}

internal static class PaintLerp
{
    public static int Lerp(int from, int to, double t) => (int)Math.Round(from + (to - from) * t);

    public static Color LerpColor(Color from, Color to, double t) => Color.FromArgb(
        Lerp(from.A, to.A, t),
        Lerp(from.R, to.R, t),
        Lerp(from.G, to.G, t),
        Lerp(from.B, to.B, t));
}

/// <summary>Frame-rate independent exponential approach toward a target value.</summary>
internal static class MotionStep
{
    public static double Approach(double current, double target, double dtSeconds, double rate)
        => current + (target - current) * (1 - Math.Exp(-dtSeconds * rate));

    public static float Approach(float current, float target, double dtSeconds, double rate)
        => (float)(current + (target - current) * (1 - Math.Exp(-dtSeconds * rate)));
}

/// <summary>Drives a single eased 0→1 progression on the UI thread, then stops.</summary>
internal sealed class ValueAnimator : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch watch = new();

    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(200);
    public Func<double, double> Ease { get; set; } = Easing.OutCubic;
    public bool IsRunning { get; private set; }

    public event Action<double>? Progressed;
    public event Action? Completed;

    public ValueAnimator()
    {
        timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        IsRunning = true;
        watch.Restart();
        timer.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        timer.Stop();
        watch.Reset();
    }

    private void OnTick()
    {
        var raw = Math.Clamp(watch.ElapsedMilliseconds / (double)Duration.TotalMilliseconds, 0, 1);
        Progressed?.Invoke(Ease(raw));
        if (raw >= 1)
        {
            Stop();
            Completed?.Invoke();
        }
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }
}

/// <summary>Shared 15ms ticker for controls that ease several values until settled.</summary>
internal sealed class MotionTicker : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch watch = new();
    private long previousTick;

    public event Action<double>? Tick;

    public bool IsRunning => timer.Enabled;

    public MotionTicker()
    {
        timer.Tick += (_, _) => OnTick();
    }

    public void EnsureRunning()
    {
        if (timer.Enabled) return;
        watch.Restart();
        previousTick = 0;
        timer.Start();
    }

    public void Stop() => timer.Stop();

    private void OnTick()
    {
        var now = watch.ElapsedMilliseconds;
        var dt = Math.Clamp((now - previousTick) / 1000.0, 0.001, 0.1);
        previousTick = now;
        Tick?.Invoke(dt);
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }
}
