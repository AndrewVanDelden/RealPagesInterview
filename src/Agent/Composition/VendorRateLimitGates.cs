using System.Collections.Concurrent;

namespace Agent.Composition;

// One VendorRateLimitGate per model name. The vendor's limits are per model, so every client of one
// model, the composer's and the judge's alike, has to wait in the same gate: two gates on one model
// would each let through ninety percent of the same limit.
public sealed class VendorRateLimitGates(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, VendorRateLimitGate> gatesByModel = new(StringComparer.Ordinal);

    public VendorRateLimitGate For(string model) =>
        gatesByModel.GetOrAdd(model, _ => new VendorRateLimitGate(timeProvider));
}
