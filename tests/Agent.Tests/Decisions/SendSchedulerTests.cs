using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// A4 and A5: the channel's slot (sms 09:00, email 10:00, voice 09:00 local) on the first
// day at or after max(reference time, last_interaction) in the record's timezone; an
// unknown or absent timezone is UTC (A6).
public class SendSchedulerTests
{
    private static readonly ISendScheduler Scheduler = new SendScheduler();
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2025-12-09T00:00:00-06:00");

    [Fact]
    public void Resolve_SmsSampleCase_ResolvesToNineAmLocalOnTheReferenceDay()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), resolved);
        Assert.Equal(TimeSpan.FromHours(-6), resolved.Offset);
    }

    [Fact]
    public void Resolve_EmailSampleCase_ResolvesToTenAmLocalOnTheReferenceDay()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, DateTimeOffset.Parse("2025-12-06T11:30:00Z"), "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), resolved);
    }

    [Fact]
    public void Resolve_LastInteractionAfterTheReferenceSlot_IsTheFloor()
    {
        var lateNightLastInteraction = new DateTimeOffset(2025, 12, 9, 23, 30, 0, TimeSpan.FromHours(-6));

        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, lateNightLastInteraction, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), resolved);
    }

    [Fact]
    public void Resolve_ReferenceTimePastTheSlot_RollsToTheNextDay()
    {
        DateTimeOffset afternoonReference = DateTimeOffset.Parse("2025-12-09T15:00:00-06:00");

        DateTimeOffset resolved = Scheduler.Resolve(afternoonReference, null, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-10T09:00:00-06:00"), resolved);
    }

    [Fact]
    public void Resolve_AbsentLastInteraction_UsesTheReferenceTimeAlone()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), resolved);
    }

    [Fact]
    public void Resolve_VoiceChannel_ResolvesToNineAmLocal()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, null, "America/Chicago", CommunicationChannel.Voice);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(resolved.DateTime));
    }

    [Fact]
    public void Resolve_UnknownTimeZoneId_ResolvesInUtc()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, null, "Not/AZone", CommunicationChannel.Sms);

        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00Z"), resolved);
        Assert.Equal(TimeSpan.Zero, resolved.Offset);
    }

    [Fact]
    public void Resolve_AbsentTimeZoneId_ResolvesInUtc()
    {
        DateTimeOffset resolved = Scheduler.Resolve(ReferenceTime, null, null, CommunicationChannel.Sms);

        Assert.Equal(TimeSpan.Zero, resolved.Offset);
    }

    [Fact]
    public void Resolve_UndefinedChannelValue_ThrowsArgumentOutOfRangeException()
    {
        var undefinedChannel = (CommunicationChannel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.Resolve(ReferenceTime, null, "America/Chicago", undefinedChannel));
    }
}
