namespace Agent.Composition;

// One call's place in a VendorRateLimitGate's sixty-second window: when it was let through and how
// many tokens it counts for, first the estimate and then, once the call completes, what it cost.
// Only the gate that issued it reads or changes it, under that gate's lock.
public sealed class VendorCallReservation
{
    internal VendorCallReservation(DateTimeOffset sentAt, int tokens)
    {
        SentAt = sentAt;
        Tokens = tokens;
    }

    internal DateTimeOffset SentAt { get; }

    internal int Tokens { get; set; }
}
