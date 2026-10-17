namespace EnglishTutor.Infrastructure.Brain;

// JSON Schema of the canonical tutor reply (ADR-002), on a single line because it goes as the
// value of --json-schema. Reused by the Ollama "format" (US-006).
public static class TutorReplySchema
{
    public const string Json =
        """{"type":"object","properties":{"reply":{"type":"string"},"corrections":{"type":"array","maxItems":2,"items":{"type":"object","properties":{"original":{"type":"string"},"corrected":{"type":"string"},"explanation":{"type":"string"}},"required":["original","corrected","explanation"],"additionalProperties":false}},"pronunciation":{"type":["string","null"]},"vocabulary":{"type":["string","null"]},"praise":{"type":["string","null"]}},"required":["reply","corrections","pronunciation","vocabulary","praise"],"additionalProperties":false}""";
}
