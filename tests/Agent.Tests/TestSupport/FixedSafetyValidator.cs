using Agent.Domain;
using Agent.Safety;

namespace Agent.Tests.TestSupport;

internal sealed class FixedSafetyValidator(SafetyValidationResult result) : ISafetyValidator
{
    public SafetyValidationResult Validate(NextMessage message, CaseConstraints constraints) => result;
}
