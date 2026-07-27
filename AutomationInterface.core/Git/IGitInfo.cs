namespace AutomationInterface.core;

/// <summary>
/// Defines the contract for accessing parsed Git version and commit information.
/// </summary>
public interface IGitInfo
{
    /// <summary>Gets the major version number from the latest Git tag.</summary>
    int? GetMajor();

    /// <summary>Gets the minor version number from the latest Git tag.</summary>
    int? GetMinor();

    /// <summary>Gets the build (patch) version number from the latest Git tag.</summary>
    int? GetBuild();

    /// <summary>Gets the revision version number from the latest Git tag.</summary>
    int? GetRevision();

    /// <summary>Gets the abbreviated commit hash of the current HEAD.</summary>
    string? GetCommitHash();

    /// <summary>Gets the ISO 8601 timestamp of the current HEAD commit.</summary>
    string? GetCommitTime();

    /// <summary>Gets the long version string from <c>git describe</c> (e.g. <c>1.2.3-5-gabcdef</c>).</summary>
    string? GetLongVersion();

    /// <summary>Gets the short version string (e.g. <c>1.2.3</c>) parsed from the Git tag.</summary>
    string? GetShortVersion();
}