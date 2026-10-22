namespace EnglishTutor.Core.Brain;

// Notice that a turn moved from the primary brain to the fallback brain (RN-BRAIN08, ADR-010).
public sealed record BrainSwitchNotice(string FromProvider, string ToProvider, BrainErrorKind Reason);
