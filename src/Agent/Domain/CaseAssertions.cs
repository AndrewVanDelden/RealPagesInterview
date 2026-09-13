namespace Agent.Domain;

// The element is nullable because JSON can supply a null inside the list and
// RespectNullableAnnotations does not reach collection elements, so a non-nullable element
// type would be a claim the deserializer does not honour. RequiredStateMap skips such a name,
// and a blank one, rather than rejecting the record: a missing name asserts no state, and an
// input shape is never an error (A16). The names beside it are still answered.
public sealed record CaseAssertions(IReadOnlyList<string?>? RequiredStates = null, CaseConstraints? Constraints = null) : HasUnknownMembers;
