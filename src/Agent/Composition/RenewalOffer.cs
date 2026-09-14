namespace Agent.Composition;

// A resident's renewal offer as the property system states it, identified by its unit or its
// offer id: how many days current pricing is held, and whether text reminders are offered.
public sealed record RenewalOffer(string? Unit = null, string? OfferId = null, int? PriceHoldDays = null, bool? TextRemindersOffered = null);
