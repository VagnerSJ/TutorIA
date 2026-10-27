namespace EnglishTutor.Core.Speech;

public enum SpeedChangeStatus
{
    Applied,
    NotANumber,
    OutOfRange,
}

// Current = the speed in force after the call (unchanged unless Status is Applied).
public sealed record SpeedChange(SpeedChangeStatus Status, float Current);
