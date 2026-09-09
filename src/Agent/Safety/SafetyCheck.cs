namespace Agent.Safety;

// The four safety checks (D38). They are named as the checks themselves rather than as one
// PII check, because D40 gives SocialSecurityNumber and LongDigitRun different answers to
// the question of whether a record may switch them off.
public enum SafetyCheck
{
    OptOutInstructions,
    SocialSecurityNumber,
    LongDigitRun,
    FairHousing,
}
