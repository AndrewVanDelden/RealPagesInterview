using Agent.Domain;
using Agent.Safety;

namespace Agent.Tests.TestSupport;

// Records every message it was asked to validate, in call order, and answers with the inner
// validator's verdict. A test that counts calls needs the real verdicts too, because which
// path a record takes through the agent depends on them.
internal sealed class CountingSafetyValidator(ISafetyValidator inner) : ISafetyValidator
{
    private readonly List<NextMessage> validatedMessages = [];

    public IReadOnlyList<NextMessage> ValidatedMessages => validatedMessages;

    public SafetyValidationResult Validate(NextMessage message, CaseConstraints constraints)
    {
        validatedMessages.Add(message);
        return inner.Validate(message, constraints);
    }
}
