using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// The send lands on the first day at or after max(reference time, last_interaction) in the
// record's timezone, moved by the slot row's day offset and set to its local time; a persona,
// stage and channel with no row keeps the channel's hour (sms 09:00, email 10:00, voice 09:00)
// on that first day. An unknown or absent timezone is UTC. The scheduler also returns which
// input was the floor, the zone it computed in, how the slot resolved, and which rule set it.
public class SendSchedulerTests
{
    private static readonly SendScheduler Scheduler = new();
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2025-12-09T00:00:00-06:00");

    [Fact]
    public void Resolve_SmsSampleCase_ResolvesToNineAmLocalOnTheReferenceDay()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago", CommunicationChannel.Sms, "prospect", "new");

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(TimeSpan.FromHours(-6), scheduled.SendAt.Offset);
        Assert.Equal(SendSlotSource.ChannelDefault, scheduled.Source);
    }

    [Fact]
    public void Resolve_EmailSampleCase_ResolvesToTenAmLocalOnTheReferenceDay()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-06T11:30:00Z"), "America/Chicago", CommunicationChannel.Email, "prospect", "open");

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(SendSlotSource.ChannelDefault, scheduled.Source);
    }

    // One row per hold-out record whose label is not the channel's hour on the floor day. Each
    // is that record's own persona, stage, chosen channel and label; none of the nine records
    // states a last interaction after the reference time, so the reference time is the floor.
    [Theory]
    [InlineData("prospect", "no_show", CommunicationChannel.Sms, "2025-12-09T09:15:00-06:00")]
    [InlineData("prospect", "cancelled_manager", CommunicationChannel.Email, "2025-12-09T13:00:00-06:00")]
    [InlineData("prospect", "new", CommunicationChannel.Email, "2025-12-09T09:05:00-06:00")]
    [InlineData("prospect", "open", CommunicationChannel.Sms, "2025-12-09T09:20:00-06:00")]
    [InlineData("resident", "renewal_window", CommunicationChannel.Email, "2025-12-09T10:30:00-06:00")]
    [InlineData("resident", "renewal_undecided", CommunicationChannel.Sms, "2025-12-14T09:00:00-06:00")]
    [InlineData("resident", "welcome", CommunicationChannel.Email, "2025-12-10T09:30:00-06:00")]
    [InlineData("resident", "loyalty_engage", CommunicationChannel.Email, "2025-12-12T11:00:00-06:00")]
    [InlineData("resident", "renewal_details_requested", CommunicationChannel.Email, "2025-12-14T09:10:00-06:00")]
    public void Resolve_PersonaStageAndChannelWithASlotRow_SendsAtTheRowsDayAndLocalTime(string persona, string lifecycleStage, CommunicationChannel channel, string expectedSendAt)
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", channel, persona, lifecycleStage);

        Assert.Equal(DateTimeOffset.Parse(expectedSendAt), scheduled.SendAt);
        Assert.Equal(TimeSpan.FromHours(-6), scheduled.SendAt.Offset);
        Assert.Equal(SendSlotSource.SlotRow, scheduled.Source);
        Assert.Equal(ScheduleFloor.ReferenceTime, scheduled.Floor);
    }

    // A row states only the channel its record was sent on. The same persona and stage on
    // another channel was never shown, so it keeps that channel's hour on the floor day.
    [Theory]
    [InlineData("prospect", "new", CommunicationChannel.Sms, "2025-12-09T09:00:00-06:00")]
    [InlineData("prospect", "open", CommunicationChannel.Email, "2025-12-09T10:00:00-06:00")]
    [InlineData("resident", "welcome", CommunicationChannel.Sms, "2025-12-09T09:00:00-06:00")]
    public void Resolve_RowForAnotherChannel_UsesTheChannelHourOnTheFloorDay(string persona, string lifecycleStage, CommunicationChannel channel, string expectedSendAt)
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", channel, persona, lifecycleStage);

        Assert.Equal(DateTimeOffset.Parse(expectedSendAt), scheduled.SendAt);
        Assert.Equal(SendSlotSource.ChannelDefault, scheduled.Source);
    }

    [Theory]
    [InlineData("prospect", "toured")]
    [InlineData(null, "welcome")]
    [InlineData("resident", null)]
    [InlineData(null, null)]
    public void Resolve_NoRowForPersonaAndStage_UsesTheChannelHourOnTheFloorDay(string? persona, string? lifecycleStage)
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email, persona, lifecycleStage);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(SendSlotSource.ChannelDefault, scheduled.Source);
    }

    // Persona and stage are free text on the record, so they match a row the way the action
    // catalog matches them: trimmed and case-insensitive.
    [Fact]
    public void Resolve_PersonaAndStageInAnotherCaseWithSpaces_MatchTheRow()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email, " Resident ", "WELCOME");

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:30:00-06:00"), scheduled.SendAt);
        Assert.Equal(SendSlotSource.SlotRow, scheduled.Source);
    }

    // The day offset counts from the floor's day, so a last interaction after the reference
    // time moves a row's send the way it moves the channel hour's.
    [Fact]
    public void Resolve_RowWithADayOffsetAndALaterLastInteraction_CountsFromTheLastInteractionsDay()
    {
        var lastInteraction = new DateTimeOffset(2025, 12, 10, 8, 0, 0, TimeSpan.FromHours(-6));

        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, lastInteraction, "America/Chicago", CommunicationChannel.Email, "resident", "welcome");

        Assert.Equal(DateTimeOffset.Parse("2025-12-11T09:30:00-06:00"), scheduled.SendAt);
        Assert.Equal(ScheduleFloor.LastInteraction, scheduled.Floor);
    }

    // A same-day row whose local time has already passed at the floor sends the next day at
    // that time, never before the floor.
    [Fact]
    public void Resolve_SameDayRowWhoseTimeHasPassed_RollsToTheNextDayAtTheRowsTime()
    {
        DateTimeOffset afternoonReference = DateTimeOffset.Parse("2025-12-09T15:00:00-06:00");

        ScheduledSend scheduled = Scheduler.Resolve(afternoonReference, null, "America/Chicago", CommunicationChannel.Email, "prospect", "cancelled_manager");

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T13:00:00-06:00"), scheduled.SendAt);
    }

    // The day offset is counted in local days, not added to an instant: five days after
    // 2026-03-03 is the day Chicago springs forward, and 09:00 that day is under the daylight
    // offset. Adding five times 24 hours to the standard-time instant would land at 10:00.
    [Fact]
    public void Resolve_DayOffsetLandsOnADaylightSavingTransitionDay_KeepsTheLocalTimeUnderTheNewOffset()
    {
        DateTimeOffset standardTimeReference = DateTimeOffset.Parse("2026-03-03T00:00:00-06:00");

        ScheduledSend scheduled = Scheduler.Resolve(standardTimeReference, null, "America/Chicago", CommunicationChannel.Sms, "resident", "renewal_undecided");

        Assert.Equal(DateTimeOffset.Parse("2026-03-08T09:00:00-05:00"), scheduled.SendAt);
        Assert.Equal(TimeSpan.FromHours(-5), scheduled.SendAt.Offset);
        Assert.Equal(SlotResolution.Exact, scheduled.Slot);
    }

    [Fact]
    public void Resolve_LastInteractionAfterTheReferenceSlot_IsTheFloor()
    {
        var lateNightLastInteraction = new DateTimeOffset(2025, 12, 9, 23, 30, 0, TimeSpan.FromHours(-6));

        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, lateNightLastInteraction, "America/Chicago", CommunicationChannel.Sms, null, null);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(ScheduleFloor.LastInteraction, scheduled.Floor);
    }

    [Fact]
    public void Resolve_ReferenceTimePastTheSlot_RollsToTheNextDay()
    {
        DateTimeOffset afternoonReference = DateTimeOffset.Parse("2025-12-09T15:00:00-06:00");

        ScheduledSend scheduled = Scheduler.Resolve(afternoonReference, null, "America/Chicago", CommunicationChannel.Sms, null, null);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), scheduled.SendAt);
    }

    [Fact]
    public void Resolve_AbsentLastInteraction_UsesTheReferenceTimeAlone()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email, null, null);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(ScheduleFloor.ReferenceTime, scheduled.Floor);
    }

    // A last interaction before the reference time does not move the floor: the send is
    // still counted from the reference time, so the floor named is the reference time.
    [Fact]
    public void Resolve_LastInteractionBeforeTheReferenceTime_NamesTheReferenceTimeAsTheFloor()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-11-01T08:00:00Z"), "America/Chicago", CommunicationChannel.Sms, null, null);

        Assert.Equal(ScheduleFloor.ReferenceTime, scheduled.Floor);
    }

    [Fact]
    public void Resolve_VoiceChannel_ResolvesToNineAmLocal()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Voice, null, null);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(scheduled.SendAt.DateTime));
    }

    [Fact]
    public void Resolve_UnknownTimeZoneId_ResolvesInUtc()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "Not/AZone", CommunicationChannel.Sms, null, null);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00Z"), scheduled.SendAt);
        Assert.Equal(TimeSpan.Zero, scheduled.SendAt.Offset);
        Assert.Equal(TimeZoneInfo.Utc.Id, scheduled.TimeZoneId);
    }

    [Fact]
    public void Resolve_AbsentTimeZoneId_ResolvesInUtc()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, null, CommunicationChannel.Sms, null, null);

        Assert.Equal(TimeSpan.Zero, scheduled.SendAt.Offset);
        Assert.Equal(TimeZoneInfo.Utc.Id, scheduled.TimeZoneId);
    }

    // The synthetic set's daylight-saving record: the send day is the day the zone springs
    // forward, and 10:00 is after the transition, so the slot itself is ordinary and the
    // offset is the daylight one.
    [Fact]
    public void Resolve_SendDayIsADaylightSavingTransitionDay_UsesTheOffsetInForceAtTheSlot()
    {
        DateTimeOffset transitionDayReference = DateTimeOffset.Parse("2026-03-08T07:30:00Z");

        ScheduledSend scheduled = Scheduler.Resolve(transitionDayReference, null, "America/Chicago", CommunicationChannel.Email, null, null);

        Assert.Equal(DateTimeOffset.Parse("2026-03-08T10:00:00-05:00"), scheduled.SendAt);
        Assert.Equal(SlotResolution.Exact, scheduled.Slot);
    }

    [Fact]
    public void Resolve_UndefinedChannelValue_ThrowsArgumentOutOfRangeException()
    {
        var undefinedChannel = (CommunicationChannel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.Resolve(ReferenceTime, null, "America/Chicago", undefinedChannel, null, null));
    }

    // The send is never before the floor: a same-day row rolls forward at most one day, which
    // is enough only when no row counts backwards from the floor's day.
    [Fact]
    public void Rows_EveryRow_CountsForwardFromTheFloorDay()
    {
        Assert.All(SendSlotTable.Default.Rows, row => Assert.True(row.DaysAfterFloorDay >= 0, $"{row.Persona}/{row.LifecycleStage}/{row.Channel}"));
    }

    // A scheduler given a table reads that table alone: its row sets the slot, and a key only
    // the compiled table has keeps the channel's hour.
    [Fact]
    public void Resolve_GivenATable_ReadsItsRowsInsteadOfTheCompiledOnes()
    {
        SendSlotTable table = SendSlotTable.Create([new SendSlotRow("prospect", "toured", CommunicationChannel.Email, 2, new TimeOnly(8, 45))]).Value;
        var scheduler = new SendScheduler(table);

        ScheduledSend fromGivenRow = scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email, "prospect", "toured");
        ScheduledSend fromCompiledKey = scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email, "resident", "welcome");

        Assert.Equal(DateTimeOffset.Parse("2025-12-11T08:45:00-06:00"), fromGivenRow.SendAt);
        Assert.Equal(SendSlotSource.SlotRow, fromGivenRow.Source);
        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), fromCompiledKey.SendAt);
        Assert.Equal(SendSlotSource.ChannelDefault, fromCompiledKey.Source);
    }
}
