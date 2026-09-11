using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// The send-slot table is a value with one gate, Create, which the compiled default goes
// through like any other table. A row the scheduler could never use, or one that would move
// the send before its floor, is refused, and every bad row is named in the one failure.
public class SendSlotTableTests
{
    private static readonly TimeOnly NineOhFive = new(9, 5);

    [Fact]
    public void Create_SeveralBadRows_NamesEveryOneByPositionAndKey()
    {
        Result<SendSlotTable> result = SendSlotTable.Create(
        [
            new SendSlotRow(" ", "new", CommunicationChannel.Email, 0, NineOhFive),
            new SendSlotRow("prospect", "", CommunicationChannel.Email, 0, NineOhFive),
            new SendSlotRow("prospect", "new", CommunicationChannel.None, 0, NineOhFive),
            new SendSlotRow("prospect", "new", CommunicationChannel.Unknown, 0, NineOhFive),
            new SendSlotRow("prospect", "new", (CommunicationChannel)99, 0, NineOhFive),
            new SendSlotRow("prospect", "open", CommunicationChannel.Sms, -1, NineOhFive),
            new SendSlotRow("resident", "welcome", CommunicationChannel.Email, 366, NineOhFive),
            new SendSlotRow("prospect", "new", CommunicationChannel.Email, 0, NineOhFive),
            new SendSlotRow(" Prospect", "NEW ", CommunicationChannel.Email, 1, NineOhFive),
        ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [
                "Send slot row 1 (/new/email): persona is blank.",
                "Send slot row 2 (prospect//email): lifecycle stage is blank.",
                "Send slot row 3 (prospect/new/none): channel is not one of sms, email, voice.",
                "Send slot row 4 (prospect/new/unknown): channel is not one of sms, email, voice.",
                "Send slot row 5 (prospect/new/99): channel is not one of sms, email, voice.",
                "Send slot row 6 (prospect/open/sms): day offset -1 is negative.",
                "Send slot row 7 (resident/welcome/email): day offset 366 is more than 365.",
                "Send slot row 9 (prospect/new/email): duplicates send slot row 8.",
            ],
            result.Error.Split(Environment.NewLine));
    }

    // A year after the floor is the furthest a row may move the send: past that it is not a
    // next message, and the bound keeps the scheduler's date arithmetic in range.
    [Fact]
    public void Create_DayOffsetOfAYear_IsAccepted()
    {
        Result<SendSlotTable> result = SendSlotTable.Create([new SendSlotRow("resident", "welcome", CommunicationChannel.Email, 365, NineOhFive)]);

        Assert.True(result.IsSuccess);
    }

    // The channel is part of the key, so one persona and stage may state a slot per channel.
    [Fact]
    public void Create_SamePersonaAndStageOnTwoChannels_KeepsBothRows()
    {
        var emailRow = new SendSlotRow("prospect", "new", CommunicationChannel.Email, 0, NineOhFive);
        var smsRow = new SendSlotRow("prospect", "new", CommunicationChannel.Sms, 1, new TimeOnly(8, 0));

        SendSlotTable table = SendSlotTable.Create([emailRow, smsRow]).Value;

        Assert.Equal(emailRow, table.Find("prospect", "new", CommunicationChannel.Email).Value);
        Assert.Equal(smsRow, table.Find("prospect", "new", CommunicationChannel.Sms).Value);
        Assert.False(table.Find("prospect", "new", CommunicationChannel.Voice).HasValue);
    }

    [Fact]
    public void Create_NoRows_IsATableWhereEveryKeyFallsToTheChannelHour()
    {
        SendSlotTable table = SendSlotTable.Create([]).Value;

        Assert.Empty(table.Rows);
        Assert.False(table.Find("prospect", "new", CommunicationChannel.Email).HasValue);
    }
}
