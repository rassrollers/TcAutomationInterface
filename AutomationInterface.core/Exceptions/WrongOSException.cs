namespace AutomationInterface.core;

/// <summary>
/// Thrown when the application is executed on a non-Windows operating system.
/// TwinCAT Automation Interface requires a Windows environment.
/// </summary>
public class WrongOSException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WrongOSException"/> class with the detected OS platform name.
    /// </summary>
    /// <param name="OS">The name of the detected operating system platform.</param>
    public WrongOSException(string OS) : base(OS)
    { }
}
