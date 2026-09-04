namespace Agent.Decisions;

public sealed record NextActionPlannerOptions
{
    public NextActionPlannerOptions(
        int shortHorizonThresholdDays = 45,
        string shortHorizonCadenceName = "prospect_welcome_short_horizon",
        int longHorizonFollowUpDays = 3)
    {
        if (shortHorizonThresholdDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shortHorizonThresholdDays), shortHorizonThresholdDays, "Short horizon threshold days cannot be negative.");
        }

        if (longHorizonFollowUpDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(longHorizonFollowUpDays), longHorizonFollowUpDays, "Long horizon follow-up days must be positive.");
        }

        ShortHorizonThresholdDays = shortHorizonThresholdDays;
        ShortHorizonCadenceName = shortHorizonCadenceName;
        LongHorizonFollowUpDays = longHorizonFollowUpDays;
    }

    public int ShortHorizonThresholdDays { get; }

    public string ShortHorizonCadenceName { get; }

    public int LongHorizonFollowUpDays { get; }
}
