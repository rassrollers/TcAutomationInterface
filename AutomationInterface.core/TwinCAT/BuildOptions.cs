using CommandLine;

namespace AutomationInterface.core;

/// <summary>
/// Command-line options for the Automation Interface build process.
/// Parsed using the <c>CommandLineParser</c> library.
/// </summary>
public class BuildOptions
{
    /// <summary>Gets or sets the root working directory that contains the <c>.git</c> folder.</summary>
    [Option('w', "WorkDir", Required = true, HelpText = "Path to the root directory containing the .git")]
    public required string WorkDir { get; set; }

    /// <summary>Gets or sets the solution file name to search for in the working directory.</summary>
    [Option('s', "SolutionName", HelpText = "Name of the solution in the workDir folder")]
    public required string SolutionName { get; set; }

    /// <summary>Gets or sets the build type (Library or Project).</summary>
    [Option('t', "Type", HelpText = "Specify whether this is a 'Library' or 'Project'")]
    public BuildType Type { get; set; }

    /// <summary>Gets or sets whether the TcXaeShell UI should be visible.</summary>
    [Option('u', "UiXae", Default = false, HelpText = "Open XAE UI")]
    public bool UiXae { get; set; }

    /// <summary>Gets or sets whether user control of the UI is enabled (prevents auto-close).</summary>
    [Option('c', "UserControl", Default = false, HelpText = "Enables User Control of the UI")]
    public bool UserControl { get; set; }

    /// <summary>Gets or sets the resolved full path to the solution file.</summary>
    public string SolutionPath { get; set; } = string.Empty;
}