using Agent.Domain;

namespace Agent.Decisions;

// One send slot: the persona, lifecycle stage and channel it answers for, how many local days
// after the floor's day the send lands, the local time of day it lands at, and the horizon branch it
// is limited to, null when it answers every branch.
public sealed record SendSlotRow(
    string Persona,
    string LifecycleStage,
    CommunicationChannel Channel,
    int DaysAfterFloorDay,
    TimeOnly LocalTime,
    HorizonBranch? Branch = null);
