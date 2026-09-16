using System.Collections.Concurrent;

namespace Agent.Composition;

// One VendorRateLimitGate per model name. The vendor's limits are per model, so every client of one
// model, the composer's and the judge's alike, has to wait in the same gate: two gates on one model
// would each let through ninety percent of the same limit. Matched case-insensitively: the model
// name is the vendor's identifier, not a case-sensitive token, and a config value typed in the
// vendor's own branding case ("GPT-4o") still names the same model as the lowercase API string.
public sealed class VendorRateLimitGates(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, VendorRateLimitGate> gatesByModel = new(StringComparer.OrdinalIgnoreCase);

    public VendorRateLimitGate For(string model) =>
        gatesByModel.GetOrAdd(model, _ => new VendorRateLimitGate(timeProvider));
}
