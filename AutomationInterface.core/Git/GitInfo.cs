using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AutomationInterface.core;

/// <summary>
/// Extracts and exposes Git repository version and commit information using LibGit2Sharp.
/// Implements <see cref="IGitInfo"/> for version injection and <see cref="IDisposable"/> to release the repository handle.
/// </summary>
public class GitInfo : IGitInfo, IDisposable
{
    private readonly ILogger log;
    private Repository? repo;
    private int? major = 0;
    private int? minor = 0;
    private int? build = 0;
    private int? revision = 0;
    private string? hash = null;
    private string? commitTime = null;
    private string? shortVersion = null;
    private string? longVersion = null;

    private readonly string hashPattern = @"g([0-9a-f]+)$";
    private readonly string gitPattern = @"^v?" + // optional 'v' prefix
        @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?" + // version numbers
        @"(?:-([A-Za-z]+(?:-\d+)?))?" + // optional pre-release tag
        @"-(\d+)-g([0-9a-f]+)$"; // commit count and hash

    /// <summary>
    /// Initializes a new instance of the <see cref="GitInfo"/> class.
    /// </summary>
    /// <param name="logger">The logger used to report warnings and information.</param>
    public GitInfo(ILogger logger)
    {
        log = logger;
    }

    /// <summary>
    /// Releases the underlying Git repository handle.
    /// </summary>
    public void Dispose()
    {
        if (repo is not null) 
            repo.Dispose();
    }

    /// <summary>
    /// Opens the Git repository at the specified path, verifies there are no uncommitted changes,
    /// and parses the version description from the latest tag.
    /// </summary>
    /// <param name="path">The root directory of the Git repository (must contain a <c>.git</c> folder).</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when the path does not contain a <c>.git</c> directory.</exception>
    /// <exception cref="Exception">Thrown when there are uncommitted changes in the repository.</exception>
    public void GitRepository(string path)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")))
            throw new DirectoryNotFoundException("Directory does not contain a .git directory: " + path);

        repo = new Repository(path);
        if (repo.Diff.Compare<TreeChanges>().Count > 0)
            throw new Exception($"{repo.Diff.Compare<TreeChanges>().Count} uncommitted changes in repository");
        GetDescription();
    }

    /// <summary>
    /// Parses the <c>git describe</c> output to extract version numbers, commit hash, and commit time.
    /// </summary>
    private void GetDescription()
    {
        try
        {
            string description = repo!.Describe(commit: repo.Head.Tip, options: new DescribeOptions { AlwaysRenderLongFormat = true });
            description = description.StartsWith("v") ? description.Substring(1) : description;
            Match match = Regex.Match(description, gitPattern);
            if (!match.Success)
                throw new Exception($"Git description did not match expected patter. Description: {description}");

            longVersion = description;
            shortVersion = description.Split('-')[0];

            var v = System.Version.Parse(shortVersion);
            major = v.Major;
            minor = v.Minor;
            build = v.Build;
            revision = v.Revision;
            hash = Regex.Match(longVersion, hashPattern).Groups[1].Value;
            commitTime = repo!.Head.Tip.Committer.When.ToString("o");
        }
        catch (Exception ex)
        {
            log.LogWarning("Failed to get git description. Exception: {ex}", ex.Message);
        }
    }

    /// <summary>
    /// Logs the parsed Git version and commit details to the configured logger.
    /// </summary>
    public void PrintInformation()
    {
        log.LogInformation("- - - - - Git info - - - - -");
        log.LogInformation($"Major: {major}");
        log.LogInformation($"Minor: {minor}");
        log.LogInformation($"Build: {build}");
        log.LogInformation($"Revision: {revision}");
        log.LogInformation($"Commit hash: {hash}");
        log.LogInformation($"Commit time: {commitTime}");
        log.LogInformation($"Version string: {GetLongVersion()}");
    }

    /// <inheritdoc />
    public int? GetMajor() { return major; }
    /// <inheritdoc />
    public int? GetMinor() { return minor; }
    /// <inheritdoc />
    public int? GetBuild() { return build; }
    /// <inheritdoc />
    public int? GetRevision() { return revision; }
    /// <inheritdoc />
    public string? GetCommitHash() { return hash; }
    /// <inheritdoc />
    public string? GetCommitTime() { return commitTime; }
    /// <inheritdoc />
    public string? GetLongVersion() { return longVersion; }
    /// <inheritdoc />
    public string? GetShortVersion() { return shortVersion; }
}
