using System.Collections.Frozen;
using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// Which row answered a lookup (playbook step 43: the fallback is defined, and firing it is
// recorded). The two generic values are different facts and the diagnostics keep them
// apart: no row exists for this persona and stage, versus a row exists but states no
// action for this horizon branch because no sample showed one.
public enum ActionSource
{
    CatalogRow,
    GenericRowNoBranch,
    GenericRowNoMatch,
}

public sealed record ActionCatalogMatch(NextAction Action, ActionSource Source);

// The action catalog, keyed on persona and lifecycle stage: a lookup is an exact key or the
// generic row, never the nearest row. The table in this file is the default, and a rules file
// can replace it. Create is the one gate every catalog goes through, Default included, so a
// malformed table is a failure with a message rather than a wrong action at run time.
public sealed class ActionCatalog
{
    private const string GenericRowLabel = "Generic catalog row";

    private const string ShortBranchLabel = "short horizon";
    private const string LongBranchLabel = "long horizon";
    private const string NoMoveDateBranchLabel = "no move date";

    private readonly FrozenDictionary<(string Persona, string LifecycleStage), ActionCatalogRow> _rowsByKey;

    private ActionCatalog(
        GenericActionRow genericRow,
        IReadOnlyList<ActionCatalogRow> rows,
        FrozenDictionary<(string, string), ActionCatalogRow> rowsByKey)
    {
        GenericRow = genericRow;
        Rows = rows;
        _rowsByKey = rowsByKey;
    }

    // The rows as they were given, in order, so the catalog can be written out and read back.
    public GenericActionRow GenericRow { get; }

    public IReadOnlyList<ActionCatalogRow> Rows { get; }

    // The rows the labeled records have evidence for, plus the generic row of A8. A row states
    // only the branches its records showed; any other branch falls to the generic row. Every
    // hold-out record below states no move date, so each sets its row's no-move-date branch (A7).
    public static ActionCatalog Default { get; } = Create(
        new GenericActionRow(
            new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon"),
            new NextAction(ActionTypes.FollowUpInDays, Value: 3)),
        [
            // Short: sample 1, 32 days out. No move date: hold-out
            // prospect_consent_block_sms_fallback_email, whose label names the long-horizon
            // cadence. That cadence is also the long branch's: its name states the horizon it is
            // for, and a prospect whose move is months away belongs in a nurture sequence, not in
            // the generic row's short follow-up.
            new ActionCatalogRow(
                "prospect",
                "new",
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon")),
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_long_horizon")),
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_long_horizon"))),

            // Long: sample 2, 68 days out. No move date: hold-out prospect_spanish_locale, followed
            // up sooner because the timeline is not yet qualified.
            new ActionCatalogRow(
                "prospect",
                "open",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 3)),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 2))),

            // Hold-out prospect_no_show_reengage.
            new ActionCatalogRow(
                "prospect",
                "no_show",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.ResetCadence, "prospect_reengage"))),

            // Hold-out prospect_cancellation_manager_cross_sell.
            new ActionCatalogRow(
                "prospect",
                "cancelled_manager",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 2))),

            // Hold-out resident_renewal_90day_notice: the text reminder in 5 days.
            new ActionCatalogRow(
                "resident",
                "renewal_window",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.ScheduleSmsReminder, InDays: 5))),

            // Hold-out resident_renewal_undecided_followup: the action each reply leads to.
            new ActionCatalogRow(
                "resident",
                "renewal_undecided",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(
                    ActionTypes.BranchOnIntent,
                    Mapping: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["yes"] = "start_esign_flow",
                        ["no"] = "exit_nurture",
                        ["details"] = "send_offer_details_email",
                    }))),

            // Hold-out resident_welcome_day0.
            new ActionCatalogRow(
                "resident",
                "welcome",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 2))),

            // Hold-out resident_loyalty_engage.
            new ActionCatalogRow(
                "resident",
                "loyalty_engage",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 5))),

            // Hold-out resident_renewal_details_branch_email.
            new ActionCatalogRow(
                "resident",
                "renewal_details_requested",
                Option<NextAction>.None(),
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.StartEsignFlow))),
        ]).Value;

    // O(n) time and space in the number of rows. Rows are numbered from 1 in the order given.
    public static Result<ActionCatalog> Create(GenericActionRow genericRow, IReadOnlyList<ActionCatalogRow> rows) =>
        Create(Result<GenericActionRow>.Success(genericRow), [.. rows.Select((row, index) => (index + 1, row))]);

    // O(n) time and space in the number of rows, each checked once. A failure lists every bad
    // row, named by its position and key, so a table with several mistakes is fixed in one pass.
    // A rules file numbers its rows by their place in the file and passes only those it could
    // read, so positions are the caller's; a generic row it could not read arrives as that
    // failure, and the rows are still checked so it is not the only one reported.
    internal static Result<ActionCatalog> Create(Result<GenericActionRow> genericRow, IReadOnlyList<(int Position, ActionCatalogRow Row)> rows)
    {
        List<string> failures = genericRow.IsSuccess
            ? [.. ActionFailures(GenericRowLabel, ShortBranchLabel, genericRow.Value.ShortHorizonAction), .. ActionFailures(GenericRowLabel, LongBranchLabel, genericRow.Value.LongHorizonAction)]
            : [genericRow.Error];

        var byKey = new Dictionary<(string Persona, string LifecycleStage), (int Position, ActionCatalogRow Row)>();

        foreach ((int position, ActionCatalogRow row) in rows)
        {
            (string Persona, string LifecycleStage) key = KeyOf(row.Persona, row.LifecycleStage);
            string label = $"Catalog row {position} ({key.Persona}/{key.LifecycleStage})";

            if (key.Persona.Length == 0)
            {
                failures.Add($"{label}: persona is blank.");
            }

            if (key.LifecycleStage.Length == 0)
            {
                failures.Add($"{label}: lifecycle stage is blank.");
            }

            if (key.Persona.Length > 0 && key.LifecycleStage.Length > 0 && !byKey.TryAdd(key, (position, row)))
            {
                failures.Add($"{label}: duplicates catalog row {byKey[key].Position}.");
            }

            failures.AddRange(StatedActionFailures(label, ShortBranchLabel, row.ShortHorizonAction));
            failures.AddRange(StatedActionFailures(label, LongBranchLabel, row.LongHorizonAction));
            failures.AddRange(StatedActionFailures(label, NoMoveDateBranchLabel, row.NoMoveDateAction));
        }

        return failures.Count == 0
            ? Result<ActionCatalog>.Success(new ActionCatalog(
                genericRow.Value,
                [.. rows.Select(numbered => numbered.Row)],
                byKey.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Row)))
            : Result<ActionCatalog>.Failure(string.Join(Environment.NewLine, failures));
    }

    // O(1): one hash lookup, then one branch read. The record's persona and stage are optional,
    // untrusted free text, so an absent one takes the generic row and a present one is
    // normalized the same way the rows were.
    public ActionCatalogMatch Resolve(string? persona, string? lifecycleStage, HorizonBranch branch)
    {
        if (persona is null || lifecycleStage is null)
        {
            return GenericMatch(persona, branch, ActionSource.GenericRowNoMatch);
        }

        if (!_rowsByKey.TryGetValue(KeyOf(persona, lifecycleStage), out ActionCatalogRow? row))
        {
            return GenericMatch(persona, branch, ActionSource.GenericRowNoMatch);
        }

        Option<NextAction> action = row.ActionFor(branch);

        return action.HasValue
            ? new ActionCatalogMatch(action.Value, ActionSource.CatalogRow)
            : GenericMatch(persona, branch, ActionSource.GenericRowNoBranch);
    }

    // The generic row's short action names a prospect cadence, and no evidence shows a cadence for
    // anyone else, so a record whose persona is not a prospect, an absent one included, takes the
    // generic row's long action on every branch.
    private ActionCatalogMatch GenericMatch(string? persona, HorizonBranch branch, ActionSource source) =>
        new(Personas.IsProspect(persona) ? GenericRow.ActionFor(branch) : GenericRow.LongHorizonAction, source);

    private static (string Persona, string LifecycleStage) KeyOf(string persona, string lifecycleStage) =>
        (persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant());

    private static List<string> StatedActionFailures(string label, string branch, Option<NextAction> action) =>
        action.HasValue ? ActionFailures(label, branch, action.Value) : [];

    // The deleted NextActionPlannerOptions threw when its follow-up-days setting was not
    // positive; Create is the row's replacement gate, so the same guarantee lives here
    // instead of at a caller that could forget it, and a reminder's day count holds to it too.
    private static List<string> ActionFailures(string label, string branch, NextAction action)
    {
        List<string> failures = [];

        if (!ActionTypes.All.Contains(action.Type))
        {
            failures.Add($"{label}: {branch} action type '{action.Type}' is unknown.");
        }

        if (action.Value is { } value && value <= 0)
        {
            failures.Add($"{label}: {branch} action value {value} must be positive.");
        }

        if (action.InDays is { } inDays && inDays <= 0)
        {
            failures.Add($"{label}: {branch} action in_days {inDays} must be positive.");
        }

        return failures;
    }
}
