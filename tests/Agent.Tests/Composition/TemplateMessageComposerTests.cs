using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

public class TemplateMessageComposerTests
{
    private static readonly IMessageComposer Composer = new TemplateMessageComposer();

    // The message this composer returned, when it returned one. Every test that expects a
    // message goes through here, so composing something is asserted once rather than
    // restated at every call site.
    private static ComposedMessage ComposedOf(ComposeOutcome outcome) =>
        Assert.IsType<ComposeOutcome.Composed>(outcome).Message;

    [Fact]
    public async Task ComposeAsync_SmsWithCityInterest_BodyContainsRequiredElements()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        NextMessage message = result.Message;
        Assert.Contains("Taylor", message.Body);
        Assert.Contains("Oak Ridge Apartments", message.Body);
        Assert.Contains("Richardson, TX", message.Body);
        Assert.Contains("book a tour", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STOP", message.Body);
        Assert.Equal("schedule_tour", message.Cta!.Type);
        Assert.Null(message.Subject);
        Assert.Null(message.SendAt);
    }

    [Fact]
    public async Task ComposeAsync_EmailWithAmenityInterest_BodyAndSubjectContainRequiredElements()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: ["pool", "fitness"]);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);
        NextMessage message = result.Message;
        Assert.Contains("Taylor", message.Body);
        Assert.Contains("pool", message.Body);
        Assert.Contains("fitness", message.Body);
        Assert.Contains("book a tour", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STOP", message.Body);
        Assert.NotNull(message.Subject);
        Assert.Contains("Oak Ridge Apartments", message.Subject);
    }

    [Fact]
    public async Task ComposeAsync_AmenityAndCityInterestBothPresent_BodyMentionsBoth()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: ["pool"]);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        NextMessage message = result.Message;
        Assert.Contains("pool", message.Body);
        Assert.Contains("Richardson, TX", message.Body);
    }

    [Fact]
    public async Task ComposeAsync_NoInterestProvided_OmitsInterestPhrase()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.DoesNotContain("interested in", result.Message.Body);
        Assert.DoesNotContain("looking in", result.Message.Body);
    }

    // A12: an absent first name means no name in the greeting, not a failed record.
    [Fact]
    public async Task ComposeAsync_AbsentFirstName_ComposesWithoutAName()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.DoesNotContain("Taylor", result.Message.Body);
        Assert.StartsWith("Hi!", result.Message.Body);
        Assert.Contains("STOP", result.Message.Body);
    }

    // The greeting uses the name without the emoji and whitespace around it.
    [Fact]
    public async Task ComposeAsync_FirstNamePaddedWithAnEmoji_GreetsTheNameAlone()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: "  🙂 Sam 🙂 ");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.StartsWith("Hi Sam!", ComposedOf(outcome).Message.Body);
    }

    [Fact]
    public async Task ComposeAsync_AbsentPropertyName_ComposesWithoutAPropertyFact()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);
        Assert.DoesNotContain("Oak Ridge", result.Message.Body);
        Assert.Equal("Your next step", result.Message.Subject);
    }

    // A9: no primary_cta, at a persona and stage with no default call to action, means the
    // generic reply call to action.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCta_UsesTheGenericReplyCallToAction()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null, lifecycleStage: "open");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.Equal("reply", result.Message.Cta!.Type);
        Assert.Contains("Reply to learn more", result.Message.Body);
    }

    [Fact]
    public async Task ComposeAsync_BlankPrimaryCta_IsTreatedAsAbsent()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "  ", lifecycleStage: "open");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.Equal("reply", result.Message.Cta!.Type);
    }

    [Fact]
    public async Task ComposeAsync_NoContextAtAll_StillComposesAMessageWithOptOut()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(lifecycleStage: "open") with { Input = null, Assertions = null };

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.Contains("STOP", result.Message.Body);
        Assert.Equal("reply", result.Message.Cta!.Type);
    }

    // Playbook step 57: the result names the implementation that produced it, so a
    // fallback on an openai run shows up in the diagnostics instead of passing for a clean run.
    [Fact]
    public async Task ComposeAsync_AnyRecord_NamesItselfAsTheComposerOfOneAttempt()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.Equal(ComposerNames.Template, result.Notes.Composer);
        Assert.Equal(1, result.Notes.Attempts);
    }

    // A10: sms carries the numbered reply options in the body and in cta.options, the way sample 1's
    // label does. A tour invitation's options are the tour slots the agent planned, each written as a
    // date, and since each option carries commas the numbered options are joined with a semicolon.
    [Fact]
    public async Task ComposeAsync_TourSms_CarriesTheTourSlotsAsNumberedDatedOptions()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms, tourSlots: SampleTourSlots.Tuesday);

        ComposedMessage result = ComposedOf(outcome);

        NextMessage message = result.Message;
        Assert.Equal(SampleTourSlots.TuesdayText, message.Cta!.Options);
        Assert.Contains("Reply 1 for Dec 11, 2025, 10:00 AM; 2 for Dec 12, 2025, 10:00 AM.", message.Body);
        Assert.Null(message.Cta.Link);
    }

    // A caller that planned no slots has no date to offer, so a tour sms falls back to the language
    // set's generic pair rather than inventing days.
    [Fact]
    public async Task ComposeAsync_TourSmsWithNoSlots_OffersTheGenericPair()
    {
        ComposeOutcome outcome = await Composer.ComposeAsync(SampleProspectCases.Minimal(), CommunicationChannel.Sms);

        Assert.Equal(["a question", "a tour"], ComposedOf(outcome).Message.Cta!.Options);
    }

    // A10 and A21: email carries the link, built from the property slug and the catalog's
    // path for this call to action, and the body carries the same link.
    [Fact]
    public async Task ComposeAsync_EmailWithKnownCta_CarriesTheLinkBuiltFromThePropertySlug()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        NextMessage message = result.Message;
        Assert.Equal(new Uri("https://oakridge.example/tour"), message.Cta!.Link);
        Assert.Contains("https://oakridge.example/tour", message.Body);
        Assert.Null(message.Cta.Options);
    }

    // A21: no property name, no host, so no link is invented and the payload check fails
    // honestly on that record.
    [Fact]
    public async Task ComposeAsync_EmailWithoutPropertyName_CarriesNoLink()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        Assert.Null(result.Message.Cta!.Link);
    }

    // A21: the slug is the property name lowercased, with a trailing property-type word
    // dropped and every non-alphanumeric character removed.
    [Theory]
    [InlineData("Oak Ridge Apartments", "https://oakridge.example/tour")]
    [InlineData("Oak Ridge", "https://oakridge.example/tour")]
    [InlineData("St. James Lofts", "https://stjames.example/tour")]
    [InlineData("Lofts", "https://lofts.example/tour")]
    [InlineData("Saguaro Flats", "https://saguaroflats.example/tour")]
    public async Task ComposeAsync_EmailWithAnyPropertyName_BuildsTheSlugFromIt(string propertyName, string expectedLink)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: propertyName);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        Assert.Equal(new Uri(expectedLink), result.Message.Cta!.Link);
    }

    // A9: an unknown primary_cta passes through as the type, and the payload comes
    // from the generic row, since the shape is the channel's rule (A10) and only the content
    // is the catalog's.
    [Fact]
    public async Task ComposeAsync_UnknownPrimaryCtaOnSms_PassesTheTypeThroughWithTheGenericPayload()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "call_now");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);

        Cta cta = result.Message.Cta!;
        Assert.Equal("call_now", cta.Type);
        Assert.Equal(["a question", "a tour"], cta.Options);
    }

    // The generic reply list offers a tour, which only a prospect is shopping for, so a record whose
    // persona is not a prospect gets the list without it, in its own language.
    [Theory]
    [InlineData("resident", "en", new[] { "a question" })]
    [InlineData(null, "es", new[] { "una pregunta" })]
    public async Task ComposeAsync_GenericOptionsForAPersonaThatIsNotAProspect_LeaveOutTheTour(string? persona, string language, string[] expected)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "request_parking", persona: persona, lifecycleStage: "move_in", language: language);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(expected, ComposedOf(outcome).Message.Cta!.Options);
    }

    // A call to action the table does not recognize names no page any evidence shows, so its email
    // carries no link, the rule an absent property name already follows.
    [Fact]
    public async Task ComposeAsync_UnknownPrimaryCtaOnEmail_CarriesNoLink()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "request_callback", persona: "resident", lifecycleStage: "active");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);
        Assert.Equal("request_callback", result.Message.Cta!.Type);
        Assert.Null(result.Message.Cta.Link);
        Assert.DoesNotContain("https://", result.Message.Body);
    }

    // The calls to action synthetic_v2.jsonl's records name, each with the page its label links to and
    // the wire type it states: renew_lease goes out as review_renewal_offer.
    [Theory]
    [InlineData("start_application", "start_application", "https://oakridge.example/apply")]
    [InlineData("complete_application", "complete_application", "https://oakridge.example/portal")]
    [InlineData("sign_lease", "sign_lease", "https://oakridge.example/lease")]
    [InlineData("submit_maintenance_request", "submit_maintenance_request", "https://oakridge.example/maintenance")]
    [InlineData("renew_lease", "review_renewal_offer", "https://oakridge.example/renew")]
    public async Task ComposeAsync_BlindSetCallToActionOnEmail_CarriesItsTypeAndPage(string primaryCta, string expectedType, string expectedLink)
    {
        ComposeOutcome outcome = await Composer.ComposeAsync(SampleProspectCases.Minimal(primaryCta: primaryCta), CommunicationChannel.Email);

        Cta cta = ComposedOf(outcome).Message.Cta!;
        Assert.Equal(expectedType, cta.Type);
        Assert.Equal(new Uri(expectedLink), cta.Link);
    }

    // A move-in confirmation offers the two key pickup times its label names.
    [Fact]
    public async Task ComposeAsync_ConfirmMoveInOnSms_OffersTheMoveInTimes()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "confirm_move_in", persona: "resident", lifecycleStage: "move_in");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(["9 AM", "1 PM"], ComposedOf(outcome).Message.Cta!.Options);
    }

    // A9: no primary_cta, at a persona and stage with no default call to action, means the
    // generic reply call to action and its own payload.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCtaOnEmail_UsesTheGenericLinkPath()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null, lifecycleStage: "open");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        Assert.Equal(new Uri("https://oakridge.example/reply"), result.Message.Cta!.Link);
    }

    private static ProspectCase ResidentCase(string? primaryCta, string lifecycleStage, string? unit, string? language = "en")
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: primaryCta, persona: "resident", lifecycleStage: lifecycleStage, language: language);
        return prospectCase with { Input = prospectCase.ContextOrEmpty with { Unit = unit } };
    }

    // Each resident call to action reads as its own phrase, not its wire type: a language set
    // with no row for a type spells the type itself (A9), so a resident CTA without a row would
    // otherwise read as English words inside a Spanish message.
    [Theory]
    [InlineData("get_started", "welcome", "get started with your move-in checklist")]
    [InlineData("enroll_loyalty", "loyalty_engage", "enroll in the loyalty program")]
    [InlineData("review_renewal", "renewal_window", "review your renewal offer")]
    public async Task ComposeAsync_ResidentEmailEnglish_UsesTheCallToActionsOwnPhrase(string primaryCta, string lifecycleStage, string expectedPhrase)
    {
        ProspectCase prospectCase = ResidentCase(primaryCta, lifecycleStage, unit: "A‑204");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Contains(expectedPhrase, result.Message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("get_started", "welcome", "completar tu proceso de mudanza")]
    [InlineData("enroll_loyalty", "loyalty_engage", "inscribirte en el programa de lealtad")]
    [InlineData("review_renewal", "renewal_window", "revisar tu oferta de renovación")]
    public async Task ComposeAsync_ResidentEmailSpanish_UsesTheCallToActionsOwnPhrase(string primaryCta, string lifecycleStage, string expectedPhrase)
    {
        ProspectCase prospectCase = ResidentCase(primaryCta, lifecycleStage, unit: "A‑204", language: "es");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Contains(expectedPhrase, result.Message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("get started", result.Message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enroll loyalty", result.Message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("review renewal", result.Message.Body, StringComparison.OrdinalIgnoreCase);
    }

    // The hold-out's resident email labels: the welcome and loyalty pages by path, and the
    // renewal pages under the record's own unit, whose non-breaking hyphen is written as a plain
    // hyphen, which is how the labels spell the link.
    [Theory]
    [InlineData("get_started", "welcome", "https://oakridge.example/welcome")]
    [InlineData("enroll_loyalty", "loyalty_engage", "https://oakridge.example/loyalty")]
    [InlineData("review_renewal", "renewal_window", "https://oakridge.example/renewal/A-204")]
    [InlineData(null, "renewal_details_requested", "https://oakridge.example/renewal/A-204/details")]
    public async Task ComposeAsync_ResidentEmail_BuildsItsCallToActionLinkFromTheRecord(string? primaryCta, string lifecycleStage, string expectedLink)
    {
        ProspectCase prospectCase = ResidentCase(primaryCta, lifecycleStage, unit: "A‑204");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Equal(new Uri(expectedLink), result.Message.Cta!.Link);
        Assert.Contains(expectedLink, result.Message.Body);
    }

    // A unit's segment keeps letters, digits and hyphens and drops every other character.
    [Theory]
    [InlineData("B 118", "https://oakridge.example/renewal/B118")]
    [InlineData("#12/C", "https://oakridge.example/renewal/12C")]
    public async Task ComposeAsync_RenewalEmail_DropsEveryOtherCharacterFromTheUnitSegment(string unit, string expectedLink)
    {
        ProspectCase prospectCase = ResidentCase("review_renewal", "renewal_window", unit);

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Equal(new Uri(expectedLink), result.Message.Cta!.Link);
    }

    // A path that names the unit has no page to point at on a record that states no usable unit,
    // so no link is invented, the rule an absent property name already follows.
    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("#/")]
    public async Task ComposeAsync_RenewalEmailWithoutAUsableUnit_CarriesNoLink(string? unit)
    {
        ProspectCase prospectCase = ResidentCase("review_renewal", "renewal_window", unit);

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Null(result.Message.Cta!.Link);
    }

    // The hold-out's no-show record states reschedule_tour, and its label spells the type
    // reschedule with the options today and tomorrow.
    [Fact]
    public async Task ComposeAsync_RescheduleTourOnSms_CarriesTheRescheduleTypeAndItsOptions()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "reschedule_tour", lifecycleStage: "no_show");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms));

        Assert.Equal("reschedule", result.Message.Cta!.Type);
        Assert.Equal(["today", "tomorrow"], result.Message.Cta.Options);
        Assert.Contains("Reply to reschedule your tour. Reply 1 for today; 2 for tomorrow.", result.Message.Body);
    }

    // The hold-out's undecided-renewal record states reply_intent, and its label spells the
    // type intent_capture with the options yes, no and details.
    [Fact]
    public async Task ComposeAsync_ReplyIntentOnSms_CarriesTheIntentCaptureTypeAndItsOptions()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "reply_intent", persona: "resident", lifecycleStage: "renewal_undecided");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms));

        Assert.Equal("intent_capture", result.Message.Cta!.Type);
        Assert.Equal(["yes", "no", "details"], result.Message.Cta.Options);
        Assert.Contains("Reply to tell us if you plan to renew. Reply 1 for yes; 2 for no; 3 for details.", result.Message.Body);
    }

    [Theory]
    [InlineData("reschedule_tour", "Responde para reprogramar tu visita. Responde 1 para hoy; 2 para mañana.")]
    [InlineData("reply_intent", "Responde para decirnos si piensas renovar. Responde 1 para sí; 2 para no; 3 para detalles.")]
    public async Task ComposeAsync_SpanishSmsWithAHoldOutCallToAction_ComposesItsPhraseAndOptionsInSpanish(string primaryCta, string expectedSentences)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: primaryCta, language: "es");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms));

        Assert.Contains(expectedSentences, result.Message.Body);
    }

    // The hold-out's consent-fallback record is prospect/new with no primary_cta, and its
    // label says schedule_tour, so that stage's default is the tour and its tour link.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCtaAtProspectNew_UsesTheScheduleTourCallToAction()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null);

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Equal("schedule_tour", result.Message.Cta!.Type);
        Assert.Equal(new Uri("https://oakridge.example/tour"), result.Message.Cta.Link);
    }

    // The hold-out's renewal-details record is resident/renewal_details_requested with no
    // primary_cta, and its label says review_renewal_details.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCtaAtRenewalDetailsRequested_UsesTheReviewRenewalDetailsCallToAction()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null, persona: "resident", lifecycleStage: "renewal_details_requested");

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email));

        Assert.Equal("review_renewal_details", result.Message.Cta!.Type);
        Assert.Contains("Reply or click to review your renewal details at Oak Ridge Apartments.", result.Message.Body);
    }

    // The stage default is keyed the way the action catalog is: case and surrounding space in
    // the record do not matter, and a record that states no persona or no stage has no default.
    [Theory]
    [InlineData(" Prospect ", "NEW", "schedule_tour")]
    [InlineData(null, "new", "reply")]
    [InlineData("prospect", null, "reply")]
    public async Task ComposeAsync_AbsentPrimaryCta_LooksUpTheStageDefaultByNormalizedPersonaAndStage(string? persona, string? stage, string expectedType)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null, persona: persona, lifecycleStage: stage);

        ComposedMessage result = ComposedOf(await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms));

        Assert.Equal(expectedType, result.Message.Cta!.Type);
    }

    // A21: a property name with no letters or digits in it leaves no host, so the record
    // gets no link rather than "https://.example/tour".
    [Fact]
    public async Task ComposeAsync_EmailWithPunctuationOnlyPropertyName_CarriesNoLink()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: "!!!");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        Assert.Null(result.Message.Cta!.Link);
    }

    // A13: a record that states Spanish gets a Spanish message and the notes record that the locale
    // was applied. The tour slots are dates written the same way in every language, so only the words
    // around them are Spanish.
    [Fact]
    public async Task ComposeAsync_SpanishSms_ComposesInSpanishWithTheSameDatedOptions()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: "es");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms, tourSlots: SampleTourSlots.Tuesday);

        ComposedMessage result = ComposedOf(outcome);

        NextMessage message = result.Message;
        Assert.StartsWith("Hola Taylor", message.Body);
        Assert.Equal(SampleTourSlots.TuesdayText, message.Cta!.Options);
        Assert.Contains("Responde 1 para Dec 11, 2025, 10:00 AM; 2 para Dec 12, 2025, 10:00 AM.", message.Body);
        Assert.Contains("STOP", message.Body);
        Assert.True(result.Notes.LocaleApplied);
    }

    [Fact]
    public async Task ComposeAsync_SpanishEmail_UsesTheSpanishSubjectAndLinkLine()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: "es");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        ComposedMessage result = ComposedOf(outcome);

        NextMessage message = result.Message;
        Assert.Equal("Visita Oak Ridge Apartments", message.Subject);
        Assert.Contains("Empieza aquí: https://oakridge.example/tour", message.Body);
        Assert.Contains("STOP", message.Body);
    }

    // A13: the set is chosen on the primary subtag, so a regional or upper-case tag lands
    // on the same set.
    [Theory]
    [InlineData("es-MX")]
    [InlineData("ES")]
    [InlineData("es_419")]
    public async Task ComposeAsync_RegionalSpanishTag_ResolvesToTheSpanishSet(string languageTag)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: languageTag);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);

        Assert.StartsWith("Hola Taylor", result.Message.Body);
        Assert.True(result.Notes.LocaleApplied);
    }

    // A language with no full set is served wholly in English and says so: a message is written in one
    // language, and the program holds the opt-out and option sentences only for the languages it has
    // a set for.
    [Fact]
    public async Task ComposeAsync_LanguageWithNoTemplateSet_ComposesInEnglishAndReportsTheLocaleNotApplied()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: "tlh");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);

        Assert.StartsWith("Hi Taylor", result.Message.Body);
        Assert.False(result.Notes.LocaleApplied);
    }

    // French has a set, matched on the primary subtag, so a Canadian French record is written wholly in
    // French, its opt-out included.
    [Fact]
    public async Task ComposeAsync_FrenchCanadianRecord_ComposesWhollyInFrench()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: "fr-CA");

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);
        Assert.StartsWith("Bonjour Taylor", result.Message.Body);
        Assert.EndsWith("Répondez STOP pour vous désabonner.", result.Message.Body);
        Assert.DoesNotContain("Reply", result.Message.Body);
        Assert.True(result.Notes.LocaleApplied);
    }

    // A13: an absent language is the en default the record inherits, so nothing failed to
    // be applied.
    [Fact]
    public async Task ComposeAsync_AbsentLanguage_ComposesInEnglishWithTheLocaleApplied()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(language: null);

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposedMessage result = ComposedOf(outcome);

        Assert.StartsWith("Hi Taylor", result.Message.Body);
        Assert.True(result.Notes.LocaleApplied);
    }

    // Cost state one of three (no call, abandoned, completed): this composer issues no request,
    // so there is no measurement to report and the member is null. Null is the absence of a
    // measurement; a zero would be one, and would read as a model call that cost nothing.
    [Fact]
    public async Task ComposeAsync_AnyRecord_NotesStateNoModelCallAtAll()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Null(outcome.ModelCost);
    }
}
