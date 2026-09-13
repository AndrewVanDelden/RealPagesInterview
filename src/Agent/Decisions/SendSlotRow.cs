using Agent.Domain;

namespace Agent.Decisions;

// One send slot: the persona, lifecycle stage and channel it answers for, how many local days
// after the floor's day the send lands, and the local time of day it lands at.
public sealed record SendSlotRow(
    string Persona,
    string LifecycleStage,
    CommunicationChannel Channel,
    int DaysAfterFloorDay,
    TimeOnly LocalTime);
