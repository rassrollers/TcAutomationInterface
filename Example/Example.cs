// Setup configuration from appsettings.json
using AutomationInterface.core;
using CommandLine;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;
using TwinCatBuildServerTool;

string AppName = Assembly.GetExecutingAssembly().GetName().Name!;
string AppVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

// Load configuration from appsettings.json
IConfiguration config = AppTools.SetupConfiguration(new ConfigurationBuilder());

// Setup logger for application
ILogger logger = AppTools.SetupLogger(config, "ConsoleApp");
logger.LogInformation("{name} {version} starting up...", AppName, AppVersion);

// Parse arguments in depending in build type
BuildOptions? options = null;
Parser.Default.ParseArguments<BuildOptions>(args)
    .WithParsed(opt =>
    {
        options = opt;
    })
    .WithNotParsed(errors =>
    {
        throw new ArgumentException("Missing or no arguments: {args}", string.Join(", ", args));
    });

try
{
    AppTools.VerifyThatSystemIsWindows();
#pragma warning disable CA1416 // Validate platform compatibility

    // Finds solution and project files in WorkDir
    options!.SolutionPath = FilesAndPathTools.FindFile(options!.WorkDir, options!.SolutionName)
        ?? throw new FileNotFoundException($"The file '{options.SolutionName}' was not found in the directory '{options.WorkDir}'.");
    AppTools.PrintBuildInfo(logger, options);

    // Handling Git information and make sure that there are no uncommitted changes
    using GitInfo git = new(logger);
    git.GitRepository(options.WorkDir);
    git.PrintInformation();

    // ADS handler for System Service
    using AdsHandler ads = new(logger);
    string netId = config["Target:NetId"] ?? throw new ArgumentException("No NetID defined in the appsettings.json");
    int netPort_System = 10000;
    ads.Connect(netId, netPort_System);

    // TwinCAT XAE environment with Automation Interface
    using TcXaeShellEnvironment tcEnv = new(logger);
    await tcEnv.OpenEnvironment(options.SolutionPath);
    await tcEnv.ReloadMotionTasks();
    tcEnv.InstallLibrariesFromDirectory(Path.Combine(options.WorkDir, "PlcProjecy\\_Libraries"));
    tcEnv.SetProjectVariant("iTest");
    await tcEnv.CleanAndRebuildSolution();

    switch (options.Type)
    {
        case BuildType.Project:
            tcEnv.ConfigureTarget(config);
            tcEnv.DownloadToPlc();
            tcEnv.StartTwinCat();
            await Task.Delay(TimeSpan.FromSeconds(5));

            logger.LogInformation(ads.GetTwinCatStatus());
            if (!ads.IsTwinCatInRunMode())
            {
                logger.LogWarning("TwinCAT did not start after download. Trying to restart TwinCAT...");
                ads.SetTwinCatInRunMode();
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!ads.IsTwinCatInRunMode())
                    throw new TwinCatException("TwinCAT system service is not in RUN mode after trying to restart");
            }
            break;

        default:
            logger.LogError("Unknown build type: {type}", options.Type);
            break;
    }
    logger.LogInformation("- - - - - Finished building: {type} - - - - -", options.Type.ToString());
    return ExitCode.Success;
}
catch (WrongOSException ex)
{
    logger.LogCritical("Not a Windows OS!");
    logger.LogDebug("{exMsg}", ex.Message);
    return ExitCode.SystemError;
}
catch (LibGit2SharpException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("No tag in repository");
    return ExitCode.ArgumentError;
}
catch (ArgumentException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.ArgumentError;
}
catch (TwinCatException ex)
{
    logger.LogCritical("TwinCAT error: {exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.TwinCatError;
}
catch (AutomationInterfaceException ex)
{
    logger.LogCritical("Automation Interface error: {exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.TwinCatError;
}
catch (TcXmlException ex)
{
    logger.LogCritical("TcXml error: {exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.TwinCatError;
}
catch (UnauthorizedAccessException ex)
{
    logger.LogCritical($"Access denied!");
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.TwinCatError;
}
catch (FileNotFoundException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.SystemError;
}
catch (DirectoryNotFoundException ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.SystemError;
}
catch (COMException ex)
{
    logger.LogCritical("COM exception: 0x{errorCode:X} - {message}", ex.ErrorCode, ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.ComObjectError;
}
catch (Exception ex)
{
    logger.LogCritical("{exMsg}", ex.Message);
    logger.LogDebug("{ex}", ex.ToString());
    return ExitCode.Unknown;
}
finally
{
    logger.LogInformation("{name} is closing...", AppName);
}
