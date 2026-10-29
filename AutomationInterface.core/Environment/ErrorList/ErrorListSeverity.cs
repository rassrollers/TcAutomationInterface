namespace AutomationInterface.core;

/// <summary>
/// Severity of an entry in the Visual Studio / TcXaeShell Error List.
/// Values are ordered so that a higher value means a more severe entry.
/// </summary>
public enum ErrorListSeverity
{
    /// <summary>Informational message (DTE build error level <c>Low</c>).</summary>
    Message = 0,

    /// <summary>Warning (DTE build error level <c>Medium</c>).</summary>
    Warning = 1,

    /// <summary>Error (DTE build error level <c>High</c>).</summary>
    Error = 2
}
