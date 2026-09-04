using Agent.Domain;

namespace Agent.Safety;

public interface ISafetyValidator
{
    SafetyValidationResult Validate(NextMessage message, CaseConstraints constraints);
}
