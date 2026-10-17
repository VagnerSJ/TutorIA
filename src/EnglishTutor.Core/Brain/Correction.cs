namespace EnglishTutor.Core.Brain;

// One correction shown to the student: "original → corrected (explanation)" (RN-FEED02).
public sealed record Correction(string Original, string Corrected, string Explanation);
