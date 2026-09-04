using System.Diagnostics.CodeAnalysis;

namespace Agent.Composition;

internal static class PrimaryCtaVocabulary
{
    private static readonly IReadOnlyDictionary<string, string> CtaTypeByPrimaryCta = new Dictionary<string, string>
    {
        ["book_tour"] = "schedule_tour",
    };

    // primaryCta is nullable: assertions.constraints.primary_cta is not guaranteed present on
    // every record (confirmed by real interview data, not a hypothetical) - a case can
    // legitimately have no primary CTA constraint stated at all, distinct from having one
    // that just isn't in our small mapping table. NotNullIfNotNull tells callers the
    // converse also holds - a non-null input can never come back null - so a caller that has
    // already ruled out null (e.g. TemplateMessageComposer's upfront guard) gets that proven
    // at compile time instead of needing an incidental control-flow argument.
    [return: NotNullIfNotNull(nameof(primaryCta))]
    public static string? ToCtaType(string? primaryCta) =>
        primaryCta is null
            ? null
            : CtaTypeByPrimaryCta.TryGetValue(primaryCta, out string? ctaType) ? ctaType : primaryCta;
}
