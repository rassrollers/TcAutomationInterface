namespace AutomationInterface.core;

/// <summary>
/// Represents errors that occur during TwinCAT environment operations
/// such as build failures, configuration issues, or runtime problems.
/// </summary>
public class TwinCatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TwinCatException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public TwinCatException(string message) : base(message)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TwinCatException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TwinCatException(string message, Exception innerException)
        : base(message, innerException)
    { }
}

/// <summary>
/// Represents errors that occur within the TwinCAT Automation Interface layer,
/// such as missing references, failed COM operations, or invalid state.
/// </summary>
public class AutomationInterfaceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationInterfaceException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AutomationInterfaceException(string message) : base(message)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationInterfaceException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public AutomationInterfaceException(string message, Exception innerException)
        : base(message, innerException)
    { }
}

/// <summary>
/// Represents errors that occur when parsing or manipulating TwinCAT XML files
/// (<c>.tsproj</c> or <c>.plcproj</c>).
/// </summary>
public class TcXmlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TcXmlException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public TcXmlException(string message) : base(message)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcXmlException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TcXmlException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
