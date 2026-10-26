namespace EnglishTutor.Core.Speech;

// Read once at startup when Voice:Enabled=true (ADR-012).
public interface IVoiceOutputCheck
{
    VoiceOutputStatus Check();
}
