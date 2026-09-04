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
}
