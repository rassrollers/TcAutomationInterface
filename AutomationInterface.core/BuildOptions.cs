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
    [Option('s', "SolutionName", Required = true, HelpText = "Name of the solution in the workDir folder")]
    public required string SolutionName { get; set; }

    /// <summary>Gets or sets the project name to select from the solution.</summary>
    [Option('p', "ProjectName", HelpText = "Name of the project to deploy")]
    public string? ProjectName { get; set; }

    /// <summary>Gets or sets the TwinCAT target platform architecture.</summary>
    [Option('a', "PlatformArchitecture", HelpText = "TwinCAT target platform (for example, 'TwinCAT RT (x64)')")]
    public string? PlatformArchitecture { get; set; }

    /// <summary>Gets or sets the AMS Net ID of the TwinCAT target.</summary>
    [Option('n', "TargetNetId", HelpText = "AMS Net ID of the TwinCAT target")]
    public string? TargetNetId { get; set; }

    /// <summary>Gets or sets the build type (Library or Project).</summary>
    [Option('t', "Type", HelpText = "Specify whether this is a 'Library' or 'Project'")]
    public BuildType Type { get; set; }

    /// <summary>Gets or sets whether the TcXaeShell UI should be visible.</summary>
    [Option('u', "UiXae", Default = false, HelpText = "Open XAE UI")]
    public bool UiXae { get; set; }

    /// <summary>Gets or sets whether user control of the UI is enabled (prevents auto-close).</summary>
    [Option('c', "UserControl", Default = false, HelpText = "Enables User Control of the UI")]
    public bool UserControl { get; set; }

    /// <summary>Gets or sets the path on the TwinCAT target to the unit test results XML file.</summary>
    [Option('r', "UnitTestResultPath", HelpText = "Path on the target to the unit test results XML file (for example, '/usr/local/etc/TwinCAT/3.1/Boot/UnitTestResults.xml')")]
    public string? UnitTestResultPath { get; set; }

    /// <summary>Gets or sets the resolved full path to the solution file.</summary>
    public string SolutionPath { get; set; } = string.Empty;
}