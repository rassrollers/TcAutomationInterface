using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace AutomationInterface.core;

/// <summary>
/// Provides static utility methods for application setup including configuration loading,
/// logger creation, and OS verification.
/// </summary>
public static class AppTools
{
    /// <summary>
    /// Builds an <see cref="IConfiguration"/> from the <c>appsettings.json</c> file and environment variables.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to configure.</param>
    /// <returns>A fully built <see cref="IConfiguration"/> instance.</returns>
    public static IConfiguration SetupConfiguration(IConfigurationBuilder builder)
    {
        return builder.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Creates and configures an <see cref="ILogger"/> with console and optional debug providers.
    /// </summary>
    /// <param name="config">The application configuration used to read the <c>Logging</c> section.</param>
    /// <param name="loggerName">The category name for the logger.</param>
    /// <param name="extraProvider">Optional additional <see cref="ILoggerProvider"/> instances to register.</param>
    /// <returns>A configured <see cref="ILogger"/> instance.</returns>
    public static ILogger SetupLogger(IConfiguration config, string loggerName, IEnumerable<ILoggerProvider>? extraProvider = null)
    {
        ILoggerFactory logFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(config.GetSection("Logging"));
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
#if DEBUG
            builder.AddDebug();
#endif
        });

        if (extraProvider != null)
        {
            foreach (var provider in extraProvider)
            {
                logFactory.AddProvider(provider);
            }
        }

        return logFactory.CreateLogger(loggerName);
    }

    /// <summary>
    /// Verifies that the current operating system is Windows.
    /// </summary>
    /// <exception cref="WrongOSException">Thrown when the OS is not Windows.</exception>
    public static void VerifyThatSystemIsWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new WrongOSException(Environment.OSVersion.Platform.ToString());
    }

    /// <summary>
    /// Logs the build configuration details to the provided logger.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> instance to write to.</param>
    /// <param name="opt">The <see cref="BuildOptions"/> containing the current build parameters.</param>
    public static void PrintBuildInfo(ILogger logger, BuildOptions opt)
    {
        logger.LogInformation("- - - - - Build info - - - - -");
        logger.LogInformation($"Build type: {opt.Type.ToString()}");
        logger.LogInformation($"Working directory: {opt.WorkDir}");
        logger.LogInformation($"Solution name: {opt.SolutionName} at {opt.SolutionPath}");
        logger.LogInformation($"Show XAE UI: {opt.UiXae}");
        logger.LogInformation($"User Control: {opt.UserControl}");
    }
}
