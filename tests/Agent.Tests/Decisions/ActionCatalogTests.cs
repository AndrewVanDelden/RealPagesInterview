using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// D17 and D18. The catalog is keyed on persona and lifecycle stage and holds one action per
// horizon branch. A branch a row does not state has no evidence behind it (A8), so it falls
// to the generic row and the match says so; a persona or stage with no row does the same.
// Create is the one gate every catalog goes through, including Default.
public class ActionCatalogTests
{
    private static readonly NextAction Cadence = new(ActionTypes.StartCadence, "prospect_welcome_short_horizon");
    private static readonly NextAction FollowUp = new(ActionTypes.FollowUpInDays, Value: 3);
    private static readonly GenericActionRow Generic = new(Cadence, FollowUp);

    private static ActionCatalogRow Row(string persona, string stage, Option<NextAction> shortHorizon, Option<NextAction> longHorizon) =>
        new(persona, stage, shortHorizon, longHorizon);

    private static ActionCatalog CatalogOf(params ActionCatalogRow[] rows) =>
        ActionCatalog.Create(Generic, rows).Value;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RowWithBlankPersona_Fails(string persona)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(Generic, [Row(persona, "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None())]);

        Assert.False(result.IsSuccess);
        Assert.Contains("persona", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RowWithBlankLifecycleStage_Fails(string stage)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(Generic, [Row("prospect", stage, Option<NextAction>.Some(Cadence), Option<NextAction>.None())]);

        Assert.False(result.IsSuccess);
        Assert.Contains("lifecycle stage", result.Error, StringComparison.Ordinal);
    }

    // The lookup compares case-insensitively, so two rows differing only by case are one key
    // with two answers, not two keys.
    [Fact]
    public void Create_DuplicateKeyDifferingOnlyByCase_Fails()
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            Generic,
            [
                Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()),
                Row("Prospect", "NEW", Option<NextAction>.None(), Option<NextAction>.Some(FollowUp)),
            ]);

        Assert.False(result.IsSuccess);
        Assert.Contains("prospect/new", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RowActionTypeOutsideVocabulary_Fails()
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            Generic,
            [Row("prospect", "new", Option<NextAction>.Some(new NextAction("send_postcard")), Option<NextAction>.None())]);

        Assert.False(result.IsSuccess);
        Assert.Contains("send_postcard", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_GenericRowActionTypeOutsideVocabulary_Fails()
    {
        Result<ActionCatalog> result = ActionCatalog.Create(new GenericActionRow(new NextAction("send_postcard"), FollowUp), []);

        Assert.False(result.IsSuccess);
        Assert.Contains("send_postcard", result.Error, StringComparison.Ordinal);
    }

    // The deleted NextActionPlannerOptions threw when longHorizonFollowUpDays was not
    // positive; Create is the row's replacement gate, so the same guarantee belongs here.
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_RowWithNonPositiveValueOnLongHorizonBranch_Fails(int value)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            Generic,
            [Row("prospect", "open", Option<NextAction>.None(), Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: value)))]);

        Assert.False(result.IsSuccess);
        Assert.Contains("prospect/open", result.Error, StringComparison.Ordinal);
    }

    // The short-horizon branch is checked first; this exercises that side of the check
    // rather than only the long-horizon side above.
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_RowWithNonPositiveValueOnShortHorizonBranch_Fails(int value)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            Generic,
            [Row("prospect", "new", Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: value)), Option<NextAction>.None())]);

        Assert.False(result.IsSuccess);
        Assert.Contains("prospect/new", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_GenericRowWithNonPositiveValueOnLongHorizonBranch_Fails(int value)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(new GenericActionRow(Cadence, new NextAction(ActionTypes.FollowUpInDays, Value: value)), []);

        Assert.False(result.IsSuccess);
        Assert.Contains("Generic catalog row", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_GenericRowWithNonPositiveValueOnShortHorizonBranch_Fails(int value)
    {
        Result<ActionCatalog> result = ActionCatalog.Create(new GenericActionRow(new NextAction(ActionTypes.FollowUpInDays, Value: value), FollowUp), []);

        Assert.False(result.IsSuccess);
        Assert.Contains("Generic catalog row", result.Error, StringComparison.Ordinal);
    }

    // Every bad row is in the one failure, each named by its 1-based position and its key, so
    // whoever fixes a rules file sees the whole list at once instead of one row per run.
    [Fact]
    public void Create_SeveralBadRows_NamesEveryOneByPositionAndKey()
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            Generic,
            [
                Row(" ", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()),
                Row("prospect", "open", Option<NextAction>.None(), Option<NextAction>.Some(FollowUp)),
                Row(" Prospect", "OPEN", Option<NextAction>.None(), Option<NextAction>.Some(new NextAction("send_postcard"))),
                Row("resident", "", Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 0)), Option<NextAction>.None()),
            ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [
                "Catalog row 1 (/new): persona is blank.",
                "Catalog row 3 (prospect/open): duplicates catalog row 2.",
                "Catalog row 3 (prospect/open): long horizon action type 'send_postcard' is unknown.",
                "Catalog row 4 (resident/): lifecycle stage is blank.",
                "Catalog row 4 (resident/): short horizon action value 0 must be positive.",
            ],
            result.Error.Split(Environment.NewLine));
    }

    [Fact]
    public void Create_GenericRowWithTwoBadBranches_NamesBoth()
    {
        Result<ActionCatalog> result = ActionCatalog.Create(
            new GenericActionRow(new NextAction("send_postcard"), new NextAction(ActionTypes.FollowUpInDays, Value: -1)),
            []);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [
                "Generic catalog row: short horizon action type 'send_postcard' is unknown.",
                "Generic catalog row: long horizon action value -1 must be positive.",
            ],
            result.Error.Split(Environment.NewLine));
    }

    [Fact]
    public void Resolve_KeyWithStatedBranch_ReturnsTheRowAction()
    {
        ActionCatalog catalog = CatalogOf(Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()));

        ActionCatalogMatch match = catalog.Resolve("prospect", "new", HorizonBranch.Short);

        Assert.Equal(Cadence, match.Action);
        Assert.Equal(ActionSource.CatalogRow, match.Source);
    }

    // A8: the samples show one branch per row, so the other branch has no evidence and the
    // generic row answers it. The match records which of the two fallbacks fired.
    [Fact]
    public void Resolve_KeyWithUnstatedBranch_FallsBackToGenericRow()
    {
        ActionCatalog catalog = CatalogOf(Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()));

        ActionCatalogMatch match = catalog.Resolve("prospect", "new", HorizonBranch.Long);

        Assert.Equal(FollowUp, match.Action);
        Assert.Equal(ActionSource.GenericRowNoBranch, match.Source);
    }

    [Fact]
    public void Resolve_KeyWithNoRow_FallsBackToGenericRow()
    {
        ActionCatalog catalog = CatalogOf(Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()));

        ActionCatalogMatch match = catalog.Resolve("resident", "renewal", HorizonBranch.Short);

        Assert.Equal(Cadence, match.Action);
        Assert.Equal(ActionSource.GenericRowNoMatch, match.Source);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "new")]
    [InlineData("prospect", null)]
    public void Resolve_AbsentPersonaOrStage_FallsBackToGenericRow(string? persona, string? stage)
    {
        ActionCatalog catalog = CatalogOf(Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()));

        ActionCatalogMatch match = catalog.Resolve(persona, stage, HorizonBranch.Short);

        Assert.Equal(ActionSource.GenericRowNoMatch, match.Source);
    }

    [Fact]
    public void Resolve_KeyDifferingByCaseAndSurroundingSpace_MatchesTheRow()
    {
        ActionCatalog catalog = CatalogOf(Row("prospect", "new", Option<NextAction>.Some(Cadence), Option<NextAction>.None()));

        ActionCatalogMatch match = catalog.Resolve("  Prospect ", "NEW", HorizonBranch.Short);

        Assert.Equal(ActionSource.CatalogRow, match.Source);
    }

    // Branches no record showed: the row does not state them, so they come from the generic
    // row and the diagnostics can say so.
    [Theory]
    [InlineData("prospect", "open", HorizonBranch.Short)]
    [InlineData("resident", "renewal_window", HorizonBranch.Short)]
    public void Default_UnobservedBranchOfAKnownRow_ComesFromTheGenericRow(string persona, string stage, HorizonBranch branch)
    {
        ActionCatalogMatch match = ActionCatalog.Default.Resolve(persona, stage, branch);

        Assert.Equal(ActionSource.GenericRowNoBranch, match.Source);
    }

    // The hold-out's rows. None of these records states a move date, so each takes the long
    // branch, and the long branch is the only one its row states.
    [Theory]
    [InlineData("prospect", "new", ActionTypes.StartCadence, "prospect_welcome_long_horizon", null)]
    [InlineData("prospect", "no_show", ActionTypes.ResetCadence, "prospect_reengage", null)]
    [InlineData("prospect", "cancelled_manager", ActionTypes.FollowUpInDays, null, 2)]
    [InlineData("resident", "renewal_window", ActionTypes.ScheduleSmsReminder, null, null)]
    [InlineData("resident", "renewal_undecided", ActionTypes.BranchOnIntent, null, null)]
    [InlineData("resident", "welcome", ActionTypes.FollowUpInDays, null, 2)]
    [InlineData("resident", "loyalty_engage", ActionTypes.FollowUpInDays, null, 5)]
    [InlineData("resident", "renewal_details_requested", ActionTypes.StartEsignFlow, null, null)]
    public void Default_HoldOutStageOnTheLongBranch_ComesFromItsRow(string persona, string stage, string type, string? name, int? value)
    {
        ActionCatalogMatch match = ActionCatalog.Default.Resolve(persona, stage, HorizonBranch.Long);

        Assert.Equal(new NextAction(type, name, value), match.Action);
        Assert.Equal(ActionSource.CatalogRow, match.Source);
    }

    [Fact]
    public void Default_UnknownPersona_ComesFromTheGenericRow()
    {
        ActionCatalogMatch match = ActionCatalog.Default.Resolve("resident", "renewal", HorizonBranch.Long);

        Assert.Equal(ActionTypes.FollowUpInDays, match.Action.Type);
        Assert.Equal(3, match.Action.Value);
        Assert.Equal(ActionSource.GenericRowNoMatch, match.Source);
    }
}
