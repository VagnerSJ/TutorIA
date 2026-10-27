namespace EnglishTutor.Console.Tests.Support;

// Dummy Piper files that exist, so the startup voice check passes without a real Piper.
public static class PiperTestFiles
{
    // Returns the configuration values that turn the voice on and point to the files created under the folder.
    public static Dictionary<string, string?> CreateVoiceOnValues(string folder)
    {
        var piper = Path.Combine(folder, "piper");
        Directory.CreateDirectory(piper);
        var executable = Path.Combine(piper, "piper.exe");
        var model = Path.Combine(piper, "voice.onnx");
        File.WriteAllText(executable, "x");
        File.WriteAllText(model, "x");
        File.WriteAllText(model + ".json", "{}");
        return new Dictionary<string, string?>
        {
            ["Voice:Enabled"] = "true",
            ["Tts:Piper:ExecutablePath"] = executable,
            ["Tts:Piper:ModelPath"] = model,
        };
    }
}
