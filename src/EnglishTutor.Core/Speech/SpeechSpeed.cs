using System.Globalization;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

// RN-TTS02: speaking speed of the session; Tts:Speed is only the initial value (US-012).
public sealed class SpeechSpeed(IOptions<TtsOptions> options)
{
    // Only the dot is a decimal separator; no exponent, hex or thousands separator.
    private const NumberStyles _numberStyles = NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint;

    public float Current { get; private set; } = options.Value.Speed;

    // Changes Current only when the text is a finite number inside the accepted range.
    public SpeedChange Change(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!float.TryParse(text, _numberStyles, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
        {
            return new SpeedChange(SpeedChangeStatus.NotANumber, Current);
        }

        if (value < TtsOptions.MinSpeed || value > TtsOptions.MaxSpeed)
        {
            return new SpeedChange(SpeedChangeStatus.OutOfRange, Current);
        }

        Current = value;
        return new SpeedChange(SpeedChangeStatus.Applied, Current);
    }

    // At least one and at most three decimals, always with a dot.
    public static string Format(float speed) => speed.ToString("0.0##", CultureInfo.InvariantCulture);
}
