using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using TCatSysManagerLib;

namespace AutomationInterface.core;

internal enum TcProjectExtension
{
    tsproj, // XAE project types
    tspproj, // PLC project types
    tcmproj // Measurement project types
}

/// <summary>
/// Partial class of Automation Interface containing all base related code.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AutomationInterface : IDisposable
{
    private readonly ILogger log;
    private VisualStudioEnvironment vsEnv;
    private TcProjectExtension projectType;
    // Base project references
    private ITcSysManager15? sysManager = null;
    private ITcConfigManager? configManager = null;
    private ITcSmTreeItem? plcConfig = null;
    // XAE project references
    private ITcSmTreeItem? realTimeConfig = null;
    private ITcSmTreeItem? realTimeLicense = null;
    private ITcSmTreeItem? realTimeAdditionalTasks = null;
    private ITcSmTreeItem? routeConfig = null;
    // Additional settings
    private ITcRemoteManager? tcRemoteManager = null;
    private ITcAutomationSettings? tcAutomationSettings = null;

    #region Constructor and disposal
    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationInterface"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostic output.</param>
    /// <param name="vsEnv">The Visual Studio environment abstraction for DTE access.</param>
    public AutomationInterface(ILogger logger, VisualStudioEnvironment vsEnv)
    {
        log = logger;
        this.vsEnv = vsEnv;
    }

    /// <summary>
    /// Disposes the automation interface by calling <see cref="Close"/>.
    /// </summary>
    public void Dispose() => Close();

    /// <summary>
    /// Closes the automation interface. Currently a no-op placeholder for future cleanup.
    /// </summary>
    public void Close()
    {
        // Release COM objects to prevent memory leaks
        if (sysManager != null)
        {
            Marshal.ReleaseComObject(sysManager);
            sysManager = null;
        }
        if (configManager != null)
        {
            Marshal.ReleaseComObject(configManager);
            configManager = null;
        }
        if (tcRemoteManager != null)
        {
            Marshal.ReleaseComObject(tcRemoteManager);
            tcRemoteManager = null;
        }
        if (tcAutomationSettings != null)
        {
            Marshal.ReleaseComObject(tcAutomationSettings);
            tcAutomationSettings = null;
        }
        
        // Release all tree items and other COM interfaces
        ReleaseComObject(ref plcConfig);
        ReleaseComObject(ref realTimeConfig);
        ReleaseComObject(ref realTimeLicense);
        ReleaseComObject(ref realTimeAdditionalTasks);
        ReleaseComObject(ref routeConfig);
        // AI_PLCProjects
        ReleaseComObject(ref plcProject);
        ReleaseComObject(ref plcProjectTreeItem);
        ReleaseComObject(ref plcIecProject);
        ReleaseComObject(ref plcIecProjectTreeItem);
        // AI_LibraryRepo
        ReleaseComObject(ref plcLibraryReference);
        ReleaseComObject(ref plcLibraryManager);
    }

    private static void ReleaseComObject<T>(ref T? obj) where T : class
    {
        if (obj != null)
        {
            if (Marshal.IsComObject(obj))
            {
                Marshal.ReleaseComObject(obj);
            }
            obj = null;
        }
    }
    #endregion

    #region Retry logic
    private delegate void RetryAction();

    /// <summary>
    /// Retries a COM action up to <paramref name="maxRetries"/> times with a delay between attempts.
    /// Handles <c>RPC_E_SERVERCALL_RETRYLATER</c> (0x8001010A) and dynamic COM busy failures gracefully.
    /// Non-retryable COM exceptions are logged with their <see cref="TCSYSMANAGERHRESULTS"/> name if available.
    /// </summary>
    /// <param name="action">The action delegate to execute.</param>
    /// <param name="actionName">A descriptive name for the action (used in log messages).</param>
    /// <param name="maxRetries">The maximum number of retry attempts.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds between retries.</param>
    private void Retry(RetryAction action, string actionName, int maxRetries = 5, int delayMilliseconds = 1000)
    {
        int attempt = 0;
        Exception? lastException = null;
        
        while (attempt < maxRetries)
        {
            try
            {
                action();
                return; // Success
            }
            catch (COMException ex)
            {
                lastException = ex;
                uint hresult = (uint)ex.HResult;
                if (hresult == 0x8001010A)
                {
                    attempt++;
                    log.LogDebug("[AutomationInterface] Failed to execute action: {action} due to RPC_E_SERVERCALL_RETRYLATER, retrying {attempt}/{maxRetries}", 
                        actionName, attempt, maxRetries);
                    if (attempt >= maxRetries)
                        throw;
                    Thread.Sleep(delayMilliseconds);
                }
                else
                {
                    string? errorName = Enum.GetName(typeof(TCSYSMANAGERHRESULTS), ex.HResult);
                    if (errorName != null)
                        log.LogError("[AutomationInterface] COM Exception occurred: {error} (0x{HResult:X}) - {message}", 
                            errorName, ex.HResult, ex.Message);
                    else
                        log.LogError("[AutomationInterface] Unknown COM Exception: 0x{HResult:X} - {message}", 
                            ex.HResult, ex.Message);
                    throw;
                }
            }
            catch (MissingMemberException ex) when (ex.Message.Contains("0x8001010A"))
            {
                lastException = ex;
                attempt++;
                log.LogDebug("[AutomationInterface] Dynamic COM member busy: {action}, retry {attempt}/{maxRetries}",
                    actionName, attempt, maxRetries);
                if (attempt >= maxRetries)
                    throw;
                Thread.Sleep(delayMilliseconds);
            }
        }
        
        // Should not reach here, but just in case
        throw new AutomationInterfaceException($"Failed to execute {actionName} after {maxRetries} retries", lastException!);
    }
    #endregion

    #region Reference setup
    /// <summary>
    /// Sets up all TwinCAT system manager tree item references from the given DTE project.
    /// Calls <see cref="SetupBaseAiRefs"/>, <see cref="FindPlcProjectRefs"/>,
    /// <see cref="FindIecPlcProjectRefs"/>, and <see cref="FindLibraryRefs"/> in sequence.
    /// </summary>
    internal async Task SetupProjectReferences()
    {
        if (sysManager is null)
            await SetupBaseAiRefs();

        if (projectType == TcProjectExtension.tsproj)
            SetupRealTimeConfigRefs();

        FindPlcProjectRefs();
        FindIecPlcProjectRefs();
        FindLibraryRefs();
    }

    /// <summary>
    /// Initializes the base TwinCAT system manager references (system manager, config manager, PLC config).
    /// </summary>
    /// <exception cref="AutomationInterfaceException">Thrown when the system manager is already initialized.</exception>
    internal async Task SetupBaseAiRefs()
    {
        // Get project extension without touching the COM object directly
        var projectExtension = await vsEnv.GetProjectExtension();
        Enum.TryParse(projectExtension, ignoreCase: true, out projectType);
        
        if (projectType == TcProjectExtension.tcmproj)
            throw new AutomationInterfaceException("Measurement projects are currently not supported by the Automation Interface");

        log.LogInformation("Setting up TwinCAT Automation Interface references");
        
        if (sysManager is null)
        {
            // Delegate COM operations to VisualStudioEnvironment which handles STA marshaling
            var refs = await vsEnv.GetTwinCatSystemManagerRefs();
            sysManager = refs.SysManager;
            configManager = refs.ConfigManager;
            plcConfig = refs.PlcConfig;
        }
        else
            throw new AutomationInterfaceException("System manager is already set");
    }

    private void SetupRealTimeConfigRefs()
    {
        if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("Real-Time configuration references are only available for XAE projects");

        Retry(() =>
        {
            realTimeConfig = (ITcSmTreeItem)sysManager!.LookupTreeItem(TreeItems.RT_CONFIG);
            realTimeLicense = (ITcSmTreeItem)sysManager.LookupTreeItem(TreeItems.RT_CONFIF_LICENSE);
            realTimeAdditionalTasks = (ITcSmTreeItem)sysManager.LookupTreeItem(TreeItems.RT_CONFIG_ADDITIONAL_TASKS);
            routeConfig = (ITcSmTreeItem)sysManager.LookupTreeItem(TreeItems.RT_CONFIG_ROUTE_SETTINGS);
        }, actionName: "RealTimeConfigReferences");

        if (realTimeConfig is null || realTimeLicense is null || realTimeAdditionalTasks is null || routeConfig is null)
            throw new AutomationInterfaceException("Real-Time configuration references were not set properly");
    }
    #endregion

    #region Silent mode
    /// <summary>
    /// Retrieves the <see cref="ITcAutomationSettings"/> object from the DTE if not already set.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when the settings are already initialized or when retrieval fails.
    /// </exception>
    private async Task SetAutomationSettingsIfNeeded()
    {
        if (tcAutomationSettings is not null)
            throw new AutomationInterfaceException("TcAutomationSettings was already set beforehand");

        var obj = await vsEnv.GetObjectFromDte(AutomationInterfaceSettings.TC_AUTOMATION_SETTINGS);
        tcAutomationSettings = obj as ITcAutomationSettings;

        if (tcAutomationSettings is null)
            throw new AutomationInterfaceException("TcAutomationSettings was not set after trying");
    }

    /// <summary>
    /// Make the Automation Interface operate without any mesage boxes or other visible interruptions.
    /// </summary>
    internal async Task SetSilentMode()
    {
        await SetAutomationSettingsIfNeeded();
        Retry(() =>
        {
            tcAutomationSettings!.SilentMode = true; // Only available from TC3.1.4020.0 and above
        }, actionName: "SetSilentMode", maxRetries: 5, delayMilliseconds: 1000);
    }
    #endregion

    #region Remote manager and version management
    /// <summary>
    /// Retrieves the <see cref="ITcRemoteManager"/> object from the DTE if not already set.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when the remote manager is already initialized or when retrieval fails.
    /// </exception>
    private async Task SetRemoteManagerIfNeeded()
    {
        if (tcRemoteManager is not null)
            throw new AutomationInterfaceException("TcRemoteManager was already set beforehand");
            
        var obj = await vsEnv.GetObjectFromDte(AutomationInterfaceSettings.TC_REMOTE_MANAGER);
        tcRemoteManager = obj as ITcRemoteManager;

        if (tcRemoteManager is null)
            throw new AutomationInterfaceException("TcRemoteManager was not set after trying");
    }

    /// <summary>
    /// Find all the installed TwinCAT remote manager versions
    /// </summary>
    /// <returns>List of supported TwinCAT remote manager versions</returns>
    /// <exception cref="AutomationInterfaceException">Failed to retrieve the TcRemoteManager versions</exception>
    private async Task<List<string>> GetTcVersionsSupported()
    {
        await SetRemoteManagerIfNeeded();
        List<string> versions = new();
        Array? tcRemoteManagerVersion = null;
        Retry(() =>
        {
            tcRemoteManagerVersion = tcRemoteManager!.Versions;
        }, actionName: "FetchTcRemoteManagerVersion", maxRetries: 5, delayMilliseconds: 1000);
        if (tcRemoteManagerVersion is null)
            throw new AutomationInterfaceException("Failed at getting TcRemoteManager version");
        foreach (var v in tcRemoteManagerVersion)
        {
            versions.Add(v.ToString()!);
        }
        return versions;
    }

    /// <summary>
    /// Sets the TwinCAT runtime version via the Remote Manager.
    /// Validates that the requested version is installed before applying.
    /// </summary>
    /// <param name="version">The TwinCAT version string to set (e.g. <c>3.1.4024.50</c>).</param>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when no versions are found or the requested version is not installed.
    /// </exception>
    internal async Task SetTcRuntimeVersion(string version)
    {
        List<string> installedVersions = await GetTcVersionsSupported();

        if (installedVersions.Count == 0)
            throw new AutomationInterfaceException("Unable to find any TcVersions in Remote Manager");

        if (!installedVersions.Contains(version))
            throw new AutomationInterfaceException("Requested TcVersion was not found in the RemoteManager versions");
        
        log.LogInformation("Using TcVersion {version} ", version);
        Retry(() =>
        {
            tcRemoteManager!.Version = version;
        }, actionName: "SetTcRemoteManagerVersion", maxRetries: 5, delayMilliseconds: 1000);
    }
    #endregion

    #region Project variant management
    /// <summary>
    /// Get all the available project variants in the current project
    /// </summary>
    /// <returns>A string list of available project variants</returns>
    public List<string> GetAvailableProjectVariants()
    {
        var variants = sysManager!.ProjectVariantConfig;
        XDocument doc = XDocument.Parse(variants);

        List<string> names = doc
            .Descendants("Name")
            .Select(x => x.Value)
            .ToList();
        return names;
    }

    /// <summary>
    /// Set the current project variant if it exists
    /// </summary>
    /// <param name="variantName"></param>
    /// <returns>True if successful</returns>
    public bool SetProjectVariant(string variantName)
    {
        log.LogInformation("Setting project variant: {variantName}", variantName);
        var variants = GetAvailableProjectVariants();
        if (!variants.Contains(variantName))
        {
            log.LogError($"The requested variant '{variantName}' was not found in the project variants.");
            return false;
        }
        sysManager!.CurrentProjectVariant = variantName;
        return true;
    }

    /// <summary>
    /// Gets the currently active project variant name.
    /// </summary>
    /// <returns>The current project variant string.</returns>
    public string GetProjectVariant()
    {
        return sysManager!.CurrentProjectVariant;
    }
    #endregion

    #region Target and platform configuration
    /// <summary>
    /// Sets the target platform/architecture for the TwinCAT configuration (e.g. <c>TwinCAT RT (x64)</c>).
    /// </summary>
    /// <param name="platform">The platform identifier string.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the configuration manager is not initialized.</exception>
    internal void SetPlatform(string platform)
    {
        log.LogInformation("Setting platform/architecture: {platform}", platform);
        if (configManager is null)
            throw new AutomationInterfaceException("Configuration manager was not set");
        
        Retry(() =>
        {
            configManager.ActiveTargetPlatform = platform;
        }, actionName: "SetPlatform", maxRetries: 5, delayMilliseconds: 1000);
    }

    /// <summary>
    /// Sets the target AMS Net ID for the TwinCAT system.
    /// </summary>
    /// <param name="netId">The AMS Net ID string (e.g. <c>192.168.17.10.1.1</c>).</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the system manager is not initialized.</exception>
    internal void SetTarget(string netId)
    {
        log.LogInformation("Setting NetID: {netId}", netId);
        if (sysManager is null)
            throw new AutomationInterfaceException("System manager was not set");
        
        Retry(() =>
        {
            sysManager.SetTargetNetId(netId);
        }, actionName: "SetNetId", maxRetries: 5, delayMilliseconds: 1000);
    }

    /// <summary>
    /// Activates the current TwinCAT configuration on the target system.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">Thrown when the system manager is not initialized.</exception>
    internal void ActivateConfiguration()
    {
        log.LogInformation("Activating configuration");
        if (sysManager is null)
            throw new AutomationInterfaceException("System manager was not set");

        Retry(() =>
        {
            sysManager!.ActivateConfiguration();
        }, actionName: "ActivateConfiguration", maxRetries: 5, delayMilliseconds: 1000);
    }

    /// <summary>
    /// Start/Restart TwinCAT in RUN mode
    /// </summary>
    /// <exception cref="AutomationInterfaceException">System manager was not set</exception>
    internal void StartTwinCAT()
    {
        log.LogInformation("Starting TwinCAT runtime");
        if (sysManager is null)
            throw new AutomationInterfaceException("System manager was not set");
        
        Retry(() =>
        {
            sysManager!.StartRestartTwinCAT();
        }, actionName: "StartRestartTwinCAT", maxRetries: 5, delayMilliseconds: 1000);
    }

    /// <summary>
    /// Checks if the target TwinCAT system is started. This does not necessarily mean that the PLC is in RUN mode, but that the TwinCAT runtime is active.
    /// </summary>
    /// <returns>Return TRUE if TwinCAT system is started</returns>
    /// <exception cref="AutomationInterfaceException">System manager was not set</exception>
    internal bool IsTargetTcSysRunning()
    {
        if (sysManager is null)
            throw new AutomationInterfaceException("System manager was not set");
        
        return sysManager.IsTwinCATStarted();
    }
    #endregion
}
