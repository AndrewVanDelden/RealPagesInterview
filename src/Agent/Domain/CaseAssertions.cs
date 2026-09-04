namespace Agent.Domain;

public sealed record CaseAssertions(IReadOnlyList<string> RequiredStates, CaseConstraints Constraints);
