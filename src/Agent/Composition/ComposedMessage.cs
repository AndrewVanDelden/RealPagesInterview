using Agent.Domain;

namespace Agent.Composition;

// What a composer returns (D24): the message, and the working that says which implementation
// produced it. The same shape SendScheduler's ScheduledSend has, for the same reason: the
// caller needs the decision and the account of it, and neither can be recovered from the
// other later.
public sealed record ComposedMessage(NextMessage Message, CompositionNotes Notes);
