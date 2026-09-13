using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// A rules file is operator input from outside the program. It replaces the compiled catalog
// and send slots whole, and it is checked row by row: every bad row is in the one failure,
// named by its 1-based position in its array and by its key, and a file that is not JSON in
// the file's shape fails with a position and nothing the file wrote.
public class RulesFileLoaderTests
{
    private const string NotInRowFormat =
        "does not match the row format (a value of the wrong type, a required member missing, or a member the format does not define).";

    private const string ValidGenericRow = """
        {
          "short_horizon_action": { "type": "start_cadence", "name": "welcome" },
          "long_horizon_action": { "type": "follow_up_in_days", "value": 4 }
        }
        """;

    private static Result<DecisionRules> Load(string text) => RulesFileLoader.Load(new StringReader(text));

    private static string[] FailureLines(Result<DecisionRules> result)
    {
        Assert.False(result.IsSuccess);
        return result.Error.Split(Environment.NewLine);
    }

    // The file replaces the compiled rules rather than adding to them: a key only the compiled
    // tables hold answers from the file's generic row and the channel's hour.
    [Fact]
    public void Load_AValidFile_ReturnsItsCatalogAndSlotsInPlaceOfTheCompiledOnes()
    {
        Result<DecisionRules> result = Load($$"""
            {
              "action_catalog": {
                "generic_row": {{ValidGenericRow}},
                "rows": [
                  { "persona": "prospect", "lifecycle_stage": "toured", "short_horizon_action": null, "long_horizon_action": { "type": "reset_cadence", "name": "after_tour" } }
                ]
              },
              "send_slots": [
                { "persona": "prospect", "lifecycle_stage": "toured", "channel": "sms", "days_after_floor_day": 2, "local_time": "08:45" }
              ]
            }
            """);

        Assert.True(result.IsSuccess);
        DecisionRules rules = result.Value;
        Assert.Equal(new ActionCatalogMatch(new NextAction(ActionTypes.ResetCadence, "after_tour"), ActionSource.CatalogRow), rules.Catalog.Resolve("prospect", "toured", HorizonBranch.Long));
        Assert.Equal(new ActionCatalogMatch(new NextAction(ActionTypes.StartCadence, "welcome"), ActionSource.GenericRowNoBranch), rules.Catalog.Resolve("prospect", "toured", HorizonBranch.Short));
        Assert.Equal(new ActionCatalogMatch(new NextAction(ActionTypes.FollowUpInDays, Value: 4), ActionSource.GenericRowNoMatch), rules.Catalog.Resolve("resident", "welcome", HorizonBranch.Long));
        Assert.Equal(new SendSlotRow("prospect", "toured", CommunicationChannel.Sms, 2, new TimeOnly(8, 45)), rules.SendSlots.Find("prospect", "toured", CommunicationChannel.Sms).Value);
        Assert.False(rules.SendSlots.Find("resident", "welcome", CommunicationChannel.Email).HasValue);
    }

    // Empty arrays are rules too: every lookup falls to the generic row and the channel's hour.
    [Fact]
    public void Load_NoRowsAndNoSlots_LoadsTheGenericRowAlone()
    {
        Result<DecisionRules> result = Load($$"""{ "action_catalog": { "generic_row": {{ValidGenericRow}}, "rows": [] }, "send_slots": [] }""");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Catalog.Rows);
        Assert.Empty(result.Value.SendSlots.Rows);
    }

    // The parser's own message quotes the text it choked on, so the failure carries the
    // exception type and the position only.
    [Fact]
    public void Load_TextThatIsNotJson_FailsWithAPositionAndNoContentFromTheFile()
    {
        Result<DecisionRules> result = Load("""{"action_catalog": {"generic_row": tell_nobody}}""");

        Assert.False(result.IsSuccess);
        Assert.Equal("Rules file failed to parse: JsonException: line 0, byte position 36", result.Error);
    }

    [Fact]
    public void Load_RootIsNull_Fails()
    {
        Assert.Equal(["Rules file holds null, not a rules object."], FailureLines(Load("null")));
    }

    // The file's outer shape is not a row: a section missing, null, or misspelled is a failure
    // of the whole file, with the position where the reader gave up.
    [Theory]
    [InlineData("""{ "action_catalog": { "generic_row": {}, "rows": [] } }""")]
    [InlineData("""{ "action_catalog": null, "send_slots": [] }""")]
    [InlineData("""{ "action_catalog": { "generic_row": {}, "rows": null }, "send_slots": [] }""")]
    [InlineData("""{ "action_catalog": { "generic_row": {}, "rows": [] }, "send_slot": [] }""")]
    [InlineData("""[]""")]
    public void Load_OuterShapeWrong_FailsTheWholeFileWithAPosition(string text)
    {
        string[] lines = FailureLines(Load(text));

        Assert.Single(lines);
        Assert.StartsWith("Rules file failed to parse: JsonException: line 0, byte position ", lines[0], StringComparison.Ordinal);
    }

    // Rows the file could not read come first, in file order, then the catalog's own refusals.
    // Every position is the row's place in the file, so a row that could not be read does not
    // shift the numbers of the rows after it.
    [Fact]
    public void Load_BadCatalogRows_NamesEveryOneByItsPlaceInTheFileAndItsKey()
    {
        Result<DecisionRules> result = Load($$"""
            {
              "action_catalog": {
                "generic_row": {{ValidGenericRow}},
                "rows": [
                  5,
                  { "persona": "prospect", "lifecycle_stage": "new", "short_horizon_action": { "type": "start_cadence" } },
                  { "persona": " PROSPECT", "lifecycle_stage": "New" },
                  { "persona": "Resident", "lifecycle_stage": "welcome", "long_horizon_action": { "type": "follow_up_in_days", "value": "two" } },
                  { "persona": 7, "lifecycle_stage": "open" },
                  { "persona": "resident", "lifecycle_stage": "renewal", "shrot_horizon_action": { "type": "start_cadence" } },
                  null,
                  { "lifecycle_stage": "toured", "long_horizon_action": { "type": "send_postcard", "value": 0 } },
                  { "persona": "resident" }
                ]
              },
              "send_slots": []
            }
            """);

        Assert.Equal(
            [
                $"Catalog row 1 (/): {NotInRowFormat}",
                $"Catalog row 4 (resident/welcome): {NotInRowFormat}",
                $"Catalog row 5 (/open): {NotInRowFormat}",
                $"Catalog row 6 (resident/renewal): {NotInRowFormat}",
                "Catalog row 7 (/): is null.",
                "Catalog row 3 (prospect/new): duplicates catalog row 2.",
                "Catalog row 8 (/toured): persona is blank.",
                "Catalog row 8 (/toured): long horizon action type 'send_postcard' is unknown.",
                "Catalog row 8 (/toured): long horizon action value 0 must be positive.",
                "Catalog row 9 (resident/): lifecycle stage is blank.",
            ],
            FailureLines(result));
    }

    // A generic row that cannot be read does not hide the rest: the rows and the slots are
    // still checked and reported in the same failure.
    [Fact]
    public void Load_UnreadableGenericRowAndBadRows_ReportsEveryOne()
    {
        Result<DecisionRules> result = Load("""
            {
              "action_catalog": {
                "generic_row": { "short_horizon_action": { "type": "start_cadence" } },
                "rows": [ { "persona": "prospect", "lifecycle_stage": "new", "short_horizon_action": { "type": "send_postcard" } } ]
              },
              "send_slots": [ { "persona": "prospect", "lifecycle_stage": "new", "channel": "none", "days_after_floor_day": 0, "local_time": "09:00" } ]
            }
            """);

        Assert.Equal(
            [
                $"Generic catalog row: {NotInRowFormat}",
                "Catalog row 1 (prospect/new): short horizon action type 'send_postcard' is unknown.",
                "Send slot row 1 (prospect/new/none): channel is not one of sms, email, voice.",
            ],
            FailureLines(result));
    }

    [Fact]
    public void Load_NullGenericRow_IsNamed()
    {
        Result<DecisionRules> result = Load("""{ "action_catalog": { "generic_row": null, "rows": [] }, "send_slots": [] }""");

        Assert.Equal(["Generic catalog row: is null."], FailureLines(result));
    }

    // A slot row the file could not turn into a slot comes first, in file order, then the
    // table's own refusals. The local time is 24-hour HH:mm, the minute being the rule's grain.
    [Fact]
    public void Load_BadSlotRows_NamesEveryOneByItsPlaceInTheFileAndItsKey()
    {
        Result<DecisionRules> result = Load($$"""
            {
              "action_catalog": { "generic_row": {{ValidGenericRow}}, "rows": [] },
              "send_slots": [
                { "persona": "prospect", "lifecycle_stage": "new", "days_after_floor_day": 0, "local_time": "09:05" },
                { "persona": "prospect", "lifecycle_stage": "open", "channel": "sms", "local_time": "09:20" },
                { "persona": "prospect", "lifecycle_stage": "no_show", "channel": "sms", "days_after_floor_day": 0 },
                { "persona": "prospect", "lifecycle_stage": "toured", "channel": "email", "days_after_floor_day": 0, "local_time": "25:00" },
                { "persona": "resident", "lifecycle_stage": "welcome", "channel": "email", "days_after_floor_day": 1, "local_time": "09:30:00" },
                { "persona": "resident", "lifecycle_stage": "renewal_window", "channel": "email", "days_after_floor_day": 0, "local_time": "10:30am" },
                { "persona": "resident", "lifecycle_stage": "loyalty_engage", "channel": "none", "days_after_floor_day": 3, "local_time": "11:00" },
                { "persona": "resident", "lifecycle_stage": "loyalty_engage", "channel": "fax", "days_after_floor_day": 3, "local_time": "11:00" },
                { "persona": "resident", "lifecycle_stage": "renewal_undecided", "channel": "sms", "days_after_floor_day": -5, "local_time": "09:00" },
                { "persona": "  ", "lifecycle_stage": "renewal_details_requested", "channel": "email", "days_after_floor_day": 5, "local_time": "09:10" },
                { "persona": "prospect", "lifecycle_stage": "cancelled_manager", "channel": "email", "days_after_floor_day": 0, "local_time": "13:00" },
                { "persona": "Prospect ", "lifecycle_stage": " CANCELLED_MANAGER", "channel": "EMAIL", "days_after_floor_day": 2, "local_time": "14:00" },
                { "persona": "prospect", "lifecycle_stage": "new", "channel": "sms", "days_after_floor_day": 1.5, "local_time": "09:00" },
                { "persona": "prospect", "lifecycle_stage": "new", "channel": "sms", "days_after_floor_day": 1, "local_time": "09:00", "quiet_hours": true },
                { "lifecycle_stage": "welcome", "channel": "sms", "days_after_floor_day": 0, "local_time": "09:00" },
                { "persona": "resident", "channel": "voice", "days_after_floor_day": 0, "local_time": "09:00" },
                { "persona": "resident", "lifecycle_stage": "welcome", "channel": "sms" }
              ]
            }
            """);

        Assert.Equal(
            [
                "Send slot row 1 (prospect/new/): channel is absent.",
                "Send slot row 2 (prospect/open/sms): day offset is absent.",
                "Send slot row 3 (prospect/no_show/sms): local time is absent.",
                "Send slot row 4 (prospect/toured/email): local time is not a 24-hour HH:mm time.",
                "Send slot row 5 (resident/welcome/email): local time is not a 24-hour HH:mm time.",
                "Send slot row 6 (resident/renewal_window/email): local time is not a 24-hour HH:mm time.",
                $"Send slot row 13 (prospect/new/sms): {NotInRowFormat}",
                $"Send slot row 14 (prospect/new/sms): {NotInRowFormat}",
                "Send slot row 17 (resident/welcome/sms): day offset is absent.",
                "Send slot row 17 (resident/welcome/sms): local time is absent.",
                "Send slot row 7 (resident/loyalty_engage/none): channel is not one of sms, email, voice.",
                "Send slot row 8 (resident/loyalty_engage/unknown): channel is not one of sms, email, voice.",
                "Send slot row 9 (resident/renewal_undecided/sms): day offset -5 is negative.",
                "Send slot row 10 (/renewal_details_requested/email): persona is blank.",
                "Send slot row 12 (prospect/cancelled_manager/email): duplicates send slot row 11.",
                "Send slot row 15 (/welcome/sms): persona is blank.",
                "Send slot row 16 (resident//voice): lifecycle stage is blank.",
            ],
            FailureLines(result));
    }

    // A member stated twice would otherwise be read as its last value without a word. The
    // parser refuses it while reading the file, before any row is read on its own, so it fails
    // the whole file with the position just past the second one.
    [Fact]
    public void Load_ARowStatingAMemberTwice_FailsTheWholeFileWithAPosition()
    {
        Result<DecisionRules> result = Load("""
            { "action_catalog": { "generic_row": {}, "rows": [] },
              "send_slots": [ { "persona": "prospect", "channel": "email", "channel": "sms" } ] }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal("Rules file failed to parse: JsonException: line 1, byte position 81", result.Error);
    }
}
