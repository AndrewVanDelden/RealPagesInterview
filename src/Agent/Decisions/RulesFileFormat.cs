using System.Text.Json;
using Agent.Domain;

namespace Agent.Decisions;

// The rules file: the action catalog and the send slots, snake_case. The call-to-action table stays compiled.
// Each row is held as raw JSON and read on its own, so one bad row is reported by its position
// instead of failing the whole file and hiding every other bad row.
internal sealed record RulesFileContent(RulesFileCatalogSection ActionCatalog, IReadOnlyList<JsonElement> SendSlots);

// The generic row reads as a GenericActionRow, both branches required; the rows as RulesFileCatalogRow.
internal sealed record RulesFileCatalogSection(JsonElement GenericRow, IReadOnlyList<JsonElement> Rows);

// A catalog row as the file states it. Persona and stage may be absent here so the catalog's
// own gate names them as blank; an absent or null action is a branch the row does not state.
internal sealed record RulesFileCatalogRow(
    string? Persona = null,
    string? LifecycleStage = null,
    NextAction? ShortHorizonAction = null,
    NextAction? LongHorizonAction = null);

// A send-slot row as the file states it. Every member may be absent here so the loader can name
// each one that is; the local time is 24-hour "HH:mm", the minute being the slot's grain.
internal sealed record RulesFileSlotRow(
    string? Persona = null,
    string? LifecycleStage = null,
    CommunicationChannel? Channel = null,
    int? DaysAfterFloorDay = null,
    string? LocalTime = null);
