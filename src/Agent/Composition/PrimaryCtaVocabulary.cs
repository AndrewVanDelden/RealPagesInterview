namespace Agent.Composition;

internal static class PrimaryCtaVocabulary
{
    private static readonly IReadOnlyDictionary<string, string> CtaTypeByPrimaryCta = new Dictionary<string, string>
    {
        ["book_tour"] = "schedule_tour",
    };

    public static string ToCtaType(string primaryCta) =>
        CtaTypeByPrimaryCta.TryGetValue(primaryCta, out string? ctaType) ? ctaType : primaryCta;
}
