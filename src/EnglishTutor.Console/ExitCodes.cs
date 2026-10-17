namespace EnglishTutor.Console;

// P8: 0 success (/quit, end of input, Ctrl+C), 1 unexpected error, 2 invalid configuration.
public static class ExitCodes
{
    public const int Success = 0;

    public const int UnexpectedError = 1;

    public const int InvalidConfiguration = 2;
}
