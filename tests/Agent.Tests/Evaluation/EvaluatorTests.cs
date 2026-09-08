using Agent.Domain;
using Agent.Evaluation;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agent.Tests.Evaluation;

public class EvaluatorTests
{
    private static readonly NextAction BaselineAction = new("start_cadence", "welcome", null);
    private static readonly DateTimeOffset BaselineSendAt = DateTimeOffset.Parse("2025-12-09T09:00:00-06:00");
    private static readonly Evaluator Evaluator = new();

    private const string EnglishSmsBody = "Hi Taylor, welcome to Oak Ridge Apartments. Reply 1 for Thu, 2 for Fri. Reply STOP to opt out.";

    private static NextMessage Message(
        CommunicationChannel? channel,
        string? body,
        string? subject = null,
        string? ctaType = "schedule_tour",
        DateTimeOffset? sendAt = null,
        IReadOnlyList<string>? options = null,
        Uri? link = null) =>
        new(channel, sendAt ?? BaselineSendAt, subject, body, ctaType is null ? null : new Cta(ctaType, options ?? ["Thu", "Fri"], link));

    private static NextMessage EmailMessage(string body, string? subject = "Tour Oak Ridge", Uri? link = null) =>
        new(CommunicationChannel.Email, BaselineSendAt, subject, body, new Cta("schedule_tour", null, link ?? new Uri("https://oakridge.example/tour")));

    private static NextMessage Suppressed() => new(CommunicationChannel.None);

    private static ExpectedOutcome BaselineExpected(NextMessage? message = null, NextAction? action = null) =>
        new(message ?? Message(CommunicationChannel.Sms, "expected body"), action ?? BaselineAction);

    private static ProspectCase BaselineCase(
        ExpectedOutcome? expected = null,
        string? primaryCta = "book_tour",
        bool includeOptOutInstructions = true,
        int? safetyViolationsMax = 0,
        double? personalizationScoreMin = 0.5,
        int? p95LatencyMs = 2000,
        string? language = "en") =>
        SampleProspectCases.Minimal(primaryCta: primaryCta, includeOptOutInstructions: includeOptOutInstructions, language: language) with
        {
            Expected = expected ?? BaselineExpected(),
            Thresholds = new CaseThresholds(p95LatencyMs, personalizationScoreMin, 0.9, safetyViolationsMax),
        };

    private static ScoredRun Run(ProspectCase prospectCase, NextMessage? message, NextAction? action = null, int? safetyViolationCount = 0, double? latencyMs = 1) =>
        new(prospectCase, new AgentOutput(message, action ?? BaselineAction), safetyViolationCount, latencyMs);

    private static RecordScore ScoreOf(ScoredRun run) => Evaluator.Evaluate([run]).RecordScores[0];

    // Channel: exact, with the three spellings of "no message" as one value.

    [Fact]
    public void Evaluate_ChannelMatches_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Passed, score.Channel);
    }

    [Fact]
    public void Evaluate_ChannelMismatch_Failed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), EmailMessage(EnglishSmsBody)));

        Assert.Equal(CheckResult.Failed, score.Channel);
    }

    [Fact]
    public void Evaluate_ExpectedNullMessageAndActualNoneObject_ChannelPassed()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(NextMessage: null, BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, Suppressed()));

        Assert.Equal(CheckResult.Passed, score.Channel);
        Assert.True(score.Passed);
    }

    [Fact]
    public void Evaluate_ExpectedNoneObjectAndActualNullMessage_ChannelPassed()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Suppressed(), BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, null));

        Assert.Equal(CheckResult.Passed, score.Channel);
    }

    // DESIGN.md section 2 documents channel as "sms | email | voice | null".
    [Fact]
    public void Evaluate_ExpectedNullChannelAndActualSuppressed_ChannelPassed()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(new NextMessage(Channel: null), BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, Suppressed()));

        Assert.Equal(CheckResult.Passed, score.Channel);
    }

    // send_at: to the day and to the hour, compared in the label's own offset (D4, D6).

    [Fact]
    public void Evaluate_SendAtSameInstant_DayAndHourPassed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "x", sendAt: BaselineSendAt))), Message(CommunicationChannel.Sms, EnglishSmsBody, sendAt: BaselineSendAt)));

        Assert.Equal(CheckResult.Passed, score.SendAtDay);
        Assert.Equal(CheckResult.Passed, score.SendAtHour);
    }

    [Fact]
    public void Evaluate_SendAtSameInstantInAnotherOffset_DayAndHourPassed()
    {
        DateTimeOffset sameInstantUtc = BaselineSendAt.ToUniversalTime();

        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, sendAt: sameInstantUtc)));

        Assert.Equal(CheckResult.Passed, score.SendAtDay);
        Assert.Equal(CheckResult.Passed, score.SendAtHour);
    }

    [Fact]
    public void Evaluate_SendAtOneHourOff_DayPassedHourFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, sendAt: BaselineSendAt.AddHours(1))));

        Assert.Equal(CheckResult.Passed, score.SendAtDay);
        Assert.Equal(CheckResult.Failed, score.SendAtHour);
    }

    [Fact]
    public void Evaluate_SendAtOneDayOff_DayAndHourFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, sendAt: BaselineSendAt.AddDays(1))));

        Assert.Equal(CheckResult.Failed, score.SendAtDay);
        Assert.Equal(CheckResult.Failed, score.SendAtHour);
    }

    [Fact]
    public void Evaluate_ExpectedHasNoSendAt_DayAndHourNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Suppressed(), BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, Suppressed()));

        Assert.Equal(CheckResult.NotMeasured, score.SendAtDay);
        Assert.Equal(CheckResult.NotMeasured, score.SendAtHour);
    }

    [Fact]
    public void Evaluate_ExpectedHasSendAtButActualDoesNot_DayAndHourFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), new NextMessage(CommunicationChannel.Sms, SendAt: null, Body: EnglishSmsBody, Cta: new Cta("schedule_tour", ["1"]))));

        Assert.Equal(CheckResult.Failed, score.SendAtDay);
        Assert.Equal(CheckResult.Failed, score.SendAtHour);
    }

    // next_action.type: exact (D2, D15).

    [Fact]
    public void Evaluate_NextActionTypeMatches_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody), BaselineAction));

        Assert.Equal(CheckResult.Passed, score.NextActionType);
    }

    [Fact]
    public void Evaluate_NextActionTypeMismatch_Failed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody), new NextAction("follow_up_in_days", null, 3)));

        Assert.Equal(CheckResult.Failed, score.NextActionType);
    }

    // Opt-out: the shared instruction list over subject plus body (D13 b).

    [Fact]
    public void Evaluate_OptOutRequiredAndPresent_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(includeOptOutInstructions: true), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Passed, score.OptOut);
    }

    [Fact]
    public void Evaluate_OptOutRequiredButMissing_Failed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(includeOptOutInstructions: true), Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments.")));

        Assert.Equal(CheckResult.Failed, score.OptOut);
    }

    [Fact]
    public void Evaluate_OptOutPhraseOnlyInSubject_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(includeOptOutInstructions: true), EmailMessage("Hi Taylor, welcome to Oak Ridge Apartments.", subject: "Reply STOP to opt out")));

        Assert.Equal(CheckResult.Passed, score.OptOut);
    }

    [Fact]
    public void Evaluate_OptOutNotRequired_NotMeasured()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(includeOptOutInstructions: false), Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge Apartments.")));

        Assert.Equal(CheckResult.NotMeasured, score.OptOut);
    }

    [Fact]
    public void Evaluate_ActualSuppressed_MessageChecksNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Suppressed(), BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, Suppressed()));

        Assert.Equal(CheckResult.NotMeasured, score.OptOut);
        Assert.Equal(CheckResult.NotMeasured, score.CtaType);
        Assert.Equal(CheckResult.NotMeasured, score.CtaPayload);
        Assert.Equal(CheckResult.NotMeasured, score.BodyLanguage);
        Assert.Equal(CheckResult.NotMeasured, score.Personalization);
        Assert.Null(score.PersonalizationScore);
        Assert.True(score.Passed);
    }

    // Call-to-action type: exact against the label's cta.type (D13 d), never against the
    // product's vocabulary table.

    [Fact]
    public void Evaluate_CtaTypeMatchesTheLabel_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: "schedule_tour")));

        Assert.Equal(CheckResult.Passed, score.CtaType);
    }

    [Fact]
    public void Evaluate_MessageHasNoCta_CtaTypeFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: null)));

        Assert.Equal(CheckResult.Failed, score.CtaType);
    }

    [Fact]
    public void Evaluate_CtaTypeDiffersFromTheLabel_Failed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: "call_now")));

        Assert.Equal(CheckResult.Failed, score.CtaType);
    }

    [Fact]
    public void Evaluate_LabelUsesATypeNoConstraintMapsTo_ScoredAgainstTheLabel()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected", ctaType: "intent_capture")), primaryCta: "reply_intent");

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: "intent_capture")));

        Assert.Equal(CheckResult.Passed, score.CtaType);
    }

    [Fact]
    public void Evaluate_LabelHasNoCta_CtaTypeNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Sms, "expected", ctaType: null)));

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: "reply")));

        Assert.Equal(CheckResult.NotMeasured, score.CtaType);
    }

    [Fact]
    public void Evaluate_LabelSuppressedButActualSent_ChannelFailedAndCtaTypeNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Suppressed(), BaselineAction));

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Failed, score.Channel);
        Assert.Equal(CheckResult.NotMeasured, score.CtaType);
        Assert.Equal(CheckResult.NotMeasured, score.SendAtDay);
    }

    [Fact]
    public void Evaluate_LabelHasCtaButActualSuppressed_CtaTypeNotMeasuredAndChannelFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Suppressed()));

        Assert.Equal(CheckResult.NotMeasured, score.CtaType);
        Assert.Equal(CheckResult.Failed, score.Channel);
    }

    // Call-to-action payload by channel: sms carries options, email carries a link (A10).

    [Fact]
    public void Evaluate_SmsWithOptions_CtaPayloadPassed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, options: ["Thu", "Fri"])));

        Assert.Equal(CheckResult.Passed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_SmsWithoutCta_CtaPayloadFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody, ctaType: null)));

        Assert.Equal(CheckResult.Failed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_SmsWithEmptyOptions_CtaPayloadFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), new NextMessage(CommunicationChannel.Sms, BaselineSendAt, null, EnglishSmsBody, new Cta("schedule_tour", []))));

        Assert.Equal(CheckResult.Failed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_EmailWithLink_CtaPayloadPassed()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(EmailMessage("expected")));

        RecordScore score = ScoreOf(Run(prospectCase, EmailMessage(EnglishSmsBody)));

        Assert.Equal(CheckResult.Passed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_EmailWithoutLink_CtaPayloadFailed()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(EmailMessage("expected")));

        RecordScore score = ScoreOf(Run(prospectCase, new NextMessage(CommunicationChannel.Email, BaselineSendAt, "Subject", EnglishSmsBody, new Cta("schedule_tour"))));

        Assert.Equal(CheckResult.Failed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_EmailWithoutCta_CtaPayloadFailed()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(EmailMessage("expected")));

        RecordScore score = ScoreOf(Run(prospectCase, new NextMessage(CommunicationChannel.Email, BaselineSendAt, "Subject", EnglishSmsBody, Cta: null)));

        Assert.Equal(CheckResult.Failed, score.CtaPayload);
    }

    [Fact]
    public void Evaluate_VoiceMessage_CtaPayloadNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase(BaselineExpected(Message(CommunicationChannel.Voice, "expected")));

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Voice, EnglishSmsBody, ctaType: null)));

        Assert.Equal(CheckResult.NotMeasured, score.CtaPayload);
    }

    // Body language equals input.language (A13, D13 c).

    [Fact]
    public void Evaluate_EnglishBodyForEnglishRecord_BodyLanguagePassed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: "en"), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Passed, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_EnglishBodyForSpanishRecord_BodyLanguageFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: "es"), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Failed, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_SpanishBodyForSpanishRecord_BodyLanguagePassed()
    {
        const string spanishBody = "Hola Taylor, gracias por tu interés en Oak Ridge Apartments. Responde 1 para jueves, 2 para viernes. Responde STOP para cancelar.";

        RecordScore score = ScoreOf(Run(BaselineCase(language: "es"), Message(CommunicationChannel.Sms, spanishBody)));

        Assert.Equal(CheckResult.Passed, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_RegionalLanguageTag_BodyLanguageUsesThePrimarySubtag()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: "en-US"), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.Passed, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_NoLanguageStated_BodyLanguageNotMeasured()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: null), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.NotMeasured, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_LanguageTheDetectorDoesNotKnow_BodyLanguageNotMeasured()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: "fr"), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(CheckResult.NotMeasured, score.BodyLanguage);
    }

    [Fact]
    public void Evaluate_BodyWithNoStopWords_BodyLanguageFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(language: "en"), Message(CommunicationChannel.Sms, "Taylor. Oak Ridge. STOP.")));

        Assert.Equal(CheckResult.Failed, score.BodyLanguage);
    }

    // Safety: violations within the stated budget; absent budget is zero (A15).

    [Fact]
    public void Evaluate_SafetyViolationsWithinMax_Passed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(safetyViolationsMax: 1), Message(CommunicationChannel.Sms, EnglishSmsBody), safetyViolationCount: 1));

        Assert.Equal(CheckResult.Passed, score.Safety);
    }

    [Fact]
    public void Evaluate_SafetyViolationsExceedMax_Failed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(safetyViolationsMax: 0), Message(CommunicationChannel.Sms, EnglishSmsBody), safetyViolationCount: 1));

        Assert.Equal(CheckResult.Failed, score.Safety);
    }

    [Fact]
    public void Evaluate_NoSafetyCountRecorded_SafetyNotMeasured()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody), safetyViolationCount: null));

        Assert.Equal(CheckResult.NotMeasured, score.Safety);
    }

    [Fact]
    public void Evaluate_NoThresholdsStated_SafetyBudgetIsZeroAndTheRestNotMeasured()
    {
        ProspectCase prospectCase = BaselineCase() with { Thresholds = null };

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody), safetyViolationCount: 1));

        Assert.Equal(CheckResult.Failed, score.Safety);
        Assert.Equal(CheckResult.NotMeasured, score.Personalization);
        Assert.Equal(1.0, score.PersonalizationScore);
    }

    // Personalization: coverage of the first name and the property name over subject plus
    // body (D13 a).

    [Fact]
    public void Evaluate_NameAndPropertyPresent_ScoresOneAndPassed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(personalizationScoreMin: 1.0), Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(1.0, score.PersonalizationScore);
        Assert.Equal(CheckResult.Passed, score.Personalization);
    }

    [Fact]
    public void Evaluate_PropertyOnlyInSubject_ScoresOne()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(personalizationScoreMin: 1.0), EmailMessage("Hi Taylor, tours this week. Reply STOP to opt out.", subject: "Your Oak Ridge Apartments visit")));

        Assert.Equal(1.0, score.PersonalizationScore);
    }

    [Fact]
    public void Evaluate_PropertyNamedByMostOfItsWords_CountsAsCovered()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(personalizationScoreMin: 1.0), Message(CommunicationChannel.Sms, "Hi Taylor, welcome to Oak Ridge! Reply STOP to opt out.")));

        Assert.Equal(1.0, score.PersonalizationScore);
    }

    [Fact]
    public void Evaluate_NeitherFactPresent_ScoresZeroAndFailed()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(personalizationScoreMin: 0.5), Message(CommunicationChannel.Sms, "Hello. Reply STOP to opt out.")));

        Assert.Equal(0.0, score.PersonalizationScore);
        Assert.Equal(CheckResult.Failed, score.Personalization);
    }

    [Fact]
    public void Evaluate_OnlyNamePresent_ScoresHalf()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(personalizationScoreMin: 0.5), Message(CommunicationChannel.Sms, "Hi Taylor. Reply STOP to opt out.")));

        Assert.Equal(0.5, score.PersonalizationScore);
        Assert.Equal(CheckResult.Passed, score.Personalization);
    }

    [Fact]
    public void Evaluate_CityAndAmenitiesAbsentFromBody_DoNotLowerTheScore()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: ["pool"]) with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.Equal(1.0, score.PersonalizationScore);
    }

    [Fact]
    public void Evaluate_RecordCarriesNameButNoProperty_ScoresOverTheNameAlone()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null) with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, "Hi Taylor. Reply STOP to opt out.")));

        Assert.Equal(1.0, score.PersonalizationScore);
    }

    [Fact]
    public void Evaluate_FactWithNoWords_NeverCountsAsCovered()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: "...") with
        {
            Expected = BaselineExpected(),
            Thresholds = new CaseThresholds(2000, 1.0, 0.9, 0),
        };

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, "Welcome to Oak Ridge Apartments. Reply STOP to opt out.")));

        Assert.Equal(0.5, score.PersonalizationScore);
    }

    [Fact]
    public void Evaluate_RecordCarriesNoFacts_ScoresOne()
    {
        ProspectCase prospectCase = BaselineCase() with { Input = null };

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, "Reply STOP to opt out.")));

        Assert.Equal(1.0, score.PersonalizationScore);
        Assert.Equal(CheckResult.Passed, score.Personalization);
    }

    [Fact]
    public void Evaluate_ActualBodyNull_OptOutFailedAndScoreZero()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(), Message(CommunicationChannel.Sms, null)));

        Assert.Equal(CheckResult.Failed, score.OptOut);
        Assert.Equal(0.0, score.PersonalizationScore);
    }

    // Latency is carried per record and judged only as p95 over the batch (A15, step 32).

    [Fact]
    public void Evaluate_LatencyIsCarriedThroughUnjudged()
    {
        RecordScore score = ScoreOf(Run(BaselineCase(p95LatencyMs: 5), Message(CommunicationChannel.Sms, EnglishSmsBody), latencyMs: 50));

        Assert.Equal(50, score.LatencyMs);
        Assert.True(score.Passed);
    }

    [Fact]
    public void Evaluate_LatencyBudgetIsTheStrictestStatedAcrossRecords()
    {
        ProspectCase loose = BaselineCase(p95LatencyMs: 5000) with { TaskId = "loose" };
        ProspectCase strict = BaselineCase(p95LatencyMs: 20) with { TaskId = "strict" };
        ProspectCase unstated = BaselineCase(p95LatencyMs: null) with { TaskId = "unstated" };

        Scorecard scorecard = Evaluator.Evaluate([Run(loose, Message(CommunicationChannel.Sms, EnglishSmsBody), latencyMs: 10), Run(strict, Message(CommunicationChannel.Sms, EnglishSmsBody), latencyMs: 30), Run(unstated, Message(CommunicationChannel.Sms, EnglishSmsBody), latencyMs: 1)]);

        Assert.Equal(20, scorecard.LatencyBudgetMs);
        Assert.Equal(30, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.Failed, scorecard.LatencyP95);
    }

    [Fact]
    public void Evaluate_NoRecordStatesALatencyBudget_BudgetIsNull()
    {
        Scorecard scorecard = Evaluator.Evaluate([Run(BaselineCase(p95LatencyMs: null), Message(CommunicationChannel.Sms, EnglishSmsBody))]);

        Assert.Null(scorecard.LatencyBudgetMs);
        Assert.Equal(CheckResult.NotMeasured, scorecard.LatencyP95);
    }

    // Unscoreable rows and per-record isolation (playbook step 34).

    [Fact]
    public void Evaluate_CaseHasNoExpectedOutcome_ReturnsUnscoreableRecordScore()
    {
        RecordScore score = ScoreOf(Run(SampleProspectCases.Minimal(), Message(CommunicationChannel.Sms, "hi")));

        Assert.NotNull(score.ScoringError);
        Assert.False(score.Passed);
    }

    [Fact]
    public void Evaluate_OneUnscoreableAmongMultiple_StillScoresTheOthers()
    {
        Scorecard scorecard = Evaluator.Evaluate([
            Run(SampleProspectCases.Minimal(), Message(CommunicationChannel.Sms, "hi")),
            Run(BaselineCase(), Message(CommunicationChannel.Sms, EnglishSmsBody)),
        ]);

        Assert.Equal(2, scorecard.TotalCount);
        Assert.NotNull(scorecard.RecordScores[0].ScoringError);
        Assert.Null(scorecard.RecordScores[1].ScoringError);
        Assert.Equal(CheckResult.Passed, scorecard.RecordScores[1].Channel);
    }

    // The oracle's NextAction is forced null via `!` despite the non-nullable static type,
    // simulating the "malformed despite what the type promises" scenario real data keeps
    // producing.
    [Fact]
    public void Evaluate_ScoringThrows_RecordBecomesUnscoreableInsteadOfAbortingTheBatch()
    {
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Message(CommunicationChannel.Sms, "expected"), null!));

        RecordScore score = ScoreOf(Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody)));

        Assert.NotNull(score.ScoringError);
        Assert.Contains("NullReferenceException", score.ScoringError);
        Assert.False(score.Passed);
    }

    [Fact]
    public void Evaluate_ScoringThrows_LogsErrorWithTheException()
    {
        var capturingLogger = new CapturingLogger<Evaluator>();
        var evaluator = new Evaluator(capturingLogger);
        ProspectCase prospectCase = BaselineCase(new ExpectedOutcome(Message(CommunicationChannel.Sms, "expected"), null!));

        evaluator.Evaluate([Run(prospectCase, Message(CommunicationChannel.Sms, EnglishSmsBody))]);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is NullReferenceException);
    }

    [Fact]
    public void Evaluate_AllChecksPassOrNotMeasured_RecordPassed()
    {
        Scorecard scorecard = Evaluator.Evaluate([Run(BaselineCase(personalizationScoreMin: 0.5, p95LatencyMs: 5000), Message(CommunicationChannel.Sms, EnglishSmsBody), latencyMs: 10)]);

        Assert.True(scorecard.RecordScores[0].Passed);
        Assert.Equal(1, scorecard.PassedCount);
        Assert.True(scorecard.AllPassed);
    }

    [Fact]
    public void Evaluate_OneFailedCheck_RecordNotPassed()
    {
        Scorecard scorecard = Evaluator.Evaluate([Run(BaselineCase(), EmailMessage(EnglishSmsBody))]);

        Assert.Equal(0, scorecard.PassedCount);
        Assert.False(scorecard.AllPassed);
    }
}
