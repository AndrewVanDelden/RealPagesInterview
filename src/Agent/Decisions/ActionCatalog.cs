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

// D2 and D17: the catalog keyed on persona and lifecycle stage, compiled into this file.
// Create is the one gate every catalog goes through, Default included, so a malformed
// table is a failure with a message rather than a wrong action at run time.
public sealed class ActionCatalog
{
    private readonly GenericActionRow _genericRow;
    private readonly FrozenDictionary<(string Persona, string LifecycleStage), ActionCatalogRow> _rowsByKey;

    private ActionCatalog(GenericActionRow genericRow, FrozenDictionary<(string, string), ActionCatalogRow> rowsByKey)
    {
        _genericRow = genericRow;
        _rowsByKey = rowsByKey;
    }

    // The rows DESIGN.md section 3 has evidence for, plus the generic row of A8. Sample 1 is
    // a prospect at new, 32 days out, and sets that row's short branch; sample 2 is a
    // prospect at open, 68 days out, and sets that row's long branch. Neither row's other
    // branch was ever shown, so neither states one.
    public static ActionCatalog Default { get; } = Create(
        new GenericActionRow(
            new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon"),
            new NextAction(ActionTypes.FollowUpInDays, Value: 3)),
        [
            new ActionCatalogRow(
                "prospect",
                "new",
                Option<NextAction>.Some(new NextAction(ActionTypes.StartCadence, "prospect_welcome_short_horizon")),
                Option<NextAction>.None()),
            new ActionCatalogRow(
                "prospect",
                "open",
                Option<NextAction>.None(),
                Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 3))),
        ]).Value;

    // O(n) in the number of rows, each validated once; n is the table in this file, not
    // input. A failure names the offending row so the message identifies it without a
    // debugger.
    public static Result<ActionCatalog> Create(GenericActionRow genericRow, IReadOnlyList<ActionCatalogRow> rows)
    {
        string? genericType = UnknownType(genericRow.ShortHorizonAction) ?? UnknownType(genericRow.LongHorizonAction);

        if (genericType is not null)
        {
            return Result<ActionCatalog>.Failure($"Generic catalog row: unknown action type '{genericType}'.");
        }

        int? genericInvalidValue = NonPositiveValue(genericRow.ShortHorizonAction) ?? NonPositiveValue(genericRow.LongHorizonAction);

        if (genericInvalidValue is not null)
        {
            return Result<ActionCatalog>.Failure($"Generic catalog row: action value {genericInvalidValue} must be positive.");
        }

        var byKey = new Dictionary<(string Persona, string LifecycleStage), ActionCatalogRow>();

        foreach (ActionCatalogRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Persona))
            {
                return Result<ActionCatalog>.Failure("Catalog row: persona is blank.");
            }

            if (string.IsNullOrWhiteSpace(row.LifecycleStage))
            {
                return Result<ActionCatalog>.Failure($"Catalog row for persona '{row.Persona}': lifecycle stage is blank.");
            }

            (string Persona, string LifecycleStage) key = KeyOf(row.Persona, row.LifecycleStage);

            if (!byKey.TryAdd(key, row))
            {
                return Result<ActionCatalog>.Failure($"Duplicate catalog row for {key.Persona}/{key.LifecycleStage}.");
            }

            string? rowType = UnknownStatedType(row.ShortHorizonAction) ?? UnknownStatedType(row.LongHorizonAction);

            if (rowType is not null)
            {
                return Result<ActionCatalog>.Failure($"Catalog row for {key.Persona}/{key.LifecycleStage}: unknown action type '{rowType}'.");
            }

            int? rowInvalidValue = NonPositiveStatedValue(row.ShortHorizonAction) ?? NonPositiveStatedValue(row.LongHorizonAction);

            if (rowInvalidValue is not null)
            {
                return Result<ActionCatalog>.Failure($"Catalog row for {key.Persona}/{key.LifecycleStage}: action value {rowInvalidValue} must be positive.");
            }
        }

        return Result<ActionCatalog>.Success(new ActionCatalog(genericRow, byKey.ToFrozenDictionary()));
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
        new(_genericRow.ActionFor(branch), source);

    private static (string Persona, string LifecycleStage) KeyOf(string persona, string lifecycleStage) =>
        (persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant());

    private static string? UnknownStatedType(Option<NextAction> action) =>
        action.HasValue ? UnknownType(action.Value) : null;

    private static string? UnknownType(NextAction action) =>
        ActionTypes.All.Contains(action.Type) ? null : action.Type;

    // The deleted NextActionPlannerOptions threw when its follow-up-days setting was not
    // positive; Create is the row's replacement gate, so the same guarantee lives here
    // instead of at a caller that could forget it.
    private static int? NonPositiveStatedValue(Option<NextAction> action) =>
        action.HasValue ? NonPositiveValue(action.Value) : null;

    private static int? NonPositiveValue(NextAction action) =>
        action.Value is { } value && value <= 0 ? value : null;
}
