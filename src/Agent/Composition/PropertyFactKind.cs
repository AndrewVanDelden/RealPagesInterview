namespace Agent.Composition;

// The kinds of property fact a call to action can carry to the model. Which kinds serve which call
// to action is the call-to-action table's; whether the record's own input calls for a kind is the
// composer's selection.
internal enum PropertyFactKind
{
    TourAvailability,
    ExtendedTourHours,
    StartingPrice,
    RenewalOffer,
}
