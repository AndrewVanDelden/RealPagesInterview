using Agent.Domain;

namespace Agent.Safety;

public interface ISafetyValidator
{
    ValidationResult Validate(NextMessage message, CaseConstraints constraints);
}
