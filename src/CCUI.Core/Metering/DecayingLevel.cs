namespace CCUI.Core.Metering;

/// <summary>
/// A meter level that jumps up on a hit and decays exponentially, with a peak marker that holds briefly and then
/// falls, like an audio peak meter. Time comes from a <see cref="TimeProvider"/>, so the behaviour is deterministic
/// in tests; a renderer simply samples <see cref="Level"/> and <see cref="Peak"/> every frame.
/// </summary>
public sealed class DecayingLevel(TimeProvider time, TimeSpan halfLife, TimeSpan peakHold)
{
    private const double Silence = 0.002;
    private static readonly TimeSpan BreathPeriod = TimeSpan.FromSeconds(1.6);

    private readonly Lock _gate = new();
    private double _level;
    private long _levelAt;
    private double _peak;
    private long _peakAt;
    private double _floor;
    private long _floorAt;

    /// <summary>Raised after a hit, so a renderer can start animating.</summary>
    public event EventHandler? Changed;

    /// <summary>Raises the level to at least <paramref name="amount"/> (0..1).</summary>
    public void Hit(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var now = time.GetTimestamp();
        lock (_gate)
        {
            if (amount >= LevelAt(now))
            {
                _level = amount;
                _levelAt = now;
            }

            if (amount >= PeakAt(now))
            {
                _peak = amount;
                _peakAt = now;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Keeps the level from falling below <paramref name="amount"/> (0..1) until it is set back to 0, breathing
    /// gently so a long silence (thinking, a slow tool call) still reads as "working". Hits still rise above it.
    /// </summary>
    public void SetFloor(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        lock (_gate)
        {
            if (amount == _floor)
            {
                return;
            }

            _floor = amount;
            _floorAt = time.GetTimestamp();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public double Level
    {
        get
        {
            lock (_gate)
            {
                return LevelAt(time.GetTimestamp());
            }
        }
    }

    public double Peak
    {
        get
        {
            lock (_gate)
            {
                var now = time.GetTimestamp();
                return Math.Max(PeakAt(now), LevelAt(now));
            }
        }
    }

    /// <summary>True once both level and peak have decayed to nothing; a renderer can stop animating.</summary>
    public bool IsSilent => Peak <= 0;

    private double LevelAt(long now) => Math.Max(Decay(_level, time.GetElapsedTime(_levelAt, now)), FloorAt(now));

    // Swings between 75% and 100% of the floor, starting at the top.
    private double FloorAt(long now)
    {
        if (_floor <= 0)
        {
            return 0;
        }

        var phase = time.GetElapsedTime(_floorAt, now) / BreathPeriod;
        return _floor * (0.875 + (0.125 * Math.Cos(2 * Math.PI * phase)));
    }

    private double PeakAt(long now)
    {
        var elapsed = time.GetElapsedTime(_peakAt, now) - peakHold;
        return elapsed <= TimeSpan.Zero ? _peak : Decay(_peak, elapsed);
    }

    private double Decay(double value, TimeSpan elapsed)
    {
        if (value <= 0 || halfLife <= TimeSpan.Zero)
        {
            return 0;
        }

        var decayed = value * Math.Pow(0.5, elapsed / halfLife);
        return decayed < Silence ? 0 : decayed;
    }
}

/// <summary>Maps traffic sizes onto a meter's 0..1 range on a log scale, so a 50-character prompt still registers.</summary>
public static class LevelScale
{
    public static double FromSize(long size, long fullScale = 20_000)
    {
        if (size <= 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Log10(1 + size) / Math.Log10(1 + fullScale), 0.08, 1);
    }
}
