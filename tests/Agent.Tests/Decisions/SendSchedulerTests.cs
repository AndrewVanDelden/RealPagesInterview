using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

public class SendSchedulerTests
{
    private static readonly ISendScheduler Scheduler = new SendScheduler();

    [Fact]
    public void Resolve_SmsSampleCase_ResolvesToNineAmLocal()
    {
        DateTimeOffset lastInteraction = DateTimeOffset.Parse("2025-12-08T15:04:00Z");

        DateTimeOffset resolved = Scheduler.Resolve(lastInteraction, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(resolved.DateTime));
        Assert.Equal(TimeSpan.FromHours(-6), resolved.Offset);
    }

    [Fact]
    public void Resolve_EmailSampleCase_ResolvesToTenAmLocal()
    {
        // sample.jsonl's record 2 expects send_at on 2025-12-09 (3 days out, matching next_action's
        // follow_up_in_days: 3), but Resolve only knows the default hour rule (DESIGN.md assumption #2:
        // "the exact calendar date rule is under-determined by two samples and is configurable"; the
        // sequence diagram calls Scheduler.Resolve(last_interaction, ...) directly, not a follow-up-shifted
        // date). Composing NextActionPlanner's follow-up offset with this time-of-day resolution is an
        // orchestrator concern (Sprint 5), so only the time-of-day component is asserted here.
        DateTimeOffset lastInteraction = DateTimeOffset.Parse("2025-12-06T11:30:00Z");

        DateTimeOffset resolved = Scheduler.Resolve(lastInteraction, "America/Chicago", CommunicationChannel.Email);

        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(resolved.DateTime));
    }

    [Fact]
    public void Resolve_LateNightLastInteraction_PushesToNextDayDefaultHour()
    {
        var lateNightLastInteraction = new DateTimeOffset(2025, 12, 8, 23, 30, 0, TimeSpan.FromHours(-6));

        DateTimeOffset resolved = Scheduler.Resolve(lateNightLastInteraction, "America/Chicago", CommunicationChannel.Sms);

        Assert.Equal(new DateOnly(2025, 12, 9), DateOnly.FromDateTime(resolved.DateTime));
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(resolved.DateTime));
    }

    [Fact]
    public void Resolve_VoiceChannel_ResolvesToNineAmLocal()
    {
        DateTimeOffset lastInteraction = DateTimeOffset.Parse("2025-12-08T15:04:00Z");

        DateTimeOffset resolved = Scheduler.Resolve(lastInteraction, "America/Chicago", CommunicationChannel.Voice);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(resolved.DateTime));
    }

    [Fact]
    public void Resolve_UnknownTimeZoneId_ThrowsArgumentException()
    {
        DateTimeOffset lastInteraction = DateTimeOffset.Parse("2025-12-08T15:04:00Z");

        Assert.Throws<ArgumentException>(() => Scheduler.Resolve(lastInteraction, "Not/AZone", CommunicationChannel.Sms));
    }

    [Fact]
    public void Resolve_UnknownChannel_ThrowsArgumentOutOfRangeException()
    {
        DateTimeOffset lastInteraction = DateTimeOffset.Parse("2025-12-08T15:04:00Z");
        var unknownChannel = (CommunicationChannel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.Resolve(lastInteraction, "America/Chicago", unknownChannel));
    }
}
