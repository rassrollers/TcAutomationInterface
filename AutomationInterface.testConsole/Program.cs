using AutomationInterface.core;
using CommandLine;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

// Setup configuration from appsettings.json
IConfiguration config = AppTools.SetupConfiguration(new ConfigurationBuilder());

// Setup logger for application
ILogger logger = AppTools.SetupLogger(config, "ConsoleApp");
logger.LogInformation("Application starting up...");

// Parse arguments in depending in build type
if (args.Length == 0)
{
    logger.LogError("No arguments provided. Required arguments: -w|--WorkDir, -s|--SolutionName, -t|--Type.");
    Environment.Exit(1);
}

BuildOptions? options = null;
Parser.Default.ParseArguments<BuildOptions>(args)
    .WithParsed<BuildOptions>(opt =>
    {
        options = opt;
    })
    .WithNotParsed(errors => 
    { 
        logger.LogError("Invalid or missing arguments: {args}", string.Join(", ", args));
        Environment.Exit(1);
    });

try
{
    AppTools.VerifyThatSystemIsWindows();
#pragma warning disable CA1416 // Disable the warning on only Windows platforms

    options!.SolutionPath = FilesAndPathTools.FindFile(options!.WorkDir, options!.SolutionName)
        ?? throw new FileNotFoundException($"The file '{options.SolutionName}' was not found in the directory '{options.WorkDir}'.");
    AppTools.PrintBuildInfo(logger, options);

    #region Attach to running XAE instance test
    //var xae = new TcXaeShellEnvironment(logger);
    //
    //var projects = xae.FindProjectsInSolution(options.SolutionPath); 
    //
    //var dtes = xae.FindRunningEnvironments();
    //
    //logger.LogInformation("Select a DTE [0-{dteIndex}]: ", dtes.Count()-1);
    //var index = int.Parse(Console.ReadLine() ?? "-1");
    //
    //if (index>= 0 && index < dtes.Count())
    //{
    //    var selectedDte = dtes.ElementAt(index);
    //    logger.LogInformation("Selected DTE: {dte}", selectedDte);
    //
    //    await xae.AttachToEnvironment(selectedDte);
    //    await xae.CleanAndRebuildSolution();
    //
    //}
    #endregion

    #region ADS test
    //AdsHandler adsHandler = new(logger);
    //
    //string netId = config["Target:NetId"] ?? throw new InvalidOperationException("TwinCat NetId is not configured.");
    //logger.LogInformation("Connecting to TwinCAT system with NetId: {netId}", netId);
    //adsHandler.Connect(netId, 10000);
    //
    //var delay = TimeSpan.FromSeconds(10);
    //
    //logger.LogInformation("Connected to TwinCAT system: {isConnected}", adsHandler.IsConnected());
    //logger.LogInformation(adsHandler.GetDeviceInfo());
    //logger.LogInformation("TwinCAT is in run mode: {isRunMode}", adsHandler.IsTwinCatInRunMode());
    //
    //logger.LogInformation(adsHandler.GetTwinCatStatus());
    //adsHandler.SetTwinCatInConfigMode();
    //await Task.Delay(delay);
    //
    //logger.LogInformation(adsHandler.GetTwinCatStatus());
    //adsHandler.SetTwinCatInRunMode();
    //await Task.Delay(delay);
    //
    //logger.LogInformation(adsHandler.GetTwinCatStatus());
    #endregion

    #region Opening XAE environment test
    // Handling Git information and make sure that there are no uncommitted changes
    //using GitInfo git = new(logger);
    //git.GitRepository(options.WorkDir);
    //git.PrintInformation();
    //
    //// Open TcXae environment, inject Git version to project and build project
    //using TcXaeShellEnvironment env = new(logger);
    ////env.InjectBuildLicenses();
    //var projects = env.FindProjectsInSolution(options.SolutionPath);
    //
    //await env.OpenEnvironment(options.SolutionPath, true, true);
    //
    //await env.SelectProject(projects.First());
    ////env.InjectGitVersion(git);
    //
    //switch (options!.Type)
    //{
    //    case BuildType.Library:
    //        await env.SaveLibrary(options.WorkDir, git.GetShortVersion()!);
    //        break;
    //
    //    case BuildType.Project:
    //        //await env.ReloadMotionTasks();
    //        //env.InstallLibrariesFromDirectory(Path.Join(options.WorkDir, "Software\\Software\\I_Cut\\_Libraries"));
    //        //await env.CleanAndRebuildSolution();
    //        //env.ConfigureTarget(config);
    //        //env.DownloadToPlc();
    //        //env.StartTwinCat();
    //        Console.WriteLine("Done!");
    //        Console.ReadLine();
    //
    //        break;
    //}
    #endregion

    #region Create new XAE project test
    //using TcXaeShellEnvironment env = new(logger);

    //var runningEnvironments = env.FindRunningEnvironments();
    //if (runningEnvironments.Count() == 0)
    //    throw new TwinCatException("No running TwinCAT XAE Shell environment was found on the system. Please start a new instance of TwinCAT XAE Shell and try again.");
    //await env.AttachToEnvironment(runningEnvironments.First());

    //var xaeList = env.FindInstalledTcXaeShell();
    //if (xaeList.Count == 0)
    //    throw new TwinCatException("No installed TwinCAT XAE Shell was found on the system.");
    //await env.CreateSolutionEnvironment(xaeList.Last(), "TestTemplates", options!.WorkDir, options.UiXae, options.UserControl);
    //await env.CreateProjectFromTemplate(TcProjectType.XaeProject.ToString(), options.WorkDir, "TestProject");
    //await env.AddPlcProjectFromTemplate(TcProjectType.StandardPlcProject.ToString(), "TestPlc");
    //
    //await env.CreateProgramItem("TestProgram", ProgramItemsTypes.Program.ToString(), "POUs");
    //await env.CreateProgramItem("TestFunction", ProgramItemsTypes.Function.ToString(), "POUs", returnType:"Bool");
    //await env.CreateProgramItem("TestFunctionBlock", ProgramItemsTypes.FunctionBlock.ToString(), "POUs");
    //await env.CreateProgramItem("TestStruct", ProgramItemsTypes.Struct.ToString(), "DUTs");
    //await env.CreateProgramItem("TestEnum", ProgramItemsTypes.Enum.ToString(), "DUTs");
    //await env.CreateProgramItem("TestUnion", ProgramItemsTypes.Union.ToString(), "DUTs");
    //await env.CreateProgramItem("TestGVL", ProgramItemsTypes.GVL.ToString(), "GVLs");
    //await env.CreateProgramItem("TestParam", ProgramItemsTypes.ParameterList.ToString(), "GVLs");
    //await env.CreateProgramItem("TestVisu", ProgramItemsTypes.Visualization.ToString(), "VISUs");
    //await env.CreateProgramItem("ITFs", ProgramItemsTypes.Folder.ToString(), "");
    //await env.CreateProgramItem("TestInterface", ProgramItemsTypes.Interface.ToString(), "ITFs");
    //
    //env.InjectGitVersion("1.0.0", "34fds34", "2024-06-05T12:34:56Z");
    //
    //Console.WriteLine("Done!");
    //Console.ReadLine();
    #endregion

    #region Testing system config
    //using TcXaeShellEnvironment env = new(logger);
    //var solutionFiles = FilesAndPathTools.FindSolutionFiles(options!.WorkDir);

    //var xaeList = env.FindRunningEnvironments();
    //if (xaeList.Count == 0)
    //    throw new TwinCatException("No installed TwinCAT XAE Shell was found on the system.");
    //await env.AttachToEnvironment(xaeList.First());

    //var projects = env.FindProjectsInSolution(solutionFiles.First().SolutionFilePath);
    //if (projects.Count() == 0)
    //    throw new TwinCatException("No projects were found in the solution.");
    //await env.SelectProjectInSolution(projects.First());

    //await env.AddLicenseDongle();
    //await env.AddTask("TestTask");
    //await env.AddMotionTask("TestMotionTask");
    //await env.AddMotionAxis();

    //Console.WriteLine("Done!");
    //Console.ReadLine();
    #endregion

    #region Test TcUnitRunner
    using TcXaeShellEnvironment env = new(logger);

    // 1. Find running TcXaeShell instances and let the user pick one
    var runningEnvironments = env.FindRunningEnvironments();
    if (runningEnvironments.Count == 0)
        throw new TwinCatException("No running TwinCAT XAE Shell environment was found on the system. Please start a new instance of TwinCAT XAE Shell and try again.");

    var selectedEnvironment = SelectFromList("Select a running environment", runningEnvironments);
    await env.AttachToEnvironment(selectedEnvironment);

    if (string.IsNullOrWhiteSpace(options.SolutionPath))
        throw new TwinCatException($"No solution is open in '{selectedEnvironment}'.");

    var projects = env.FindProjectsInSolution(options.SolutionPath);
    if (projects.Count == 0)
        throw new TwinCatException($"No projects were found in solution '{options.SolutionPath}'.");

    var selectedProject = SelectFromList("Select a project", projects, Path.GetFileName);
    await env.SelectProjectInSolution(selectedProject);

    // 3. Download to the target, start TwinCAT and wait for TcUnit to finish
    env.DownloadToPlc();
    env.StartTwinCat();
    await env.WaitForUnitTestToDone();
    #endregion

    logger.LogInformation("- - - - - Finished building - - - - -");
}
catch (WrongOSException ex)
{
    logger.LogCritical("Not a Windows OS!");
    logger.LogDebug("{exMsg}", ex.Message);
}
catch (LibGit2SharpException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("No tag in repository");
}
catch (UnauthorizedAccessException ex)
{
    logger.LogCritical($"Access denied!");
    logger.LogDebug("{ex}", ex.ToString());
}
catch (FileNotFoundException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
}
catch (DirectoryNotFoundException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
}
catch (COMException ex)
{
    logger.LogCritical("COM exception: 0x{errorCode:X} - {message}", ex.ErrorCode, ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
}
catch (Exception ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
}
finally
{
    logger.LogInformation("Application is closing...");
}

/// <summary>
/// Prints a numbered list to the console and reads the user's choice until a valid index is entered.
/// </summary>
static T SelectFromList<T>(string prompt, IReadOnlyList<T> items, Func<T, string>? display = null)
{
    display ??= item => item?.ToString() ?? string.Empty;

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine($"{prompt}:");
        for (int i = 0; i < items.Count; i++)
            Console.WriteLine($"  [{i}] {display(items[i])}");
        Console.Write($"Enter number [0-{items.Count - 1}]: ");

        if (int.TryParse(Console.ReadLine(), out int index) && index >= 0 && index < items.Count)
            return items[index];

        Console.WriteLine("Invalid selection, try again.");
    }
}