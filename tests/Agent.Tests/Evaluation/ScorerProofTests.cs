using Agent.Domain;
using Agent.Evaluation;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Evaluation;

// The Phase 2 check (playbook step 33): the labels passed as actuals score 100 percent on
// every labeled set, and one corrupted field scores a failure on the check that owns it.
// A scorer that cannot fail proves nothing (the retrospective's finding 5).
public class ScorerProofTests
{
    private static readonly Evaluator Evaluator = new();

    [Theory]
    [InlineData("sample.jsonl", 2)]
    [InlineData("holdout_12.jsonl", 12)]
    [InlineData("synthetic_12.jsonl", 12)]
    public void Evaluate_LabelsAsActuals_EveryRecordPasses(string fileName, int recordCount)
    {
        IReadOnlyList<ProspectCase> cases = RealAgentFactory.ReadCases(fileName);

        Scorecard scorecard = Evaluator.Evaluate(GoldenRuns.FromLabels(cases));

        Assert.Equal(recordCount, scorecard.TotalCount);
        Assert.True(scorecard.AllPassed, ScorecardFormatter.Format(scorecard));
    }

    [Theory]
    [InlineData(EvaluationCheck.Channel)]
    [InlineData(EvaluationCheck.SendAtDay)]
    [InlineData(EvaluationCheck.SendAtHour)]
    [InlineData(EvaluationCheck.NextActionType)]
    [InlineData(EvaluationCheck.OptOut)]
    [InlineData(EvaluationCheck.CtaType)]
    [InlineData(EvaluationCheck.CtaPayload)]
    [InlineData(EvaluationCheck.BodyLanguage)]
    [InlineData(EvaluationCheck.Safety)]
    [InlineData(EvaluationCheck.Personalization)]
    public void Evaluate_OneCorruptedField_FailsTheCheckThatOwnsIt(EvaluationCheck check)
    {
        ScoredRun golden = GoldenRuns.FromLabel(RealAgentFactory.ReadCases("holdout_12.jsonl")[0]);

        Scorecard scorecard = Evaluator.Evaluate([Corrupt(golden, check)]);

        Assert.Equal(CheckResult.Failed, scorecard.RecordScores[0].ResultOf(check));
        Assert.False(scorecard.AllPassed);
    }

    [Fact]
    public void Evaluate_OneSlowRecord_FailsTheBatchP95()
    {
        ScoredRun golden = GoldenRuns.FromLabel(RealAgentFactory.ReadCases("holdout_12.jsonl")[0]);

        Scorecard scorecard = Evaluator.Evaluate([golden with { LatencyMs = 10_000 }]);

        Assert.Equal(CheckResult.Failed, scorecard.LatencyP95);
        Assert.False(scorecard.AllPassed);
    }

    // holdout_12 record 1: sms, English, opt-out required, book_tour, personalization 0.85.
    private static ScoredRun Corrupt(ScoredRun golden, EvaluationCheck check)
    {
        NextMessage message = golden.Output.NextMessage!;

        return check switch
        {
            EvaluationCheck.Channel => WithMessage(golden, message with { Channel = CommunicationChannel.Email }),
            EvaluationCheck.SendAtDay => WithMessage(golden, message with { SendAt = message.SendAt!.Value.AddDays(1) }),
            EvaluationCheck.SendAtHour => WithMessage(golden, message with { SendAt = message.SendAt!.Value.AddHours(1) }),
            EvaluationCheck.NextActionType => golden with { Output = golden.Output with { NextAction = new NextAction("corrupted") } },
            EvaluationCheck.OptOut => WithMessage(golden, message with { Body = "Hi Taylor, welcome to Oak Ridge! Tours are available this week." }),
            EvaluationCheck.CtaType => WithMessage(golden, message with { Cta = message.Cta! with { Type = "corrupted" } }),
            EvaluationCheck.CtaPayload => WithMessage(golden, message with { Cta = new Cta(message.Cta!.Type) }),
            EvaluationCheck.BodyLanguage => WithMessage(golden, message with { Body = "Hola Taylor, gracias por tu interés en Oak Ridge. Responde STOP para cancelar." }),
            EvaluationCheck.Safety => golden with { SafetyViolationCount = 1 },
            EvaluationCheck.Personalization => WithMessage(golden, message with { Body = "Hello there. Tours are available this week. Reply STOP to opt out." }),
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, "No corruption defined."),
        };
    }

    private static ScoredRun WithMessage(ScoredRun run, NextMessage message) =>
        run with { Output = run.Output with { NextMessage = message } };
}
