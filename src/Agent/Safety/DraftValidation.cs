using Agent.Common;
using Agent.Domain;

namespace Agent.Safety;

// The compose-validate loop's verdict on the draft it returned, kept with the question it
// answered: which validator, which draft, which constraints. The agent's final gate reuses the
// verdict only when it would ask that same question, so a swapped draft, changed constraints or
// a loop wired with a different validator is validated again there. The constructor is
// internal and the loop is its only caller, so no composer outside this library can hand the
// final gate a verdict of its own.
public sealed class DraftValidation
{
    private readonly ISafetyValidator validator;

    private readonly NextMessage draft;

    private readonly CaseConstraints constraints;

    private readonly SafetyValidationResult result;

    internal DraftValidation(ISafetyValidator validator, NextMessage draft, CaseConstraints constraints, SafetyValidationResult result)
    {
        this.validator = validator;
        this.draft = draft;
        this.constraints = constraints;
        this.result = result;
    }

    // O(1): one reference comparison and two record equality checks, each over a fixed set of
    // members. Record equality compares list and dictionary members by reference, so an equal
    // copy built with new lists reads as a different question and is validated again.
    public Option<SafetyValidationResult> ResultFor(ISafetyValidator askingValidator, NextMessage askedDraft, CaseConstraints askedConstraints) =>
        ReferenceEquals(validator, askingValidator) && draft == askedDraft && constraints == askedConstraints
            ? Option<SafetyValidationResult>.Some(result)
            : Option<SafetyValidationResult>.None();
}
