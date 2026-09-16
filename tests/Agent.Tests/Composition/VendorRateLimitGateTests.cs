using Agent.Composition;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Agent.Tests.Composition;

public class VendorRateLimitGateTests
{
    private static readonly TimeSpan HeldCallWait = TimeSpan.FromSeconds(5);

    // Until a response has reported the key's limits, one call goes out alone: sending a batch
    // before the limits are known is how a run hits them.
    [Fact]
    public async Task ReserveAsync_LimitsUnknown_LetsOneCallThroughAndHoldsTheNextUntilItReportsLimits()
    {
        var gate = new VendorRateLimitGate(new FakeTimeProvider());

        VendorCallReservation probe = await gate.ReserveAsync(100, CancellationToken.None);
        Task<VendorCallReservation> next = gate.ReserveAsync(100, CancellationToken.None);

        Assert.False(next.IsCompleted);
        gate.Complete(probe, 100, new VendorRateLimits(RequestsPerMinute: 500, TokensPerMinute: 200_000));
        await next.WaitAsync(HeldCallWait);
    }

    // A response that reported no limits leaves them unknown, so the next call goes out alone in
    // its turn rather than the batch going out on no information.
    [Fact]
    public async Task ReserveAsync_CallCompletesWithoutLimits_LetsOnlyTheNextCallThrough()
    {
        var gate = new VendorRateLimitGate(new FakeTimeProvider());

        VendorCallReservation first = await gate.ReserveAsync(100, CancellationToken.None);
        Task<VendorCallReservation> second = gate.ReserveAsync(100, CancellationToken.None);
        Task<VendorCallReservation> third = gate.ReserveAsync(100, CancellationToken.None);

        gate.Complete(first, 100, reportedLimits: null);
        await Task.WhenAny(second, third).WaitAsync(HeldCallWait);

        Assert.False(second.IsCompleted && third.IsCompleted);
    }

    // Requests sent in the last sixty seconds stay within ninety percent of the reported limit:
    // with 10 a minute, nine go out and the tenth waits until the oldest is sixty seconds old.
    [Fact]
    public async Task ReserveAsync_RequestsAtNinetyPercentOfTheLimit_HoldsTheNextUntilTheOldestLeavesTheWindow()
    {
        var time = new FakeTimeProvider();
        var gate = new VendorRateLimitGate(time);
        VendorCallReservation probe = await gate.ReserveAsync(10, CancellationToken.None);
        gate.Complete(probe, 10, new VendorRateLimits(RequestsPerMinute: 10, TokensPerMinute: 1_000_000));

        for (int call = 0; call < 8; call++)
        {
            await gate.ReserveAsync(10, CancellationToken.None).WaitAsync(HeldCallWait);
        }

        Task<VendorCallReservation> tenth = gate.ReserveAsync(10, CancellationToken.None);
        Assert.False(tenth.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(tenth.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        await tenth.WaitAsync(HeldCallWait);
    }

    // Tokens are held the same way, and a call that completes with fewer tokens than it reserved
    // gives the difference back at once: 900 of 1,000 a minute are usable, 100 + 700 are in the
    // window, a 300-token call waits, and it goes out when the 700 turns out to be 200.
    [Fact]
    public async Task ReserveAsync_TokensAtNinetyPercentOfTheLimit_HoldsTheNextUntilACompletedCallFreesTokens()
    {
        var gate = new VendorRateLimitGate(new FakeTimeProvider());
        VendorCallReservation probe = await gate.ReserveAsync(100, CancellationToken.None);
        gate.Complete(probe, 100, new VendorRateLimits(RequestsPerMinute: 100, TokensPerMinute: 1_000));
        VendorCallReservation large = await gate.ReserveAsync(700, CancellationToken.None).WaitAsync(HeldCallWait);

        Task<VendorCallReservation> held = gate.ReserveAsync(300, CancellationToken.None);
        Assert.False(held.IsCompleted);

        gate.Complete(large, 200, new VendorRateLimits(RequestsPerMinute: 100, TokensPerMinute: 1_000));
        await held.WaitAsync(HeldCallWait);
    }

    // A call larger than the whole allowance could never fit, so with nothing else in the window it
    // goes out rather than waiting forever; the vendor, not the gate, is the one to refuse it.
    [Fact]
    public async Task ReserveAsync_CallLargerThanTheAllowanceAndAnEmptyWindow_IsLetThrough()
    {
        var time = new FakeTimeProvider();
        var gate = new VendorRateLimitGate(time);
        VendorCallReservation probe = await gate.ReserveAsync(100, CancellationToken.None);
        gate.Complete(probe, 100, new VendorRateLimits(RequestsPerMinute: 100, TokensPerMinute: 1_000));
        time.Advance(TimeSpan.FromSeconds(60));

        await gate.ReserveAsync(5_000, CancellationToken.None).WaitAsync(HeldCallWait);
    }

    // A held call observes its caller's token, so a cancelled run is not left waiting on capacity.
    [Fact]
    public async Task ReserveAsync_CancelledWhileHeld_Throws()
    {
        var gate = new VendorRateLimitGate(new FakeTimeProvider());
        await gate.ReserveAsync(100, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        Task<VendorCallReservation> held = gate.ReserveAsync(100, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.WaitAsync(HeldCallWait));
    }
}
