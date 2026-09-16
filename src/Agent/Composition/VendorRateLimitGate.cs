namespace Agent.Composition;

// Keeps one model's calls under the vendor's per-minute limits instead of retrying after hitting
// them. Until a response has reported the key's limits, one call goes out at a time. After that a
// call goes out only while the calls sent in the last sixty seconds, counted in requests and in
// tokens, stay within ninety percent of the latest reported limits; the rest wait until the oldest
// call leaves the window or a completed call gives back tokens it reserved and did not use. A
// window of what was sent is stricter than the vendor's continuously refilled allowance, and the
// headroom covers the gap between this project's token estimate and the vendor's. It assumes this
// run is the only user of the key's limits while it runs. Time comes from the TimeProvider the
// caller supplies, because pacing is elapsed time, not a decision about a record.
public sealed class VendorRateLimitGate(TimeProvider timeProvider)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const double Headroom = 0.9;

    private readonly object sync = new();
    private readonly Queue<VendorCallReservation> sent = new();
    private VendorRateLimits? limits;
    private bool unlimitedCallInFlight;
    private TaskCompletionSource capacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Waits until the call fits, then records it as sent. O(w) per attempt in the calls w still in
    // the window, which the reported request limit bounds; a held call re-checks each time a call
    // completes or the oldest call leaves the window.
    public async Task<VendorCallReservation> ReserveAsync(int estimatedTokens, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task wake;
            TimeSpan? untilOldestLeaves = null;

            lock (sync)
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                while (sent.Count > 0 && now - sent.Peek().SentAt >= Window)
                {
                    sent.Dequeue();
                }

                if (TryAdmit(estimatedTokens, now) is { } reservation)
                {
                    return reservation;
                }

                wake = capacityChanged.Task;
                if (limits is not null)
                {
                    untilOldestLeaves = sent.Peek().SentAt + Window - now;
                }
            }

            if (untilOldestLeaves is { } delay)
            {
                await await Task.WhenAny(wake, Task.Delay(delay, timeProvider, cancellationToken));
            }
            else
            {
                await wake.WaitAsync(cancellationToken);
            }
        }
    }

    // Records what a call counted for once it is over, whether it completed or failed, and the
    // limits its response reported, if any, then wakes every held call to check again.
    public void Complete(VendorCallReservation reservation, int countedTokens, VendorRateLimits? reportedLimits)
    {
        TaskCompletionSource woken;
        lock (sync)
        {
            reservation.Tokens = countedTokens;
            limits = reportedLimits ?? limits;
            unlimitedCallInFlight = false;
            woken = capacityChanged;
            capacityChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        woken.TrySetResult();
    }

    // Called under the lock. With no limits known, only one call may be out. With limits known, a
    // call fits within both allowances, or goes out anyway when the window is empty, since a call
    // larger than the whole allowance would otherwise wait forever.
    private VendorCallReservation? TryAdmit(int estimatedTokens, DateTimeOffset now)
    {
        if (limits is null)
        {
            if (unlimitedCallInFlight)
            {
                return null;
            }

            unlimitedCallInFlight = true;
            return Send(estimatedTokens, now);
        }

        int usableRequests = Math.Max(1, (int)(limits.RequestsPerMinute * Headroom));
        int usableTokens = Math.Max(1, (int)(limits.TokensPerMinute * Headroom));
        bool fits = sent.Count + 1 <= usableRequests && sent.Sum(call => (long)call.Tokens) + estimatedTokens <= usableTokens;
        return fits || sent.Count == 0 ? Send(estimatedTokens, now) : null;
    }

    private VendorCallReservation Send(int estimatedTokens, DateTimeOffset now)
    {
        var reservation = new VendorCallReservation(now, estimatedTokens);
        sent.Enqueue(reservation);
        return reservation;
    }
}
