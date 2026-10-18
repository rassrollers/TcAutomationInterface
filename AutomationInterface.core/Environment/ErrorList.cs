using EnvDTE80;
using System.Text.RegularExpressions;

namespace AutomationInterface.core;

/// <summary>
/// Severity of an entry in the Visual Studio / TcXaeShell Error List.
/// Values are ordered so that a higher value means a more severe entry.
/// </summary>
public enum ErrorListSeverity
{
    /// <summary>Informational message (<see cref="vsBuildErrorLevel.vsBuildErrorLevelLow"/>).</summary>
    Message = 0,
    /// <summary>Warning (<see cref="vsBuildErrorLevel.vsBuildErrorLevelMedium"/>).</summary>
    Warning = 1,
    /// <summary>Error (<see cref="vsBuildErrorLevel.vsBuildErrorLevelHigh"/>).</summary>
    Error = 2
}

/// <summary>
/// Immutable snapshot of a single Error List item.
/// Unlike <see cref="ErrorItem"/> this is a plain .NET object, so it is safe to use from any thread
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

    /// <summary>Maps the DTE build error level to <see cref="ErrorListSeverity"/>.</summary>
    internal static ErrorListSeverity FromVs(vsBuildErrorLevel level) => level switch
    {
        vsBuildErrorLevel.vsBuildErrorLevelHigh => ErrorListSeverity.Error,
        vsBuildErrorLevel.vsBuildErrorLevelMedium => ErrorListSeverity.Warning,
        _ => ErrorListSeverity.Message
    };

    /// <summary>Single-line representation, e.g. <c>[Error] C0077: Unknown type 'FOO' | POUs\MAIN.TcPOU(12,5)</c>.</summary>
    public override string ToString()
    {
        string location = string.IsNullOrEmpty(FileName)
            ? string.Empty
            : Line > 0 ? $" | {FileName}({Line},{Column})" : $" | {FileName}";
        return $"[{Severity}] {Description}{location}";
    }
}

/// <summary>
/// Filter options when reading the Error List.
/// </summary>
public sealed class ErrorListQuery
{
    /// <summary>Only include items with this severity or higher. Default: <see cref="ErrorListSeverity.Message"/> (everything).</summary>
    public ErrorListSeverity MinSeverity { get; set; } = ErrorListSeverity.Message;

    /// <summary>Optional case-insensitive substring (or regex when <see cref="UseRegex"/> is set) matched against the description.</summary>
    public string? Contains { get; set; }

    /// <summary>Treat <see cref="Contains"/> as a regular expression.</summary>
    public bool UseRegex { get; set; }

    /// <summary>Optional case-insensitive substring matched against the file name.</summary>
    public string? FileFilter { get; set; }

    /// <summary>Optional case-insensitive substring matched against the project name.</summary>
    public string? ProjectFilter { get; set; }

    /// <summary>Maximum number of items returned. Default 200. Use 0 or less for unlimited.</summary>
    public int MaxItems { get; set; } = 200;

    /// <summary>Remove duplicate items (same severity, description, file, line and column). Default true.</summary>
    public bool Distinct { get; set; } = true;

    /// <summary>
    /// Applies the query to a snapshot of the Error List.
    /// Totals in the result are always computed on the full, unfiltered list.
    /// </summary>
    /// <param name="entries">The full Error List snapshot.</param>
    public ErrorListResult Apply(IReadOnlyList<ErrorListEntry> entries)
    {
        Regex? regex = null;
        if (UseRegex && !string.IsNullOrEmpty(Contains))
            regex = new Regex(Contains, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        IEnumerable<ErrorListEntry> filtered = entries.Where(e => e.Severity >= MinSeverity);

        if (!string.IsNullOrEmpty(Contains))
            filtered = regex is not null
                ? filtered.Where(e => regex.IsMatch(e.Description))
                : filtered.Where(e => e.Description.Contains(Contains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(FileFilter))
            filtered = filtered.Where(e => e.FileName.Contains(FileFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(ProjectFilter))
            filtered = filtered.Where(e => e.Project.Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase));

        if (Distinct)
            filtered = filtered.DistinctBy(e => (e.Severity, e.Description, e.FileName, e.Line, e.Column));

        // Most severe first, then keep the original Error List order
        var matched = filtered
            .OrderByDescending(e => e.Severity)
            .ThenBy(e => e.Index)
            .ToList();

        bool truncated = MaxItems > 0 && matched.Count > MaxItems;

        return new ErrorListResult
        {
            ErrorCount = entries.Count(e => e.Severity == ErrorListSeverity.Error),
            WarningCount = entries.Count(e => e.Severity == ErrorListSeverity.Warning),
            MessageCount = entries.Count(e => e.Severity == ErrorListSeverity.Message),
            MatchedCount = matched.Count,
            Truncated = truncated,
            Items = truncated ? matched.Take(MaxItems).ToList() : matched
        };
    }
}

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

/// <summary>
/// Result of a clean rebuild of the solution.
/// </summary>
public sealed class BuildResult
{
    /// <summary><see langword="true"/> when the build finished without errors in the Error List.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Wall clock duration of clean + build.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The Error List after the build.</summary>
    public ErrorListResult ErrorList { get; init; } = new();
}
