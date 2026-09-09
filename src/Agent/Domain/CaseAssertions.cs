namespace Agent.Domain;

// D47: the element is nullable because JSON can supply a null inside the list and
// RespectNullableAnnotations does not reach collection elements, so a non-nullable element
// type would be a claim the deserializer does not honour. RequiredStateMap skips such a name.
public sealed record CaseAssertions(IReadOnlyList<string?>? RequiredStates = null, CaseConstraints? Constraints = null) : HasUnknownMembers;
