namespace DBUDoctor.Core;

/// <summary>
/// Invalid input or configuration. The CLI prints the message and exits with code 2.
/// </summary>
public sealed class DoctorException : Exception
{
    public DoctorException(string message) : base(message) { }

    public DoctorException(string message, Exception inner) : base(message, inner) { }
}

public static class ExitCodes
{
    public const int Ok = 0;
    public const int PolicyViolated = 1;
    public const int InvalidInput = 2;
}
