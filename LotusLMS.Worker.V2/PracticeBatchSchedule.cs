namespace LotusLMS.Worker2;

/// <summary>Sequential deadlines without overlapping requests or catch-up bursts.</summary>
internal sealed class PracticeBatchSchedule
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;
    private readonly Func<double, CancellationToken, Task>? _trackTime;
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromSeconds(10);
    private long _lastSubmission;
    private long _lastTracking;

    public PracticeBatchSchedule(TimeSpan interval, TimeProvider clock,
        Func<double, CancellationToken, Task>? trackTime = null)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _interval = interval;
        _clock = clock;
        _lastSubmission = clock.GetTimestamp();
        _lastTracking = _lastSubmission;
        _trackTime = trackTime;
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        WaitAsync(_interval, cancellationToken);

    public async Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TrackElapsedAsync(flush: false, cancellationToken);
            var remaining = interval - _clock.GetElapsedTime(_lastSubmission);
            if (remaining <= TimeSpan.Zero) break;
            if (_trackTime is not null)
            {
                var untilTracking = TrackingInterval - _clock.GetElapsedTime(_lastTracking);
                if (untilTracking <= TimeSpan.Zero) continue;
                if (untilTracking < remaining) remaining = untilTracking;
            }
            await Task.Delay(remaining, _clock, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lastSubmission = _clock.GetTimestamp();
    }

    public Task FlushTrackingAsync(CancellationToken cancellationToken) =>
        TrackElapsedAsync(flush: true, cancellationToken);

    private async Task TrackElapsedAsync(bool flush, CancellationToken cancellationToken)
    {
        if (_trackTime is null) return;
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastTracking, now);
        if (elapsed < TimeSpan.FromMilliseconds(1) || (!flush && elapsed < TrackingInterval)) return;
        await _trackTime(elapsed.TotalSeconds, cancellationToken);
        _lastTracking = now;
    }
}
