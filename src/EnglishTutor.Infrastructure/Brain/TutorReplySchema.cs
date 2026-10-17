namespace EnglishTutor.Infrastructure.Brain;

// JSON Schema of the canonical tutor reply (ADR-002), on a single line because it goes as the
// value of --json-schema. Reused by the Ollama "format" (US-006).
public static class TutorReplySchema
{
    public const string Json = "{}";
}
