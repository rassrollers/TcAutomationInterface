using System.Text.RegularExpressions;

namespace AutomationInterface.core;

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
