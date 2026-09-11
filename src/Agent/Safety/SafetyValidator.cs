using System.Text.RegularExpressions;
using Agent.Domain;

namespace Agent.Safety;

// Four keyword and pattern proxies, not a complete fair-housing or PII compliance system
// (playbook step 63). A 37-input probe showed what they miss: semantic paraphrase
// (docs/CODE_REVIEW.md); letter spacing and interior punctuation, since matching across
// separators would make "color" fire on unrelated letters; Cyrillic homoglyphs, since this
// system's own composer writes the text, not an adversary who controls the bytes (A18).
// Each check answers for itself and all four are hard gates, each a legal exposure: fair
// housing law, the opt-out that makes a message lawful to send, and an identifier leaked into a
// channel the recipient does not control. Opt-out is OptOutInstructions, the scorer's too.
public sealed partial class SafetyValidator : ISafetyValidator
{
    private const string SocialSecurityNumberDetail =
        "Body appears to contain a leaked personal identifier (Social Security number pattern).";

    private const string LongDigitRunDetail =
        "Body appears to contain a leaked personal identifier (long numeric sequence).";

    // O(n) in the combined subject and body length: each check is a bounded number of
    // single passes over that text. The introducer-span strip is computed once, here, and
    // shared by SocialSecurityNumberCheck and LongDigitRunCheck rather than each repeating
    // the same pass over the same text: SocialSecurityNumberCheck always runs, so the strip
    // is never wasted work.
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

    // Gated on include_opt_out_instructions: transactional exemptions are real, and only the
    // record knows whether this message is one.
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

    // Unconditional: no leasing message legitimately carries a Social Security number, so a
    // record's no_pii_leak: false says it does not need the heuristic, not that it consents to
    // a leak. This check owns any nine-digit span (a word boundary after exactly nine digits;
    // LongDigitRunPattern needs thirteen), so one leak is never counted twice. The
    // confirmation-and-reference span exempts here too: a bare nine-digit confirmation number is
    // the same false positive as a fourteen-digit one, and exempting it on only one identifier
    // check would be the inconsistency rather than the fix.
    // O(n) in the text length: one pattern pass over the already-stripped text.
    private static SafetyCheckResult SocialSecurityNumberCheck(string identifierScannable) =>
        SocialSecurityNumberPattern().IsMatch(identifierScannable)
            ? SafetyCheckResult.Failed(SafetyCheck.SocialSecurityNumber, [SocialSecurityNumberDetail])
            : SafetyCheckResult.Passed(SafetyCheck.SocialSecurityNumber);

    // Gated on no_pii_leak: this proxy also matches a legitimate long identifier (a
    // confirmation number, a tour reference), so a record carrying one needs a way to say so.
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

    // Unconditional, and NoSensitiveDiscrimination is never read: fair housing law has no
    // legitimate per-case opt-out. Every distinct matched term is reported, not the first, so
    // six protected-class terms are six violations when safety_violations_max is scored.
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

    // Spans, never terms: a matched span is removed from the scanned copy, so "disability" and
    // "color" still fire elsewhere in the same message. The disclosure ("do/does not discriminate"
    // to the sentence end) is the Equal Housing Opportunity sentence a compliant message carries; its
    // six terms would suppress it. It stops at a sentence end or a comma before but, yet, however,
    // although or though (the disclosure is a comma list, but a clause tacked on is not part of it).
    // Each other span is a legitimate use of a term: disability accommodations, wheelchair accessible
    // (matches no current term; SafetyValidatorAllowListTests pins that), color scheme, a 5K race,
    // gender-neutral restrooms, a race simulator.
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

    // Hyphens throughout, spaces throughout, or nine bare digits, so "123 45 6789" and
    // "123456789" are caught as well as the hyphenated form. Three alternatives rather than one
    // shape with two optional separators: \b\d{3}[- ]?\d{2}[- ]?\d{4}\b read ZIP+4 "75201-1234"
    // as a Social Security number (first separator absent, second present), suppressing a
    // property address through a gate no record can switch off. A five-and-four pair fits none of
    // the three shapes, so it stops matching by construction rather than by an exemption.
    [GeneratedRegex(@"\b(?:\d{3}-\d{2}-\d{4}|\d{3} \d{2} \d{4}|\d{9})\b")]
    private static partial Regex SocialSecurityNumberPattern();

    // Same 13-19 total digit threshold as a bare unbroken run, but tolerant of the
    // space/dash grouping real formatted numbers (e.g. credit cards) actually use.
    [GeneratedRegex(@"\b\d(?:[- ]?\d){12,18}\b")]
    private static partial Regex LongDigitRunPattern();

    // A fourteen-digit confirmation number is not a leak, so an introduced identifier is an
    // exempt span rather than a loosened pattern. The introducer reaches at most 20
    // characters and never across a sentence terminator, so a number in the next sentence
    // is not exempted by a confirmation mentioned in this one.
    //
    // The leading \b is load-bearing: without it, "reference" matches starting inside a
    // longer word like "preference", exempting a number that followed only by coincidence
    // of spelling, not because anything actually introduced it.
    [GeneratedRegex(@"\b(?:confirmation(?:\s+number)?|reference)[^.!?\d]{0,20}\d(?:[- ]?\d)*", RegexOptions.IgnoreCase)]
    private static partial Regex IntroducedIdentifierSpan();
}
