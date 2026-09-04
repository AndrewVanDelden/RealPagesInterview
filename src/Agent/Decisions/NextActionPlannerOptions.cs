namespace Agent.Decisions;

public sealed record NextActionPlannerOptions(
    int ShortHorizonThresholdDays = 45,
    string ShortHorizonCadenceName = "prospect_welcome_short_horizon",
    int LongHorizonFollowUpDays = 3);
