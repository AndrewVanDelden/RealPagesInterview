using Agent.Common;
using Agent.Domain;

namespace Agent.Safety;

// D42: what brand style is, computed from the message rather than claimed. Three rules,
// each fitted to what the two sample bodies prove and each satisfied by the template
// composer on sms and email in both language sets; BrandStyleRule carries what was proposed
// and rejected, and why.
//
// D39 classifies this: it is a diagnostic and never suppresses. An off-voice message is
// off-voice, not unlawful, so a message failing a rule here still goes out and the state is
// recorded false. It is deliberately not in ValidatingMessageComposer's compose-validate
// loop for that reason, and it is not an ISafetyValidator.
//
// No interface and no instance state (EA): there is one implementation, it is a pure
// function of the message, and no test needs a substitute for it because a test can craft
// the message that fails the rule it cares about. SafetyValidator earns its interface
// because FixedSafetyValidator stands in for it; nothing stands in for this.
public static class BrandStyleValidator
{
    private const int MaxExclamationMarks = 1;

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    // O(n) in the body length: one line split scanned from the end, one exclamation count,
    // and one subject test that reads no more than the subject.
    public static BrandStyleValidationResult Validate(NextMessage message)
    {
        string body = message.Body ?? string.Empty;
        var failedRules = new List<BrandStyleRule>(3);

        if (!ClosesWithOptOutInstructions(body))
        {
            failedRules.Add(BrandStyleRule.OptOutOnLastLine);
        }

        if (body.Count(character => character == '!') > MaxExclamationMarks)
        {
            failedRules.Add(BrandStyleRule.ExclamationLimit);
        }

        bool isEmail = message.Channel == CommunicationChannel.Email;
        bool carriesSubject = !Presence.IsAbsent(message.Subject);

        if (isEmail != carriesSubject)
        {
            failedRules.Add(BrandStyleRule.SubjectMatchesChannel);
        }

        return new BrandStyleValidationResult(failedRules);
    }

    // The last non-blank line, not the last line: a trailing newline is whitespace, not a
    // missing instruction. A body with no non-blank line has no line for the instruction to
    // sit on, so the rule fails rather than passing by vacuity.
    // O(n) in the body length: one split, then a scan back to the first non-blank line.
    private static bool ClosesWithOptOutInstructions(string body)
    {
        string[] lines = body.Split(LineBreaks, StringSplitOptions.None);

        for (int index = lines.Length - 1; index >= 0; index--)
        {
            if (!Presence.IsAbsent(lines[index]))
            {
                return OptOutInstructions.IsPresent(lines[index]);
            }
        }

        return false;
    }
}
