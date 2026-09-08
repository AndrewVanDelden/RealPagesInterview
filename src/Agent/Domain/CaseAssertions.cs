namespace Agent.Domain;

public sealed record CaseAssertions(IReadOnlyList<string>? RequiredStates = null, CaseConstraints? Constraints = null) : HasUnknownMembers;
