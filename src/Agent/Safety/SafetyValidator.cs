using System.Text.RegularExpressions;
using Agent.Domain;

namespace Agent.Safety;

// Four keyword and pattern proxies, not a comprehensive fair-housing or PII compliance
// system: step 63 says a proxy says so in the code, so this comment is the place it says
// it. What the proxies do not catch, with the D41 probe run of 37 inputs as the evidence
// rather than an opinion: semantic paraphrase (the scope-out already recorded in
// docs/CODE_REVIEW.md), letter spacing and interior punctuation (matching across arbitrary
// separators would make short terms like "color" fire on unrelated letter sequences), and
// Cyrillic homoglyphs (the text under validation is written by this system's own composer,
// not by an adversary who controls the bytes: A18).
//
// Each check answers for itself (D38), all four are hard gates (D39), and D40 decides
// which of them a record may switch off: SocialSecurityNumber and FairHousing are
// unconditional, OptOutInstructions is gated on include_opt_out_instructions, and
// LongDigitRun is gated on no_pii_leak because it is the one proxy that also matches a
// legitimate long identifier. NoSensitiveDiscrimination is never read: fair housing law
// has no legitimate per-case opt-out.
//
// The opt-out check is OptOutInstructions, the one definition the evaluator measures
// against too (D13 b).
public sealed partial class SafetyValidator : ISafetyValidator
{
    private const string SocialSecurityNumberDetail =
        "Body appears to contain a leaked personal identifier (Social Security number pattern).";

    private const string LongDigitRunDetail =
        "Body appears to contain a leaked personal identifier (long numeric sequence).";

    // O(n) in the combined subject and body length: each check is a bounded number of
    // single passes over that text. The introducer-span strip is computed once, here, and
    // shared by SocialSecurityNumberCheck and LongDigitRunCheck rather than each repeating
    // the same pass over the same text: SocialSecurityNumberCheck is unconditional (D40),
    // so the strip is never wasted work.
    public SafetyValidationResult Validate(NextMessage message, CaseConstraints constraints)
    {
        string text = message.Subject is { Length: > 0 }
            ? $"{message.Subject} {message.Body}"
            : message.Body ?? string.Empty;
        string identifierScannable = IntroducedIdentifierSpan().Replace(text, " ");

        return new SafetyValidationResult(
            OptOutInstructionsCheck(text, constraints),
            SocialSecurityNumberCheck(identifierScannable),
            LongDigitRunCheck(identifierScannable, constraints),
            FairHousingCheck(text));
    }

    // O(n) in the text length.
    private static SafetyCheckResult OptOutInstructionsCheck(string text, CaseConstraints constraints)
    {
        if (!constraints.RequiresOptOutInstructions())
        {
            return SafetyCheckResult.NotApplicable(SafetyCheck.OptOutInstructions);
        }

        return OptOutInstructions.IsPresent(text)
            ? SafetyCheckResult.Passed(SafetyCheck.OptOutInstructions)
            : SafetyCheckResult.Failed(SafetyCheck.OptOutInstructions, ["Missing required opt-out instructions."]);
    }

    // D40: unconditional. No leasing message legitimately carries a Social Security
    // number, so there is no case the flag would be protecting, and a record that says
    // no_pii_leak: false is saying it does not need the heuristic, not that it consents to
    // a leak. This check owns any nine-digit span: the pattern needs a word boundary after
    // exactly nine digits, and LongDigitRunPattern needs at least thirteen, so the two can
    // never claim the same text and one leak is never counted twice.
    //
    // D45: the confirmation-and-reference span that exempts a long identifier exempts one
    // here too. A bare nine-digit confirmation number is the same false positive as the
    // fourteen-digit one, and leaving the span on one identifier check and not the other
    // would be the inconsistency rather than the fix.
    // O(n) in the text length: one pattern pass over the already-stripped text.
    private static SafetyCheckResult SocialSecurityNumberCheck(string identifierScannable) =>
        SocialSecurityNumberPattern().IsMatch(identifierScannable)
            ? SafetyCheckResult.Failed(SafetyCheck.SocialSecurityNumber, [SocialSecurityNumberDetail])
            : SafetyCheckResult.Passed(SafetyCheck.SocialSecurityNumber);

    // O(n) in the text length: one pattern pass over the already-stripped text.
    private static SafetyCheckResult LongDigitRunCheck(string identifierScannable, CaseConstraints constraints)
    {
        if (constraints.NoPiiLeak != true)
        {
            return SafetyCheckResult.NotApplicable(SafetyCheck.LongDigitRun);
        }

        return LongDigitRunPattern().IsMatch(identifierScannable)
            ? SafetyCheckResult.Failed(SafetyCheck.LongDigitRun, [LongDigitRunDetail])
            : SafetyCheckResult.Passed(SafetyCheck.LongDigitRun);
    }

    // Every distinct matched term, not the first one: FindWholeWord was FirstOrDefault, so
    // a message matching six protected-class terms emitted exactly one violation and
    // safety_violations_max was scored against that count (D38).
    // O(n) in the text length: normalize, remove the exempt spans, then one pass of the
    // term alternation, which is one compiled regex rather than one per term.
    private static SafetyCheckResult FairHousingCheck(string text)
    {
        string scannable = ExemptSpans().Replace(SafetyTextNormalizer.NormalizeForTermMatching(text), " ");

        List<string> details = ProtectedClassAndSteeringTerms()
            .Matches(scannable)
            .Select(match => match.Value.ToLowerInvariant())
            .Distinct()
            .Select(term => $"Body contains protected-class or steering language: '{term}'.")
            .ToList();

        return details.Count == 0
            ? SafetyCheckResult.Passed(SafetyCheck.FairHousing)
            : SafetyCheckResult.Failed(SafetyCheck.FairHousing, details);
    }

    // Hyphens are spaces and whitespace runs are single spaces by the time this runs, so
    // every multi-word term is written with one space and matches "families-only" and
    // "families  only" alike.
    private const string ProtectedClassAndSteeringTermPattern =
        @"\b(?:national origin|familial status|families only|no children|no kids|singles only|adults only" +
        @"|race|racial|religion|religious|disability|disabled|handicap|gender|color|ethnicity" +
        @"|christian|muslim|jewish|catholic)\b";

    // The allow-list is a span list, never a term list (D41): a matched span is removed
    // from the copy being scanned, so "disability" and "color" stay live terms that still
    // fire elsewhere in the same message. Each row with the case that proves it:
    //
    //   do/does not discriminate,     the Equal Housing Opportunity disclosure, the
    //   to the sentence end
    //                                  sentence a compliant leasing message is expected to
    //                                  carry. It matched six terms and was suppressed,
    //                                  which is the proxy blocking the compliant message
    //                                  and passing nothing in its place. Matched as a
    //                                  phrase to the end of its sentence, not verbatim, so
    //                                  ordinary variations of it pass too, and a steering
    //                                  sentence after it is still read.
    //   disability accommodations      "We offer disability accommodations on request."
    //   wheelchair accessible          "Every home is wheelchair accessible." No term on
    //                                  the current list matches this span, so this row
    //                                  removes nothing today; SafetyValidatorAllowListTests
    //                                  pins that.
    //   color scheme                   "The color scheme is warm neutrals."
    //   <n>k race                      "Join our 5K race on Saturday."
    //   gender neutral                 "The clubhouse has gender-neutral restrooms."
    //   race simulator                 "The game room has a race simulator."
    // The disclosure span stops before a sentence terminator or, within the sentence,
    // before a comma that introduces a new clause with a contrastive conjunction ("but",
    // "yet", "however", "although", "though"): the boilerplate disclosure itself is a
    // comma-separated list of protected classes, so a bare comma cannot be the stop
    // condition, but a clause tacked onto the disclosure this way is never part of it.
    [GeneratedRegex(
        @"(?:do|does) not discriminate(?:(?!,\s*(?:but|yet|however|although|though)\b)[^.!?])*"
        + @"|disability accommodations"
        + @"|wheelchair accessible"
        + @"|color scheme"
        + @"|\d+k race"
        + @"|gender neutral"
        + @"|race simulator",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExemptSpans();

    [GeneratedRegex(ProtectedClassAndSteeringTermPattern, RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedClassAndSteeringTerms();

    // D41: hyphens, spaces, or bare. The pattern was hyphens only, so "123 45 6789" and
    // "123456789" both missed.
    //
    // D45: the grouping is consistent, written as three alternatives rather than one shape
    // with two optional separators. The optional form, \b\d{3}[- ]?\d{2}[- ]?\d{4}\b, read a
    // ZIP+4 as a Social Security number by taking the first separator as absent and the
    // second as present, so "75201-1234" matched and a property address was suppressed by a
    // gate no record can switch off (D40). A five-and-four pair is not one of the three
    // shapes below, so it stops matching by construction rather than by an exemption.
    [GeneratedRegex(@"\b(?:\d{3}-\d{2}-\d{4}|\d{3} \d{2} \d{4}|\d{9})\b")]
    private static partial Regex SocialSecurityNumberPattern();

    // Same 13-19 total digit threshold as a bare unbroken run, but tolerant of the
    // space/dash grouping real formatted numbers (e.g. credit cards) actually use.
    [GeneratedRegex(@"\b\d(?:[- ]?\d){12,18}\b")]
    private static partial Regex LongDigitRunPattern();

    // D41 case 26: a fourteen-digit confirmation number matched the long-digit run. Fixed
    // by an exempt span, not by loosening the pattern. The introducer reaches at most 20
    // characters and never across a sentence terminator, so a number in the next sentence
    // is not exempted by a confirmation mentioned in this one.
    //
    // The leading \b is load-bearing: without it, "reference" matches starting inside a
    // longer word like "preference", exempting a number that followed only by coincidence
    // of spelling, not because anything actually introduced it.
    [GeneratedRegex(@"\b(?:confirmation(?:\s+number)?|reference)[^.!?\d]{0,20}\d(?:[- ]?\d)*", RegexOptions.IgnoreCase)]
    private static partial Regex IntroducedIdentifierSpan();
}
