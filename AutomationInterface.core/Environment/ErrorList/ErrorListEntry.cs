namespace AutomationInterface.core;

/// <summary>
/// Immutable snapshot of a single Error List item.
/// Unlike the COM <c>ErrorItem</c> this is a plain .NET object, so it is safe to use from any thread
/// after the COM object has been read on the STA thread.
/// </summary>
public sealed record ErrorListEntry
{
    /// <summary>1-based position of the item in the Error List at the time it was read.</summary>
    public int Index { get; init; }

    /// <summary>The severity of the item.</summary>
    public ErrorListSeverity Severity { get; init; }

    /// <summary>The message text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The file the item refers to (may be empty, e.g. for ADS log messages).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The line number in <see cref="FileName"/> (0 when unknown).</summary>
    public int Line { get; init; }

    /// <summary>The column number in <see cref="FileName"/> (0 when unknown).</summary>
    public int Column { get; init; }

    /// <summary>The project the item belongs to (may be empty).</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>Single-line representation, e.g. <c>[Error] C0077: Unknown type 'FOO' | POUs\MAIN.TcPOU(12,5)</c>.</summary>
    public override string ToString()
    {
        string location = string.IsNullOrEmpty(FileName)
            ? string.Empty
            : Line > 0 ? $" | {FileName}({Line},{Column})" : $" | {FileName}";
        return $"[{Severity}] {Description}{location}";
    }
}
