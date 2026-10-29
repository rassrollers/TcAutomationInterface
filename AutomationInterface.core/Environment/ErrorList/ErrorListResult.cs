namespace AutomationInterface.core;

/// <summary>
/// Result of reading the Error List.
/// </summary>
public sealed class ErrorListResult
{
    /// <summary>Total number of errors in the Error List (unfiltered).</summary>
    public int ErrorCount { get; init; }

    /// <summary>Total number of warnings in the Error List (unfiltered).</summary>
    public int WarningCount { get; init; }

    /// <summary>Total number of messages in the Error List (unfiltered).</summary>
    public int MessageCount { get; init; }

    /// <summary>Number of items that matched the query (before <see cref="ErrorListQuery.MaxItems"/> was applied).</summary>
    public int MatchedCount { get; init; }

    /// <summary><see langword="true"/> when <see cref="Items"/> was cut off by <see cref="ErrorListQuery.MaxItems"/>.</summary>
    public bool Truncated { get; init; }

    /// <summary>The matching items, most severe first.</summary>
    public List<ErrorListEntry> Items { get; init; } = new();

    /// <summary>Short human readable summary, e.g. <c>2 error(s), 5 warning(s), 13 message(s)</c>.</summary>
    public string Summary => $"{ErrorCount} error(s), {WarningCount} warning(s), {MessageCount} message(s)";
}
