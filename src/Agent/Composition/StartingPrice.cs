namespace Agent.Composition;

// One floor plan's starting price as the property system states it: the total monthly price with
// every mandatory monthly fee included, as of a date. A price is never stated as a base rent,
// because an advertised rent that leaves out mandatory fees is the deceptive price the FTC's
// Greystar order forbids.
public sealed record StartingPrice(string FloorPlan, decimal TotalMonthlyPrice, DateOnly AsOf);
