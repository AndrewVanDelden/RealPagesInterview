using Agent.Domain;
using Agent.Evaluation;
using Agent.Orchestration;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Evaluation;

public class EvaluatorTests
{
    private static readonly NextAction BaselineAction = new("start_cadence", "welcome", null);
    private static readonly Evaluator Evaluator = new();

    private static NextMessage Message(CommunicationChannel? channel, string body, string? subject = null, string? ctaType = "schedule_tour") =>
        new(channel, null, subject, body, ctaType is null ? null : new Cta(ctaType, null, null));

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

    private static ScoredRun Run(ProspectCase prospectCase, AgentRunResult result, double latencyMs = 1) =>
        new(prospectCase, result, latencyMs);

    [Fact]
    public void Evaluate_ChannelMatches_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase();
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].ChannelMatches);
    }

    [Fact]
    public void Evaluate_ChannelMismatch_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected")));
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Email, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].ChannelMatches);
    }

    [Fact]
    public void Evaluate_NextActionTypeMatches_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase();
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), BaselineAction);

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].NextActionTypeMatches);
    }

    [Fact]
    public void Evaluate_NextActionTypeMismatch_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase();
        AgentRunResult actual = SuccessfulResult(
            Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."),
            new NextAction("follow_up_in_days", null, 3));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].NextActionTypeMatches);
    }

    [Fact]
    public void Evaluate_OptOutRequiredAndPresent_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: true);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public void Evaluate_OptOutRequiredButMissing_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: true);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].OptOutPresent);
    }

    // Mirrors SafetyValidator.Validate's own search text: opt-out phrasing in the Subject
    // is just as valid as in the Body, since that's what the real validator (the actual
    // ship/suppress gate) checks.
    [Fact]
    public void Evaluate_OptOutPhraseOnlyInSubject_ScoresPresent()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: true);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Email, "Hi Taylor, book tour at Oak Ridge Apartments.", subject: "Reply STOP to opt out"));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public void Evaluate_OptOutNotRequiredAndMissing_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(includeOptOutInstructions: false);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].OptOutPresent);
    }

    [Fact]
    public void Evaluate_PrimaryCtaMatchesRequiredMapping_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(primaryCta: "book_tour");
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour. Reply STOP to opt out.", ctaType: "schedule_tour"));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].PrimaryCtaPresent);
    }

    [Fact]
    public void Evaluate_MessageHasNoCta_PrimaryCtaPresentScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(primaryCta: "book_tour");
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor. Reply STOP to opt out.", ctaType: null));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].PrimaryCtaPresent);
    }

    [Fact]
    public void Evaluate_PrimaryCtaWrongType_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(primaryCta: "book_tour");
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, call us. Reply STOP to opt out.", ctaType: "call_now"));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].PrimaryCtaPresent);
    }

    [Fact]
    public void Evaluate_SafetyViolationsWithinMax_ScoresTrue()
    {
        ProspectCase prospectCase = BaselineCase(safetyViolationsMax: 1);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), violationCount: 1);

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.True(scorecard.RecordScores[0].SafetyViolationsWithinBudget);
    }

    [Fact]
    public void Evaluate_SafetyViolationsExceedMax_ScoresFalse()
    {
        ProspectCase prospectCase = BaselineCase(safetyViolationsMax: 0);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."), violationCount: 1);

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.False(scorecard.RecordScores[0].SafetyViolationsWithinBudget);
    }

    [Fact]
    public void Evaluate_AllPersonalizationTokensPresent_ScoresOne()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 1.0);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments near Richardson, TX. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.Equal(1.0, scorecard.RecordScores[0].PersonalizationScore);
        Assert.True(scorecard.RecordScores[0].PersonalizationScoreMet);
    }

    [Fact]
    public void Evaluate_NoStatedInterestAtAll_PersonalizationScoreOnlyChecksNameAndProperty()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null) with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.Equal(1.0, scorecard.RecordScores[0].PersonalizationScore);
    }

    // Regression test: AmenityInterest and CityInterest must both be checked when both are
    // stated, not treated as alternatives - both TemplateMessageComposer and
    // OpenAiMessageComposer mention both when both are present.
    [Fact]
    public void Evaluate_BothAmenityAndCityInterestPresent_PersonalizationChecksBoth()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: ["pool"]) with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };
        AgentRunResult missingCity = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments near the pool. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, missingCity)]);

        Assert.False(scorecard.RecordScores[0].PersonalizationScoreMet, "city interest was omitted from the message but the score didn't drop");
    }

    [Fact]
    public void Evaluate_NoPersonalizationTokensPresent_ScoresZeroAndFailsThreshold()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 0.5);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hello. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.Equal(0.0, scorecard.RecordScores[0].PersonalizationScore);
        Assert.False(scorecard.RecordScores[0].PersonalizationScoreMet);
    }

    [Fact]
    public void Evaluate_FastRun_LatencyWithinBudget()
    {
        ProspectCase prospectCase = BaselineCase(p95LatencyMs: 5000);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual, latencyMs: 10)]);

        Assert.True(scorecard.RecordScores[0].LatencyWithinBudget);
    }

    [Fact]
    public void Evaluate_SlowRun_LatencyExceedsBudget()
    {
        ProspectCase prospectCase = BaselineCase(p95LatencyMs: 5);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual, latencyMs: 50)]);

        Assert.False(scorecard.RecordScores[0].LatencyWithinBudget);
    }

    [Fact]
    public void Evaluate_CaseHasNoExpectedOutcome_ReturnsUnscoreableRecordScore()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "hi"));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        RecordScore score = scorecard.RecordScores[0];
        Assert.NotNull(score.ScoringError);
        Assert.False(score.Passed);
    }

    // Demonstrates per-record isolation: one unscoreable case among several does not
    // prevent the others from being scored normally.
    [Fact]
    public void Evaluate_OneUnscoreableAmongMultiple_StillScoresTheOthers()
    {
        ProspectCase unlabeledCase = SampleProspectCases.Minimal();
        ProspectCase labeledCase = BaselineCase();
        AgentRunResult unlabeledActual = SuccessfulResult(Message(CommunicationChannel.Sms, "hi"));
        AgentRunResult labeledActual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(unlabeledCase, unlabeledActual), Run(labeledCase, labeledActual)]);

        Assert.Equal(2, scorecard.TotalCount);
        Assert.NotNull(scorecard.RecordScores[0].ScoringError);
        Assert.Null(scorecard.RecordScores[1].ScoringError);
        Assert.True(scorecard.RecordScores[1].ChannelMatches);
    }

    [Fact]
    public void Evaluate_BothActualAndExpectedSuppressed_ScoresMessageShapeChecksAsPassed()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(NextMessage: null, BaselineAction));
        AgentRunResult actual = SuppressedResult();

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        RecordScore score = scorecard.RecordScores[0];
        Assert.True(score.ChannelMatches);
        Assert.True(score.OptOutPresent);
        Assert.True(score.PrimaryCtaPresent);
        Assert.Equal(1.0, score.PersonalizationScore);
        Assert.True(score.Passed);
    }

    [Fact]
    public void Evaluate_AllChecksPass_RecordScorePassedIsTrue()
    {
        ProspectCase prospectCase = BaselineCase(personalizationScoreMin: 0.5, p95LatencyMs: 5000);
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual, latencyMs: 10)]);

        Assert.True(scorecard.RecordScores[0].Passed);
        Assert.Equal(1, scorecard.PassedCount);
        Assert.Equal(1, scorecard.TotalCount);
        Assert.True(scorecard.AllPassed);
    }

    [Fact]
    public void Evaluate_OneFailingRecord_AllPassedIsFalse()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected")));
        AgentRunResult actual = SuccessfulResult(Message(CommunicationChannel.Email, "Hi Taylor, book tour at Oak Ridge Apartments. Reply STOP to opt out."));

        Scorecard scorecard = Evaluator.Evaluate([Run(prospectCase, actual)]);

        Assert.Equal(0, scorecard.PassedCount);
        Assert.Equal(1, scorecard.TotalCount);
        Assert.False(scorecard.AllPassed);
    }

    [Fact]
    public async Task Evaluate_RealComponentsAgainstSampleJsonl_BothRecordsPassEveryCheck()
    {
        IReadOnlyList<ProspectCase> cases = RealAgentFactory.ReadSampleCases();
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        var runs = new List<ScoredRun>(cases.Count);

        foreach (ProspectCase prospectCase in cases)
        {
            AgentRunResult result = await agent.RunAsync(prospectCase);
            runs.Add(new ScoredRun(prospectCase, result, LatencyMs: 1));
        }

        Scorecard scorecard = Evaluator.Evaluate(runs);

        Assert.Equal(2, scorecard.TotalCount);
        Assert.True(scorecard.AllPassed, string.Join("; ", scorecard.RecordScores.Where(score => !score.Passed).Select(score => score.TaskId)));
        foreach (RecordScore score in scorecard.RecordScores)
        {
            Assert.True(score.ChannelMatches, $"{score.TaskId}: channel mismatch");
            Assert.True(score.NextActionTypeMatches, $"{score.TaskId}: next_action.type mismatch");
            Assert.True(score.OptOutPresent, $"{score.TaskId}: opt-out missing");
            Assert.True(score.PrimaryCtaPresent, $"{score.TaskId}: CTA mismatch");
            Assert.True(score.SafetyViolationsWithinBudget, $"{score.TaskId}: safety violations over budget");
            Assert.True(score.PersonalizationScoreMet, $"{score.TaskId}: personalization {score.PersonalizationScore} below minimum");
            Assert.True(score.LatencyWithinBudget, $"{score.TaskId}: latency over budget");
        }
    }
}
