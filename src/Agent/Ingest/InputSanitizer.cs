using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Agent.Domain;

namespace Agent.Ingest;

// A record with every text field cleaned, and the path of each field the cleaning changed.
public sealed record SanitizedInput(ProspectCase Case, IReadOnlyList<string> ChangedFields);

// Every text field of an input record is cleaned where it enters, before any decision or message reads
// it (OWASP Input Validation Cheat Sheet: validate as early as possible, normalize first, allowlist
// character categories, bound the length). Output checks still stand behind it: input cleaning is not
// the defense against injection on its own. Two kinds of field:
//   identifiers and vocabulary (task id, persona, stage, time zone, language, unit, offer id,
//   cancellation reason, loyalty status, features, required states, primary call to action): invalid
//   code units, control, format, bidirectional-control and private or unassigned characters removed,
//   NFKC normalized, whitespace collapsed, and held to a length;
//   text a person or the model reads (first name, property name, city and amenity interest): the same,
//   then markup removed with the contents of script and style elements, and only the characters that
//   field can hold kept: letters, marks, spaces and a name's punctuation for a first name, and digits
//   and a place's punctuation as well for the others.
// A field left empty, or longer than its cap, is absent, as an absent field is, except the required
// task id, which is cut to its cap. The expected outcome is the label and is not input to a decision,
// so it is left as it is. A clean record is returned as the same instance.
public static partial class InputSanitizer
{
    private const int TaskIdCap = 200;
    private const int VocabularyCap = 100;
    private const int TimeZoneCap = 64;
    private const int LanguageCap = 35;
    private const int UnitCap = 32;
    private const int FirstNameCap = 50;
    private const int PlaceCap = 120;
    private const int AmenityCap = 60;

    // Characters removed outright: they are invisible or undefined, and the bidirectional controls among
    // the format characters can make text read in an order other than the one it is stored in.
    private static readonly FrozenSet<UnicodeCategory> RemovedCategories = new[]
    {
        UnicodeCategory.Format, UnicodeCategory.PrivateUse, UnicodeCategory.OtherNotAssigned,
    }.ToFrozenSet();

    private static readonly FrozenSet<UnicodeCategory> NameCategories = new[]
    {
        UnicodeCategory.UppercaseLetter, UnicodeCategory.LowercaseLetter, UnicodeCategory.TitlecaseLetter,
        UnicodeCategory.ModifierLetter, UnicodeCategory.OtherLetter, UnicodeCategory.NonSpacingMark,
        UnicodeCategory.SpacingCombiningMark, UnicodeCategory.EnclosingMark, UnicodeCategory.SpaceSeparator,
    }.ToFrozenSet();

    private static readonly FrozenSet<UnicodeCategory> PlaceCategories = NameCategories
        .Concat([UnicodeCategory.DecimalDigitNumber, UnicodeCategory.LetterNumber, UnicodeCategory.OtherNumber])
        .ToFrozenSet();

    // An apostrophe (straight or right single quotation mark), a hyphen and a period belong in a name;
    // a place also uses a comma, an ampersand, parentheses, a slash and a number sign.
    private static readonly FrozenSet<int> NamePunctuation = new[] { 0x27, 0x2019, 0x2D, 0x2E }.ToFrozenSet();

    private static readonly FrozenSet<int> PlacePunctuation = NamePunctuation.Concat([0x2C, 0x26, 0x28, 0x29, 0x2F, 0x23]).ToFrozenSet();

    // O(n) in the total length of the record's text fields: each field is cleaned in a bounded number of
    // passes over its own characters.
    public static SanitizedInput Sanitize(ProspectCase raw)
    {
        var changed = new List<string>();

        string taskId = Field(changed, "task_id", raw.TaskId, CleanTaskId(raw.TaskId));
        string? persona = Field(changed, "persona", raw.Persona, Vocabulary(raw.Persona, VocabularyCap));
        string? stage = Field(changed, "lifecycle_stage", raw.LifecycleStage, Vocabulary(raw.LifecycleStage, VocabularyCap));

        ProspectContext? input = raw.Input;
        ProspectContext? cleanInput = input is null ? null : CleanContext(input, changed);

        CaseAssertions? assertions = raw.Assertions;
        CaseAssertions? cleanAssertions = assertions is null ? null : CleanAssertions(assertions, changed);

        if (changed.Count == 0)
        {
            return new SanitizedInput(raw, changed);
        }

        ProspectCase clean = raw with
        {
            TaskId = taskId,
            Persona = persona,
            LifecycleStage = stage,
            Input = cleanInput,
            Assertions = cleanAssertions,
        };

        return new SanitizedInput(clean, changed);
    }

    private static ProspectContext CleanContext(ProspectContext input, List<string> changed)
    {
        string? propertyName = Field(changed, "input.property_name", input.PropertyName, Text(input.PropertyName, PlaceCap, PlaceCategories, PlacePunctuation));
        string? timeZoneId = Field(changed, "input.timezone", input.TimeZoneId, Vocabulary(input.TimeZoneId, TimeZoneCap));
        string? language = Field(changed, "input.language", input.Language, Vocabulary(input.Language, LanguageCap));
        string? unit = Field(changed, "input.unit", input.Unit, Vocabulary(input.Unit, UnitCap));
        string? renewalOfferId = Field(changed, "input.renewal_offer_id", input.RenewalOfferId, Vocabulary(input.RenewalOfferId, VocabularyCap));
        string? cancellationReason = Field(changed, "input.cancellation_reason", input.CancellationReason, Vocabulary(input.CancellationReason, VocabularyCap));

        ProspectProfile? profile = input.Profile;
        ProspectProfile? cleanProfile = profile is null ? null : CleanProfile(profile, changed);

        return input with
        {
            PropertyName = propertyName,
            TimeZoneId = timeZoneId,
            Language = language,
            Unit = unit,
            RenewalOfferId = renewalOfferId,
            CancellationReason = cancellationReason,
            Profile = cleanProfile,
        };
    }

    private static ProspectProfile CleanProfile(ProspectProfile profile, List<string> changed)
    {
        string? firstName = Field(changed, "input.profile.first_name", profile.FirstName, Text(profile.FirstName, FirstNameCap, NameCategories, NamePunctuation));
        string? city = Field(changed, "input.profile.city_interest", profile.CityInterest, Text(profile.CityInterest, PlaceCap, PlaceCategories, PlacePunctuation));
        IReadOnlyList<string>? amenities = ListField(changed, "input.profile.amenity_interest", profile.AmenityInterest, item => Text(item, AmenityCap, PlaceCategories, PlacePunctuation));
        string? loyaltyStatus = Field(changed, "input.profile.loyalty_status", profile.LoyaltyStatus, Vocabulary(profile.LoyaltyStatus, VocabularyCap));
        IReadOnlyList<string>? features = ListField(changed, "input.profile.features_enablement", profile.FeaturesEnablement, item => Vocabulary(item, VocabularyCap));

        return profile with
        {
            FirstName = firstName,
            CityInterest = city,
            AmenityInterest = amenities,
            LoyaltyStatus = loyaltyStatus,
            FeaturesEnablement = features,
        };
    }

    private static CaseAssertions CleanAssertions(CaseAssertions assertions, List<string> changed)
    {
        IReadOnlyList<string?>? requiredStates = assertions.RequiredStates;
        IReadOnlyList<string?>? cleanStates = requiredStates is null
            ? null
            : Field(changed, "assertions.required_states", requiredStates, [.. requiredStates.Select(state => Vocabulary(state, VocabularyCap)).OfType<string>()]);

        CaseConstraints? constraints = assertions.Constraints;
        CaseConstraints? cleanConstraints = constraints is null
            ? null
            : constraints with
            {
                PrimaryCta = Field(changed, "assertions.constraints.primary_cta", constraints.PrimaryCta, Vocabulary(constraints.PrimaryCta, VocabularyCap)),
            };

        return assertions with { RequiredStates = cleanStates, Constraints = cleanConstraints };
    }

    // The cleaned value, and the path noted when it differs from the raw one. A list compares item by
    // item, so a list whose items are unchanged is not noted.
    private static T Field<T>(List<string> changed, string path, T raw, T clean)
    {
        bool same = raw is IEnumerable<string?> rawItems && clean is IEnumerable<string?> cleanItems
            ? rawItems.SequenceEqual(cleanItems)
            : EqualityComparer<T>.Default.Equals(raw, clean);
        if (!same)
        {
            changed.Add(path);
        }

        return same ? raw : clean;
    }

    // An item left empty or over its cap is dropped from the list.
    private static IReadOnlyList<string>? ListField(List<string> changed, string path, IReadOnlyList<string>? raw, Func<string, string?> clean) =>
        raw is null ? null : Field(changed, path, raw, [.. raw.Select(clean).OfType<string>()]);

    private static string CleanTaskId(string taskId)
    {
        string cleaned = Collapse(Normalized(WithoutUnsafeCharacters(taskId)));
        return cleaned.Length > TaskIdCap ? cleaned[..TaskIdCap] : cleaned;
    }

    private static string? Vocabulary(string? value, int cap) =>
        value is null ? null : Bounded(Collapse(Normalized(WithoutUnsafeCharacters(value))), cap);

    private static string? Text(string? value, int cap, FrozenSet<UnicodeCategory> categories, FrozenSet<int> punctuation)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = Normalized(WithoutUnsafeCharacters(value));
        string withoutMarkup = MarkupTag().Replace(ScriptOrStyleElement().Replace(normalized, " "), " ");
        return Bounded(Collapse(OnlyAllowed(withoutMarkup, categories, punctuation)), cap);
    }

    private static string? Bounded(string value, int cap) => value.Length == 0 || value.Length > cap ? null : value;

    // Invalid code units, such as a lone surrogate, are dropped, which also keeps normalization from
    // throwing; a control character becomes a space, so a line break cannot forge a new log line.
    private static string WithoutUnsafeCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf16(value.AsSpan(index), out Rune rune, out int consumed);
            index += consumed;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (status != OperationStatus.Done || RemovedCategories.Contains(category))
            {
                continue;
            }

            builder.Append(category == UnicodeCategory.Control ? " " : rune.ToString());
        }

        return builder.ToString();
    }

    private static string Normalized(string value) => value.Normalize(NormalizationForm.FormKC);

    private static string OnlyAllowed(string value, FrozenSet<UnicodeCategory> categories, FrozenSet<int> punctuation)
    {
        var builder = new StringBuilder(value.Length);
        foreach (Rune rune in value.EnumerateRunes())
        {
            bool allowed = categories.Contains(Rune.GetUnicodeCategory(rune)) || punctuation.Contains(rune.Value);
            builder.Append(allowed ? rune.ToString() : " ");
        }

        return builder.ToString();
    }

    private static string Collapse(string value) => Whitespace().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleElement();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex MarkupTag();
}
