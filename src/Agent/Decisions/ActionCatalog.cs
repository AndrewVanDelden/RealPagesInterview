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

// D2 and D17: the catalog keyed on persona and lifecycle stage. The table in this file is the
// default, and a rules file can replace it. Create is the one gate every catalog goes through,
// Default included, so a malformed table is a failure with a message rather than a wrong
// action at run time.
public sealed class ActionCatalog
{
    private const string GenericRowLabel = "Generic catalog row";

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
    // only the branch its record showed; the other branch falls to the generic row. Every
    // hold-out record below states no move date, so each sets its row's long branch (A7).
    public static ActionCatalog Default { get; } = Create(
        new GenericActionRow(
            new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon"),
            new NextAction(ActionTypes.FollowUpInDays, Value: 3)),
        [
            // Short: sample 1, 32 days out. Long: hold-out prospect_consent_block_sms_fallback_email,
            // whose label names the cadence for the long horizon.
            new ActionCatalogRow(
                "prospect",
                "new",
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon")),
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_long_horizon"))),

            // Sample 2, 68 days out.
            new ActionCatalogRow(
                "prospect",
                "open",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 3))),

            // Hold-out prospect_no_show_reengage.
            new ActionCatalogRow(
                "prospect",
                "no_show",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.ResetCadence, "prospect_reengage"))),

            // Hold-out prospect_cancellation_manager_cross_sell.
            new ActionCatalogRow(
                "prospect",
                "cancelled_manager",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 2))),

            // Hold-out resident_renewal_90day_notice. Its label also states in_days 5, which
            // NextAction has no member for, so the reminder's day count is not emitted.
            new ActionCatalogRow(
                "resident",
                "renewal_window",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.ScheduleSmsReminder))),

            // Hold-out resident_renewal_undecided_followup. Its label also states the reply
            // mapping, which NextAction has no member for, so the mapping is not emitted.
            new ActionCatalogRow(
                "resident",
                "renewal_undecided",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.BranchOnIntent))),

            // Hold-out resident_welcome_day0.
            new ActionCatalogRow(
                "resident",
                "welcome",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 2))),

            // Hold-out resident_loyalty_engage.
            new ActionCatalogRow(
                "resident",
                "loyalty_engage",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 5))),

            // Hold-out resident_renewal_details_branch_email.
            new ActionCatalogRow(
                "resident",
                "renewal_details_requested",
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
            ? [.. ActionFailures(GenericRowLabel, "short", genericRow.Value.ShortHorizonAction), .. ActionFailures(GenericRowLabel, "long", genericRow.Value.LongHorizonAction)]
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

            failures.AddRange(StatedActionFailures(label, "short", row.ShortHorizonAction));
            failures.AddRange(StatedActionFailures(label, "long", row.LongHorizonAction));
        }

        return failures.Count == 0
            ? Result<ActionCatalog>.Success(new ActionCatalog(
                genericRow.Value,
                [.. rows.Select(numbered => numbered.Row)],
                byKey.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Row)))
            : Result<ActionCatalog>.Failure(string.Join(Environment.NewLine, failures));
    }

    // O(1): one hash lookup, then one branch read. The record's persona and stage are
    // untrusted free text (D1), so they are normalized the same way the rows were.
    public ActionCatalogMatch Resolve(string? persona, string? lifecycleStage, HorizonBranch branch)
    {
        if (persona is null || lifecycleStage is null)
        {
            return GenericMatch(branch, ActionSource.GenericRowNoMatch);
        }

        if (!_rowsByKey.TryGetValue(KeyOf(persona, lifecycleStage), out ActionCatalogRow? row))
        {
            return GenericMatch(branch, ActionSource.GenericRowNoMatch);
        }

        Option<NextAction> action = row.ActionFor(branch);

        return action.HasValue
            ? new ActionCatalogMatch(action.Value, ActionSource.CatalogRow)
            : GenericMatch(branch, ActionSource.GenericRowNoBranch);
    }

    private ActionCatalogMatch GenericMatch(HorizonBranch branch, ActionSource source) =>
        new(GenericRow.ActionFor(branch), source);

    private static (string Persona, string LifecycleStage) KeyOf(string persona, string lifecycleStage) =>
        (persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant());

    private static List<string> StatedActionFailures(string label, string branch, Option<NextAction> action) =>
        action.HasValue ? ActionFailures(label, branch, action.Value) : [];

    // The deleted NextActionPlannerOptions threw when its follow-up-days setting was not
    // positive; Create is the row's replacement gate, so the same guarantee lives here
    // instead of at a caller that could forget it.
    private static List<string> ActionFailures(string label, string branch, NextAction action)
    {
        List<string> failures = [];

        if (!ActionTypes.All.Contains(action.Type))
        {
            failures.Add($"{label}: {branch} horizon action type '{action.Type}' is unknown.");
        }

        if (action.Value is { } value && value <= 0)
        {
            failures.Add($"{label}: {branch} horizon action value {value} must be positive.");
        }

        return failures;
    }
}
