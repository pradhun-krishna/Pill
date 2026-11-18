using System.Diagnostics;
using System.Windows.Media;

namespace NotchWin.Core;

/// <summary>
/// True damped harmonic oscillator spring driven by CompositionTarget.Rendering
/// at the monitor's native refresh rate (60–144Hz). Automatically detaches when
/// all springs settle so steady-state idle CPU is strictly 0.00%.
/// </summary>
public sealed class SpringValue
{
    public double Value { get; private set; }
    public double Target { get; private set; }
    public double Velocity { get; private set; }
    public double Stiffness { get; set; }
    public double Damping { get; set; }
    public double Precision { get; set; } = 0.08;

    public bool IsSettled => Math.Abs(Target - Value) < Precision && Math.Abs(Velocity) < Precision;

    public SpringValue(double initial, double stiffness = 420.0, double damping = 32.0)
    {
        Value = initial;
        Target = initial;
        Stiffness = stiffness;
        Damping = damping;
    }

    public void SetTarget(double target) => Target = target;

    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
            return;
        }

        // Semi-implicit Euler in sub-steps for stability at any refresh rate.
        const double maxSubStep = 1.0 / 240.0;
        double remaining = Math.Min(dt, 0.05);
        while (remaining > 0)
        {
            double step = Math.Min(remaining, maxSubStep);
            double force = (Target - Value) * Stiffness;
            double damp = Velocity * Damping;
            Velocity += (force - damp) * step;
            Value += Velocity * step;
            remaining -= step;
        }

        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}

public sealed class SpringGroup : IDisposable
{
    private readonly List<SpringValue> _springs = new();
    private readonly Action _onFrame;
    private readonly Stopwatch _clock = new();
    private double _lastTime;
    private bool _running;

    public SpringGroup(Action onFrame)
    {
        _onFrame = onFrame;
    }

    public SpringValue Add(double initial, double stiffness = 420.0, double damping = 32.0, double precision = 0.08)
    {
        var s = new SpringValue(initial, stiffness, damping) { Precision = precision };
        _springs.Add(s);
        return s;
    }

    public void Wake()
    {
        if (_running) return;
        _running = true;
        _clock.Restart();
        _lastTime = 0;
        CompositionTarget.Rendering += OnRender;
    }

    private void OnRender(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;
        if (dt <= 0) dt = 1.0 / 60.0;

        bool anyActive = false;
        for (int i = 0; i < _springs.Count; i++)
        {
            var s = _springs[i];
            if (!s.IsSettled)
            {
                s.Step(dt);
                if (!s.IsSettled) anyActive = true;
            }
        }

        _onFrame();

        if (!anyActive)
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRender;
        _clock.Stop();
    }

    public void Dispose() => Stop();
}
