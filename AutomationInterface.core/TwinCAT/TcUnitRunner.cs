using EnvDTE80;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace AutomationInterface.core;

/// <summary>
/// Monitors and reports TcUnit test execution results by polling the Visual Studio Error List.
/// </summary>
[SupportedOSPlatform("windows")]
class TcUnitRunner
{
    private readonly ILogger log;
    private readonly VisualStudioEnvironment vsEnv;
    private List<ErrorItem>? _errorItems = null;

    private const string TCUNIT_FINISHED = "TESTS FINISHED RUNNING";

    /// <summary>
    /// Initializes a new instance of the <see cref="TcUnitRunner"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="vsEnv">The Visual Studio environment abstraction for querying error items.</param>
    public TcUnitRunner(ILogger logger, VisualStudioEnvironment vsEnv)
    {
        log = logger;
        this.vsEnv = vsEnv;
    }

    /// <summary>
    /// Checks whether TcUnit has finished executing all tests by looking for the
    /// <c>TESTS FINISHED RUNNING</c> message in the Error List.
    /// </summary>
    /// <returns><see langword="true"/> if TcUnit reports that tests have finished; otherwise <see langword="false"/>.</returns>
    public async Task<bool> IsTcUnitDone()
    {
        _errorItems = await vsEnv.GetErrorItems();
        for (int i = 1; i <= _errorItems.Count; i++)
        {
            ErrorItem item = _errorItems[i];
            if (item.ErrorLevel < vsBuildErrorLevel.vsBuildErrorLevelHigh)
                continue;

            if (item.Description.Contains(TCUNIT_FINISHED))
            {
                Thread.Sleep(5000); // Extra wait for last results to printed out
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Logs all high-severity error items from the Error List, sorted by description.
    /// These represent individual TcUnit test results.
    /// </summary>
    public async Task PrintResultsFromUnitTest()
    {
        _errorItems = await vsEnv.GetErrorItems();
        var errorList = _errorItems
            .Where(e => e.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelHigh)
            .OrderBy(e => e.Description)
            .ToList();
        foreach (var item in errorList)
        {
            log.LogError("{description} | {filename}", item.Description, item.FileName);
        }
    }
}
