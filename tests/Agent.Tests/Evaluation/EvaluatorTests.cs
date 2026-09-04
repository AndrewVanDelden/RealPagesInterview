using Agent.Domain;
using Agent.Evaluation;
using Agent.Orchestration;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Evaluation;

public class EvaluatorTests
{
    private static readonly NextAction BaselineAction = new("start_cadence", "welcome", null);

    private static NextMessage Message(CommunicationChannel? channel, string body, string? ctaType = "schedule_tour") =>
        new(channel, null, null, body, ctaType is null ? null : new Cta(ctaType, null, null));

    private static ExpectedOutcome BaselineExpected(NextMessage? message = null, NextAction? action = null) =>
        new(message ?? Message(CommunicationChannel.Sms, "expected body"), action ?? BaselineAction);

    private static ProspectCase BaselineCase(
        ExpectedOutcome? expected = null,
        string primaryCta = "book_tour",
        bool includeOptOutInstructions = true,
        int safetyViolationsMax = 0,
        double personalizationScoreMin = 0.5,
        int p95LatencyMs = 2000) =>
        SampleProspectCases.Minimal(primaryCta: primaryCta, includeOptOutInstructions: includeOptOutInstructions) with
        {
            Expected = expected ?? BaselineExpected(),
            Thresholds = new CaseThresholds(p95LatencyMs, personalizationScoreMin, 0.9, safetyViolationsMax),
        };

    private static AgentRunResult SuccessfulResult(NextMessage message, NextAction? action = null, int violationCount = 0) =>
        new(new AgentOutput(message, action ?? BaselineAction), new AgentDiagnostics(true, violationCount == 0, true, violationCount));

    private static AgentRunResult SuppressedResult(NextAction? action = null) =>
        new(new AgentOutput(null, action ?? BaselineAction), new AgentDiagnostics(true, null, false, 0));

    [Fact]
    public async Task EvaluateAsync_ChannelMatches_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase();
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].ChannelMatches);
    }

    [Fact]
    public async Task EvaluateAsync_ChannelMismatch_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected")));
        var actual = SuccessfulResult(Message(CommunicationChannel.Email, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].ChannelMatches);
    }

    [Fact]
    public async Task EvaluateAsync_NextActionTypeMatches_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase();
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), BaselineAction);
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].NextActionTypeMatches);
    }

    [Fact]
    public async Task EvaluateAsync_NextActionTypeMismatch_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase();
        var actual = SuccessfulResult(
            Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."),
            new NextAction("follow_up_in_days", null, 3));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].NextActionTypeMatches);
    }

    [Fact]
    public async Task EvaluateAsync_OptOutRequiredAndPresent_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: true);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public async Task EvaluateAsync_OptOutRequiredButMissing_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: true);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public async Task EvaluateAsync_OptOutNotRequiredAndMissing_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: false);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public async Task EvaluateAsync_PrimaryCtaMatchesRequiredMapping_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(primaryCta: "book_tour");
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour. Reply STOP to opt out.", ctaType: "schedule_tour"));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].PrimaryCtaPresent);
    }

    [Fact]
    public async Task EvaluateAsync_PrimaryCtaWrongType_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(primaryCta: "book_tour");
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, call us. Reply STOP to opt out.", ctaType: "call_now"));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].PrimaryCtaPresent);
    }

    [Fact]
    public async Task EvaluateAsync_SafetyViolationsWithinMax_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(safetyViolationsMax: 1);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), violationCount: 1);
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].SafetyViolationsWithinBudget);
    }

    [Fact]
    public async Task EvaluateAsync_SafetyViolationsExceedMax_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(safetyViolationsMax: 0);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), violationCount: 1);
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].SafetyViolationsWithinBudget);
    }

    [Fact]
    public async Task EvaluateAsync_AllPersonalizationTokensPresent_ScoresOne()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 1.0);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments near Richardson, TX. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.Equal(1.0, scorecard.RecordScores[0].PersonalizationScore);
        Assert.True(scorecard.RecordScores[0].PersonalizationScoreMet);
    }

    [Fact]
    public async Task EvaluateAsync_NoStatedInterestAtAll_PersonalizationScoreOnlyChecksNameAndProperty()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null) with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.Equal(1.0, scorecard.RecordScores[0].PersonalizationScore);
    }

    [Fact]
    public async Task EvaluateAsync_NoPersonalizationTokensPresent_ScoresZeroAndFailsThreshold()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 0.5);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hello. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.Equal(0.0, scorecard.RecordScores[0].PersonalizationScore);
        Assert.False(scorecard.RecordScores[0].PersonalizationScoreMet);
    }

    [Fact]
    public async Task EvaluateAsync_FastAgent_LatencyWithinBudget()
    {
        ProspectCase prospectCase = BaselineCase(p95LatencyMs: 5000);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].LatencyWithinBudget);
    }

    [Fact]
    public async Task EvaluateAsync_SlowAgent_LatencyExceedsBudget()
    {
        ProspectCase prospectCase = BaselineCase(p95LatencyMs: 5);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual, delay: TimeSpan.FromMilliseconds(50)));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.False(scorecard.RecordScores[0].LatencyWithinBudget);
    }

    [Fact]
    public async Task EvaluateAsync_CaseHasNoExpectedOutcome_ThrowsArgumentException()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();
        var evaluator = new Evaluator(new FakeMessageAgent(SuccessfulResult(Message(CommunicationChannel.Sms, "hi"))));

        await Assert.ThrowsAsync<ArgumentException>(() => evaluator.EvaluateAsync([prospectCase]));
    }

    [Fact]
    public async Task EvaluateAsync_BothActualAndExpectedSuppressed_ScoresMessageShapeChecksAsPassed()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(NextMessage: null, BaselineAction));
        var actual = SuppressedResult();
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        RecordScore score = scorecard.RecordScores[0];
        Assert.True(score.ChannelMatches);
        Assert.True(score.OptOutPresent);
        Assert.True(score.PrimaryCtaPresent);
        Assert.Equal(1.0, score.PersonalizationScore);
        Assert.True(score.Passed);
    }

    [Fact]
    public async Task EvaluateAsync_AllChecksPass_RecordScorePassedIsTrue()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 0.5, p95LatencyMs: 5000);
        var actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.True(scorecard.RecordScores[0].Passed);
        Assert.Equal(1, scorecard.PassedCount);
        Assert.Equal(1, scorecard.TotalCount);
        Assert.True(scorecard.AllPassed);
    }

    [Fact]
    public async Task EvaluateAsync_OneFailingRecord_AllPassedIsFalse()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected")));
        var actual = SuccessfulResult(Message(CommunicationChannel.Email, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));
        var evaluator = new Evaluator(new FakeMessageAgent(actual));

        Scorecard scorecard = await evaluator.EvaluateAsync([prospectCase]);

        Assert.Equal(0, scorecard.PassedCount);
        Assert.Equal(1, scorecard.TotalCount);
        Assert.False(scorecard.AllPassed);
    }

    [Fact]
    public async Task EvaluateAsync_RealComponentsAgainstSampleJsonl_BothRecordsPassChannelActionAndPersonalization()
    {
        IReadOnlyList<ProspectCase> cases = RealAgentFactory.ReadSampleCases();
        var evaluator = new Evaluator(RealAgentFactory.BuildRealAgent());

        Scorecard scorecard = await evaluator.EvaluateAsync(cases);

        Assert.Equal(2, scorecard.TotalCount);
        foreach (RecordScore score in scorecard.RecordScores)
        {
            Assert.True(score.ChannelMatches, $"{score.TaskId}: channel mismatch");
            Assert.True(score.NextActionTypeMatches, $"{score.TaskId}: next_action.type mismatch");
            Assert.True(score.PersonalizationScoreMet, $"{score.TaskId}: personalization {score.PersonalizationScore} below minimum");
        }
    }
}
