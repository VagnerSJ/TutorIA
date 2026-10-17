using System.Text.Json;
using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class TutorReplySchemaTests
{
    // US-004 — apoio: schema canônico do contrato — ADR-002
    [Fact]
    public void Json_Always_DescribesCanonicalTutorReplyContract()
    {
        using var document = JsonDocument.Parse(TutorReplySchema.Json);
        var root = document.RootElement;

        root.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("object");
        Strings(root.GetProperty("required"))
            .Should().BeEquivalentTo("reply", "corrections", "pronunciation", "vocabulary", "praise");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var corrections = root.GetProperty("properties").GetProperty("corrections");
        corrections.GetProperty("maxItems").GetInt32().Should().Be(2);
        Strings(corrections.GetProperty("items").GetProperty("required"))
            .Should().BeEquivalentTo("original", "corrected", "explanation");
        TutorReplySchema.Json.Should().NotContain("\n", because: "the schema goes as a single command-line argument");
    }

    private static List<string?> Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString())];
}
