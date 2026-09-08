using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// A4 and A5: the channel's slot (sms 09:00, email 10:00, voice 09:00 local) on the first
// day at or after max(reference time, last_interaction) in the record's timezone; an
// unknown or absent timezone is UTC (A6). D22: the scheduler also returns which input was
// the floor, the zone it computed in, and how the slot resolved (A20).
public class SendSchedulerTests
{
    private static readonly ISendScheduler Scheduler = new SendScheduler();
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2025-12-09T00:00:00-06:00");

    [Fact]
    public void Resolve_SmsSampleCase_ResolvesToNineAmLocalOnTheReferenceDay()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(TimeSpan.FromHours(-6), scheduled.SendAt.Offset);
    }

    [Fact]
    public void Resolve_EmailSampleCase_ResolvesToTenAmLocalOnTheReferenceDay()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-06T11:30:00Z"), "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), scheduled.SendAt);
    }

    [Fact]
    public void Resolve_LastInteractionAfterTheReferenceSlot_IsTheFloor()
    {
        var lateNightLastInteraction = new DateTimeOffset(2025, 12, 9, 23, 30, 0, TimeSpan.FromHours(-6));

        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, lateNightLastInteraction, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(ScheduleFloor.LastInteraction, scheduled.Floor);
    }

    [Fact]
    public void Resolve_ReferenceTimePastTheSlot_RollsToTheNextDay()
    {
        DateTimeOffset afternoonReference = DateTimeOffset.Parse("2025-12-09T15:00:00-06:00");

        ScheduledSend scheduled = Scheduler.Resolve(afternoonReference, null, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), scheduled.SendAt);
    }

    [Fact]
    public void Resolve_AbsentLastInteraction_UsesTheReferenceTimeAlone()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), scheduled.SendAt);
        Assert.Equal(ScheduleFloor.ReferenceTime, scheduled.Floor);
    }

    // A last interaction before the reference time does not move the floor: the send is
    // still counted from the reference time, so the floor named is the reference time.
    [Fact]
    public void Resolve_LastInteractionBeforeTheReferenceTime_NamesTheReferenceTimeAsTheFloor()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-11-01T08:00:00Z"), "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(ScheduleFloor.ReferenceTime, scheduled.Floor);
    }

    [Fact]
    public void Resolve_VoiceChannel_ResolvesToNineAmLocal()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Voice);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(scheduled.SendAt.DateTime));
    }

    [Fact]
    public void Resolve_UnknownTimeZoneId_ResolvesInUtc()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "Not/AZone", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00Z"), scheduled.SendAt);
        Assert.Equal(TimeSpan.Zero, scheduled.SendAt.Offset);
        Assert.Equal(TimeZoneInfo.Utc.Id, scheduled.TimeZoneId);
    }

    [Fact]
    public void Resolve_AbsentTimeZoneId_ResolvesInUtc()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, null, CommunicationChannel.Sms);

        Assert.Equal(TimeSpan.Zero, scheduled.SendAt.Offset);
        Assert.Equal(TimeZoneInfo.Utc.Id, scheduled.TimeZoneId);
    }

    // The zone the send was computed in is the resolved zone's own id, not the string the
    // record carried: a record whose timezone was unrecognized reads UTC here and the
    // unrecognized value is named once, in the ingest notes (A6).
    [Fact]
    public void Resolve_KnownTimeZoneId_NamesThatZone()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago").Id, scheduled.TimeZoneId);
    }

    // No transition in the current zone database covers 09:00 or 10:00, so every record any
    // input can produce takes the exact branch (A20). The other two are proved against the
    // custom zones in TimeZonesTests, where the transition does cover the slot.
    [Fact]
    public void Resolve_OrdinaryDay_ResolvesTheSlotExactly()
    {
        ScheduledSend scheduled = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(SlotResolution.Exact, scheduled.Slot);
    }

    // The synthetic set's daylight-saving record (item 8 of DESIGN.md section 4): the send
    // day is the day the zone springs forward, and 10:00 is after the transition, so the
    // slot itself is ordinary and the offset is the daylight one.
    [Fact]
    public void Resolve_SendDayIsADaylightSavingTransitionDay_UsesTheOffsetInForceAtTheSlot()
    {
        DateTimeOffset transitionDayReference = DateTimeOffset.Parse("2026-03-08T07:30:00Z");

        ScheduledSend scheduled = Scheduler.Resolve(transitionDayReference, null, "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(DateTimeOffset.Parse("2026-03-08T10:00:00-05:00"), scheduled.SendAt);
        Assert.Equal(SlotResolution.Exact, scheduled.Slot);
    }

    [Fact]
    public void Resolve_UndefinedChannelValue_ThrowsArgumentOutOfRangeException()
    {
        var undefinedChannel = (CommunicationChannel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.Resolve(ReferenceTime, null, "America/Chicago", undefinedChannel));
    }
}
