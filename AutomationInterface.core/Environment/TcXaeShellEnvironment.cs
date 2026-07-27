using EnvDTE80;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace AutomationInterface.core;

public enum TcProjectType
{
    XaeProject,
    PlcProject,
    EmptyPlcProject,
    StandardPlcProject
}

/// <summary>
/// Represents the state of the TwinCAT XAE Shell environment lifecycle.
/// </summary>
internal enum TcEnvironmenState
{
    /// <summary>The environment has not been initialized.</summary>
    NotInitialized,
    /// <summary>A solution has been opened in Visual Studio.</summary>
    SolutionOpened,
    /// <summary>A TwinCAT project has been selected and configured.</summary>
    ProjectSelected,
    /// <summary>The environment has been disposed.</summary>
    Disposed
}

/// <summary>
/// High-level orchestrator for TwinCAT XAE Shell operations including environment setup,
/// project configuration, building, deployment, and TcUnit test execution.
/// Wraps <see cref="VisualStudioEnvironment"/> and <see cref="AutomationInterface"/> with
/// a simplified API.
/// Only use one instance of this class per XAE shell at a time to avoid conflicts in the 
/// underlying Visual Studio DTE instance.
/// </summary>
[SupportedOSPlatform("windows")]
public class TcXaeShellEnvironment : IDisposable, IAsyncDisposable
{
    private readonly ILogger log;
    private readonly VisualStudioEnvironment visualStudioEnvironment;
    private readonly AutomationInterface automationInterface;
    private readonly TcUnitRunner tcUnitRunner;
    private TcProjectXml? tcProjectXml;
    private TcEnvironmenState tcState = TcEnvironmenState.NotInitialized;

    #region Constructor and dispose
    /// <summary>
    /// Initializes a new instance of the <see cref="TcXaeShellEnvironment"/> class,
    /// creating the underlying Visual Studio environment, automation interface, and TcUnit runner.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public TcXaeShellEnvironment(ILogger logger)
    {
        log = logger;

        visualStudioEnvironment = new(logger);
        automationInterface = new(logger, visualStudioEnvironment);
        tcUnitRunner = new(logger, visualStudioEnvironment);
    }

    /// <summary>
    /// Synchronously disposes the environment by awaiting <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the environment, closing the automation interface
    /// and releasing the Visual Studio DTE instance.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (tcState == TcEnvironmenState.Disposed)
            return;

        tcState = TcEnvironmenState.Disposed;

        automationInterface?.Close();

        if (visualStudioEnvironment is not null)
            await visualStudioEnvironment.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
    #endregion

    #region Environment setup
    /// <summary>
    /// Creates a new TwinCAT solution using the specified DTE version and build options.
    /// Use the <see cref="FindInstalledTcXaeShell"/> method to discover installed DTE versions for the <paramref name="xaeDte"/> parameter.
    /// </summary>
    /// <param name="xaeDte">The TcXaeShell DTE ProgID (e.g. <c>TcXaeShell.DTE.15.0</c>).</param>
    /// <param name="solutionName">The name of the solution to create.</param>
    /// <param name="solutionPath">The path where the solution will be created.</param>
    /// <param name="uiXae">Indicates whether to use the UI XAE.</param>
    /// <param name="userControl">Indicates whether to use user control.</param>
    public async Task CreateSolutionEnvironment(string xaeDte, string solutionName, string solutionPath, bool uiXae = false, bool userControl = false)
    {
        log.LogInformation("- - - - - Creating solution - - - - -");
        await visualStudioEnvironment.CreateSolution(xaeDte, solutionName, solutionPath, uiXae, userControl);
    }

    /// <summary>
    /// Create a project from a template in the currently opened solution.
    /// Use the <see cref="core.TcProjectType"/> enumeration to specify the type of project.
    /// </summary>
    /// <param name="projectType">The type of project to create.</param>
    /// <param name="projectName">The name of the project to create.</param>
    /// <exception cref="ArgumentException">Thrown when an unsupported project type is specified.</exception>
    public async Task CreateProjectFromTemplate(string projectType, string solutionPath, string projectName)
    {
        Enum.TryParse(projectType, ignoreCase: true, out TcProjectType type);
        string templatePath = type switch
        {
            TcProjectType.XaeProject => AutomationInterface.XaeProjectTemplatePath,
            TcProjectType.PlcProject => AutomationInterface.PlcProjectTemplate,
            _ => throw new ArgumentException($"Unsupported project type: {projectType}")
        };

        log.LogInformation("Creating project: {projectName} from template: {templatePath}", projectName, type);
        await visualStudioEnvironment.AddProjectFromTemplate(templatePath, Path.Combine(solutionPath, projectName), projectName);
        await visualStudioEnvironment.SaveAll();
    }

    /// <summary>
    /// TODO: Not working yet, get an error when creating the project.
    /// Adds a new PLC project using the specified template type and project name.
    /// </summary>
    /// <param name="projectType">The type of PLC project template to use.</param>
    /// <param name="projectName">The name of the new PLC project.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when the specified project type is not supported.</exception>
    public async Task AddPlcProjectFromTemplate(string projectType, string projectName)
    {
        Enum.TryParse(projectType, ignoreCase: true, out TcProjectType type);
        string templatePath = type switch
        {
            TcProjectType.EmptyPlcProject => AutomationInterface.EmptyPlcProjectTemplate,
            TcProjectType.StandardPlcProject => AutomationInterface.StandardPlcProjectTemplate,
            _ => throw new ArgumentException($"Unsupported project type: {projectType}")
        };

        log.LogInformation("Adding PLC project: {projectName} from template: {templatePath}", projectName, type);
        await automationInterface.SetupBaseAiRefs();
        automationInterface.CreateProjectFromTemplate(templatePath, projectName);
        await visualStudioEnvironment.SaveAll();
        await automationInterface.SetupProjectReferences();
    }

    /// <summary>
    /// Open a TwinCAT solution file.
    /// Needs to know the path to the solution file.
    /// </summary>
    /// <param name="options"></param>
    public async Task OpenSolutionEnvironment(string solutionPath, bool uiXae = false, bool userControl = false)
    {
        log.LogInformation("- - - - - Open project - - - - -");
        await visualStudioEnvironment.OpenSolution(solutionPath, uiXae, userControl);
        await automationInterface.SetSilentMode();

        tcState = TcEnvironmenState.SolutionOpened;
    }

    /// <summary>
    /// Retrieves the full paths of all project files referenced in a Visual Studio solution file.
    /// </summary>
    /// <param name="solutionPath">The path to the solution (.sln) file.</param>
    /// <returns>A list of full paths to project files found in the solution.</returns>
    public List<string> FindProjectsInSolution(string solutionPath)
    {
        log.LogInformation("- - - - - Finding projects in solution - - - - -");
        var projects = new List<string>();

        if (!File.Exists(solutionPath))
        {
            log.LogError("Solution file not found: {solutionPath}", solutionPath);
            return projects;
        }

        try
        {
            var lines = File.ReadAllLines(solutionPath);
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("Project("))
                {
                    // Parse: Project("{GUID}") = "ProjectName", "RelativePath", "{ProjectGUID}"
                    var parts = line.Split(new[] { '=', ',' }, StringSplitOptions.TrimEntries);
                    if (parts.Length >= 3)
                    {
                        // Extract project path (second element after '=')
                        var projectPath = parts[2].Trim('"');
                        var fullProjectPath = Path.Combine(Path.GetDirectoryName(solutionPath)!, projectPath);

                        if (File.Exists(fullProjectPath))
                        {
                            log.LogDebug("Found project: {projectPath}", fullProjectPath);
                            projects.Add(fullProjectPath);
                        }
                    }
                }
            }

            log.LogInformation("Found {count} project(s) in solution", projects.Count);
        }
        catch (Exception ex)
        {
            log.LogError("Failed to parse solution file: {message}", ex.Message);
        }

        return projects;
    }

    /// <summary>
    /// Preparing the Visual Studio environment for opening the solution.
    /// Setup the DTE and the project xml.
    /// Use the <see cref="FindProjectsInSolution"/> method to get the project paths in the solution for the <paramref name="projectPath"/> parameter.
    /// </summary>
    /// <param name="projectPath">The path to the TwinCAT project.</param>
    public async Task SelectProjectInSolution(string projectPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        log.LogInformation("Select project: {projectName}", projectName);
        tcProjectXml = new(projectPath);
        log.LogInformation("TcVersion: {version}", tcProjectXml.GetTcVersion());
        log.LogInformation("TcVersionFixed: {fixVersion}", tcProjectXml.GetTcVersionFixed());
        if (tcProjectXml.IsTcProjectVariantDefined())
            log.LogInformation("TcProjectVariant: {variant}", tcProjectXml.GetTcProjectVariant());

        await automationInterface.SetTcRuntimeVersion(tcProjectXml!.GetTcVersion());
        await visualStudioEnvironment.SelectProjectByName(projectName);
        await automationInterface.SetupProjectReferences();
        tcState = TcEnvironmenState.ProjectSelected;
    }

    /// <summary>
    /// Retrieves the display names of all running development environments.
    /// </summary>
    /// <returns>A list of display names for each running environment.</returns>
    public List<string> FindRunningEnvironments()
    {
        log.LogInformation("- - - - - Finding running environments - - - - -");
        var xaeList = new List<string>();
        var runningEnvironments = DteHelper.FindRunningDteInstances();
        foreach (var env in runningEnvironments)
        {
            log.LogDebug("Found running environment: {progId}", env.DisplayName);
            xaeList.Add(env.DisplayName);
        }
        return xaeList;
    }

    /// <summary>
    /// Attaches to a running development environment by its display name.
    /// Will select the first project in the solution of the attached environment.
    /// Use the <see cref="FindRunningEnvironments"/> method to get the display names of running environments for the <paramref name="dteInstanceName"/> parameter.
    /// </summary>
    /// <param name="dteInstanceName">The display name of the DTE instance to attach to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AttachToEnvironment(string dteInstanceName)
    {
        log.LogInformation("- - - - - Attaching to environment - - - - -");
        var runningEnvironments = DteHelper.FindRunningDteInstances();
        foreach (var env in runningEnvironments)
        {
            if (env.DisplayName == dteInstanceName)
            {
                await visualStudioEnvironment.AttachToRunningDte(env);
            }
        }
    }

    /// <summary>
    /// Discovers all installed TcXaeShell DTE versions on the system.
    /// </summary>
    /// <returns>A list of TcXaeShell DTE ProgID strings.</returns>
    public List<string> FindInstalledTcXaeShell()
    {
        return VisualStudioEnvironment.FindInstalledTcXaeShell();
    }
    #endregion

    #region Project configuration
    /// <summary>
    /// Inject the licenses necessary for running the build server.
    /// Adds the licenses defined in the appsettings.json.
    /// PrepareEnvironment must be called before!
    /// Must be called before opening the environment.
    /// </summary>
    public async Task InjectBuildLicenses(string projectPath, IConfiguration config)
    {
        if (tcState >= TcEnvironmenState.SolutionOpened)
            throw new TwinCatException("Cannot inject build licenses because the solution is already opened");

        await SelectProjectInSolution(projectPath);
        if (tcProjectXml is null)
            throw new TwinCatException("Unable to inject build licenses because tcProjectXml was not set");

        var licensesSection = config.GetSection("Licenses");
        foreach (var license in licensesSection.GetChildren())
        {
            if (!string.IsNullOrEmpty(license.Value))
            {
                log.LogInformation("Adding license: {key} {value}", license.Key, license.Value);
                tcProjectXml.AddLicenses(license.Value);
            }
            else
                log.LogError("License key is missing: {key}", license.Key);
        }
    }

    /// <summary>
    /// Configure the environment for the NetID, Platform, Boot project.
    /// The parameters is set in the appsettings.json file for the target and the TcUnit
    /// </summary>
    /// <param name="config">Parameters from the appsettings.json</param>
    public void ConfigureTarget(IConfiguration config)
    {
        string netId = config.GetValue<string>("Target:NetId") ?? "192.168.17.10.1.1";
        string platform = config.GetValue<string>("Target:Platform") ?? "TwinCAT RT (x64)";
        automationInterface.SetPlatform(platform);
        automationInterface.SetTarget(netId);
        automationInterface.GenerateBootProject();
    }

    /// <summary>
    /// Injects Git version information into the PLC project's VERSION_INFO POU.
    /// </summary>
    /// <param name="gitInfo">The Git information to inject.</param>
    public void InjectGitVersion(IGitInfo gitInfo)
    {
        log.LogInformation("- - - - - Git injection - - - - -");
        log.LogInformation("Initiating injection of GIT information");
        automationInterface.UpdateVersionFile(gitInfo);
        log.LogInformation("Finished Injection of GIT information");
    }

    /// <summary>
    /// Reloads all Motion/NC axis elements in the Solution Explorer.
    /// Requires the XAE UI to be visible (<see cref="BuildOptions.UiXae"/> = <see langword="true"/>).
    /// </summary>
    public async Task ReloadMotionElements()
    {
        await automationInterface.ReloadMotionElements();
    }

    /// <summary>
    /// Gets the list of available project variant names in the current TwinCAT project.
    /// </summary>
    /// <returns>A list of variant name strings.</returns>
    public List<string> GetAvailableProjectVariant()
    {
        return automationInterface.GetAvailableProjectVariants();
    }

    /// <summary>
    /// Sets the active project variant by name.
    /// </summary>
    /// <param name="variant">The variant name to activate.</param>
    public void SetProjectVariant(string variant)
    {
        automationInterface.SetProjectVariant(variant);
    }

    /// <summary>
    /// Installs all <c>.library</c> files from the specified directory into the system library repository.
    /// </summary>
    /// <param name="libDirPath">The directory path containing <c>.library</c> files.</param>
    public void InstallLibrariesFromDirectory(string libDirPath)
    {
        log.LogInformation("- - - - - Installing libraries - - - - -");
        automationInterface.InstallLibrariesFromDirectory(libDirPath);
    }
    #endregion

    #region Build and deploy
    /// <summary>
    /// Performs a clean rebuild of the solution (clean + build) and throws if build errors are detected.
    /// </summary>
    /// <exception cref="TwinCatException">Thrown when the build produces errors.</exception>
    public async Task CleanAndRebuildSolution()
    {
        log.LogInformation("- - - - - Rebuild solution - - - - -");
        log.LogInformation("Performing a clean rebuild");
        log.LogDebug("Cleaning solution...");
        await visualStudioEnvironment.CleanSolution();
        log.LogDebug("Building solution...");
        await visualStudioEnvironment.BuildSolution();
        log.LogDebug("Printing potential errors...");

        var (_, _, tcBuildError) = await LogErrorListFromVS(vsBuildErrorLevel.vsBuildErrorLevelMedium);
        if (tcBuildError > 0)
            throw new TwinCatException("Build resulted in errors!");
        log.LogInformation("Clean rebuild finished");
    }

    /// <summary>
    /// Log the output from the Error List in TcXaeShell at a minimum error level
    /// </summary>
    /// <param name="errorLevel">Minimum error level to be logged</param>
    /// <returns>Number of Info/Warn/Error</returns>
    private async Task<(int infoCount, int warningCount, int errorCount)> LogErrorListFromVS(vsBuildErrorLevel errorLevel)
    {
        var errorsBuild = await visualStudioEnvironment.GetErrorItems();
        int tcBuildInfo = 0;
        int tcBuildWarnings = 0;
        int tcBuildError = 0;
        int i = 0;

        try
        {
            for (i = 0; i < errorsBuild.Count; i++)
            {
                ErrorItem item = errorsBuild[i];
                if (item.ErrorLevel < errorLevel)
                    continue;

                switch (item.ErrorLevel)
                {
                    case vsBuildErrorLevel.vsBuildErrorLevelLow:
                        log.LogInformation("{description} | {filename}", item.Description, item.FileName);
                        tcBuildInfo++;
                        break;
                    case vsBuildErrorLevel.vsBuildErrorLevelMedium:
                        log.LogWarning("{description} | {filename}", item.Description, item.FileName);
                        tcBuildWarnings++;
                        break;
                    case vsBuildErrorLevel.vsBuildErrorLevelHigh:
                        log.LogError("{description} | {filename}", item.Description, item.FileName);
                        tcBuildError++;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError("Failed to print error list. {i}:{count}", i, errorsBuild.Count);
            log.LogDebug("Exception details: {message}", ex.Message);
        }

        return (tcBuildInfo, tcBuildWarnings, tcBuildError);
    }

    /// <summary>
    /// Downloads the current configuration to the PLC by activating the TwinCAT configuration.
    /// </summary>
    public void DownloadToPlc()
    {
        log.LogInformation("- - - - - Downloading to PLC - - - - -");
        automationInterface.ActivateConfiguration();
    }

    /// <summary>
    /// Starts or restarts the TwinCAT runtime on the target.
    /// </summary>
    public void StartTwinCat()
    {
        log.LogInformation("- - - - - Starting TwinCAT - - - - -");
        automationInterface.StartTwinCAT();
    }

    /// <summary>
    /// Checks whether the target TwinCAT system runtime is currently running.
    /// </summary>
    /// <returns><see langword="true"/> if the target TwinCAT system is started; otherwise <see langword="false"/>.</returns>
    public bool IsTargetTcSysRunning()
    {
        return automationInterface.IsTargetTcSysRunning();
    }

    /// <summary>
    /// Saves the PLC project as a versioned library file to the specified output directory.
    /// </summary>
    /// <param name="outputDir">The directory to save the library to.</param>
    /// <param name="shortGitVersion">The Git information used for library versioning.</param>
    public async Task SaveLibrary(string outputDir, string shortGitVersion)
    {
        log.LogInformation("- - - - - Saving library - - - - -");
        log.LogInformation("Starting to save the library");
        await automationInterface.SaveLibraryFile(outputDir, shortGitVersion);
        log.LogInformation("Finished saving the library");
    }
    #endregion

    #region Programming items
    /// <summary>
    /// Creates a new programming item (POU, GVL, etc.) in the PLC project with the specified parameters.
    /// </summary>
    /// <param name="itemName">The name of the programming item.</param>
    /// <param name="itemType">The type of the programming item (e.g., POU, DUT, ITF, GVL, Folders). Use the <see cref="ProgramItemsTypes"/> enum for valid values.</param>
    /// <param name="itemPath">Optional: The path where the programming item should be created.</param>
    /// <param name="returnType">Optional: The return type of function type.</param>
    public async Task CreateProgramItem(string itemName, string itemType, string itemPath = "", string returnType = "")
    {
        log.LogInformation("Creating program item: {itemName} of type: {itemType} at path: {itemPath}", itemName, itemType, itemPath);
        automationInterface.CreateProgramItem(itemName, itemType, itemPath, returnType);
        await visualStudioEnvironment.SaveAll();
    }
    #endregion

    #region TcUnit
    /// <summary>
    /// Configures the TcUnit library for test result publishing using settings from the application configuration.
    /// </summary>
    /// <param name="config">The application configuration containing the <c>UnitTest:TcUnitResultPath</c> setting.</param>
    public async Task SetupTcUnitTest(IConfiguration config)
    {
        string resPath = config.GetValue<string>("UnitTest:TcUnitResultPath") ?? "/home/Administrator/TcUnitResults.xml";
        await automationInterface.SetupTcUnitLibrary(resPath);
    }

    /// <summary>
    /// Polls the Visual Studio Error List until TcUnit reports that all tests have finished,
    /// then prints the test results. Times out after 60 seconds.
    /// </summary>
    /// <exception cref="TwinCatException">Thrown when the timeout expires before tests complete.</exception>
    public async Task WaitForUnitTestToDone()
    {
        log.LogInformation("- - - - - Waiting on unit tests - - - - -");
        int timeoutMs = 60_000;
        int pollIntervalMs = 2_000;
        var startTime = DateTime.UtcNow;

        while (true)
        {
            if (await tcUnitRunner.IsTcUnitDone())
                break;

            if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                throw new TwinCatException($"Timeout waiting for unit test to be done after {timeoutMs / 1000} seconds");

            await Task.Delay(pollIntervalMs);
        }

        log.LogInformation("Results from unit tests:");
        await tcUnitRunner.PrintResultsFromUnitTest();
    }
    #endregion
}
