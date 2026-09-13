using System.Globalization;
using System.Text.Json;
using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// The rules file can express everything the compiled rules hold: the compiled catalog and
// send slots, written in the file's format here and read back by the loader, answer every
// compiled key, on both horizon branches and every channel, exactly as the compiled ones do.
// The file text is built from the compiled rows, so a row added to the code is covered.
public class RulesFileRoundTripTests
{
    private static readonly CommunicationChannel[] SendChannels = [CommunicationChannel.Sms, CommunicationChannel.Email, CommunicationChannel.Voice];

    [Fact]
    public void Load_TheCompiledRulesWrittenAsAFile_AnswersEveryCompiledKeyTheSameWay()
    {
        Result<DecisionRules> result = RulesFileLoader.Load(new StringReader(CompiledRulesAsFileText()));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error);
        DecisionRules loaded = result.Value;

        (string Persona, string LifecycleStage)[] keys =
        [
            .. ActionCatalog.Default.Rows.Select(row => (row.Persona, row.LifecycleStage)),
            .. SendSlotTable.Default.Rows.Select(row => (row.Persona, row.LifecycleStage)),
            ("no_such_persona", "no_such_stage"),
        ];

        Assert.All(keys, key =>
        {
            foreach (HorizonBranch branch in Enum.GetValues<HorizonBranch>())
            {
                Assert.Equal(ActionCatalog.Default.Resolve(key.Persona, key.LifecycleStage, branch), loaded.Catalog.Resolve(key.Persona, key.LifecycleStage, branch));
            }

            foreach (CommunicationChannel channel in SendChannels)
            {
                Assert.Equal(SendSlotTable.Default.Find(key.Persona, key.LifecycleStage, channel), loaded.SendSlots.Find(key.Persona, key.LifecycleStage, channel));
            }
        });
        Assert.Equal(ActionCatalog.Default.GenericRow, loaded.Catalog.GenericRow);
        Assert.Equal(ActionCatalog.Default.Rows, loaded.Catalog.Rows);
        Assert.Equal(SendSlotTable.Default.Rows, loaded.SendSlots.Rows);
    }

    // The file format spelled out independently of the loader's own types: two sections, a
    // row per catalog key with an absent branch written as null, and a slot's time as HH:mm.
    private static string CompiledRulesAsFileText() =>
        JsonSerializer.Serialize(
            new
            {
                ActionCatalog = new
                {
                    ActionCatalog.Default.GenericRow,
                    Rows = ActionCatalog.Default.Rows.Select(row => new
                    {
                        row.Persona,
                        row.LifecycleStage,
                        ShortHorizonAction = StatedOrNull(row.ShortHorizonAction),
                        LongHorizonAction = StatedOrNull(row.LongHorizonAction),
                    }),
                },
                SendSlots = SendSlotTable.Default.Rows.Select(row => new
                {
                    row.Persona,
                    row.LifecycleStage,
                    row.Channel,
                    row.DaysAfterFloorDay,
                    LocalTime = row.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                }),
            },
            AgentJsonOptions.Default);

    private static NextAction? StatedOrNull(Option<NextAction> action) => action.HasValue ? action.Value : null;
}
