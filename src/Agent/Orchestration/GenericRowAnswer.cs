using System.Text.Json.Serialization;
using Agent.Common;
using Agent.Decisions;
using Agent.Domain;

namespace Agent.Orchestration;

// What a review-queue row says the generic row answered for: the persona and lifecycle stage,
// the horizon branch, and the action the record was given. Branch is null when no row exists
// for the pair, because then the pair is what is missing and the branch is not. The converter
// sits on the member, not the enum, because how this file spells a decision value is its own.
public sealed record GenericRowAnswer(
    string? Persona,
    string? LifecycleStage,
    [property: JsonConverter(typeof(SnakeCaseLowerEnumConverter<HorizonBranch>))] HorizonBranch? Branch,
    NextAction Action);
