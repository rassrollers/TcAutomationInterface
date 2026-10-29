using EnvDTE;
using EnvDTE80;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// All access to the DTE must be done through this class to ensure that the COM objects are properly released
/// and that calls are marshaled to the correct STA thread.
/// This class also implements retry logic for COM calls that may be rejected by Visual Studio when it is busy,
/// such as during a build operation.
/// The retry logic will attempt the COM call multiple times with a delay in between,
/// and will log each retry attempt for debugging purposes.
/// </summary>

[SupportedOSPlatform("windows")]
public class VisualStudioEnvironment : IDisposable, IAsyncDisposable
{
    private readonly StaComHost host;
    private DTE2? vsDte;
    private Solution2? vsSolution;
    private SolutionBuild2? vsSolutionBuild;
    private BuildEvents? vsBuildEvent;
    private Project? vsProject = null;
    private TaskCompletionSource<bool>? buildCompletionSource;
    private readonly object buildLock = new();
    private int buildDepth = 0;
    private bool isClosed;
    private bool isAttached = false; // Track if we attached to an existing instance
    private readonly ILogger log;
    private volatile bool isDteHealthy = true;
    private int? dteProcessId; // devenv.exe PID, used as a fallback to force-kill a hung instance on close

    #region Class initialization and disposal
    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStudioEnvironment"/> class,
    /// creating a dedicated STA COM host thread for marshaling DTE calls.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public VisualStudioEnvironment(ILogger logger)
    {
        host = new StaComHost();
        log = logger;
    }

    /// <summary>
    /// Asynchronously disposes the environment by closing the DTE and releasing all COM resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Synchronously disposes the environment by awaiting <see cref="CloseAsync"/>.
    /// </summary>
    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Closes the Visual Studio DTE instance asynchronously. Marshals the cleanup to the STA host thread.
    /// Subsequent calls are no-ops once the environment is closed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous close operation.</returns>
    public Task CloseAsync()
    {
        lock (this)
        {
            if (isClosed)
                return Task.CompletedTask;

            isClosed = true;
        }

        // Marshal the CloseCore call onto the STA host thread
        return host.RunAsync(() =>
        {
            try
            {
                CloseCore();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Exception during CloseCore execution.");
                throw;
            }
        });
    }

    /// <summary>
    /// Performs the actual cleanup of all COM references (build events, solution build, solution,
    /// project, and DTE). Must be called on the STA thread.
    /// </summary>
    public void CloseCore()
    {
        log.LogInformation("Closing the Visual Studio Development Tools Environment (DTE)...");
        ReleaseSolutionReferences();

        if (vsDte is not null)
        {
            bool quitSucceeded = isAttached; // nothing to quit if we attached to someone else's instance
            try
            {
                // Only call Quit if we created this instance, not if we attached to it
                if (!isAttached)
                {
                    vsDte.Quit();
                    quitSucceeded = true;
                }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800706BA))
            {
                log.LogWarning("DTE already terminated (RPC server unavailable)");
                quitSucceeded = true;
            }
            catch (COMException ex)
            {
                log.LogWarning(ex, "COM Exception while quitting vsDte: 0x{HResult:X}", ex.HResult);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Unexpected exception while quitting vsDte");
            }

            // Only force-kill if devenv is genuinely hung (not responding); a responsive process
            // that failed Quit() is most likely still busy/initializing and may finish on its own -
            // killing it here just destroys in-progress work for no benefit.
            if (!isAttached && dteProcessId is int pid)
            {
                bool exitedGracefully = false;
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    log.LogDebug("Waiting for devenv.exe (PID {Pid}) to exit after Quit()...", pid);
                    // Wait for 10 seconds for VS and TwinCAT to shut down gracefully
                    exitedGracefully = process.WaitForExit(10000);
                }
                catch (ArgumentException)
                {
                    exitedGracefully = true;
                }

                // If the process is still running after Quit() and the wait, we assume it is hung and force-kill it.
                if (!exitedGracefully)
                {
                    log.LogWarning("devenv.exe (PID {Pid}) stalled during graceful shutdown. Forcing termination...", pid);
                    TryKillProcess(pid);
                }
                else
                {
                    log.LogDebug("devenv.exe (PID {Pid}) terminated successfully.", pid);
                }
            }

            try
            {
                Marshal.FinalReleaseComObject(vsDte);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while releasing vsDte COM object");
            }
            finally
            {
                vsDte = null;
                dteProcessId = null;
            }
        }
    }

    /// <summary>
    /// Detaches from the current DTE instance without quitting it: unsubscribes build events,
    /// releases all COM references (solution, solution build, project, DTE).
    /// The devenv.exe process keeps running. After this call the environment can attach to another instance
    /// or create a new one. Marshals the cleanup to the STA host thread.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous detach operation.</returns>
    internal Task DetachAsync()
    {
        lock (this)
        {
            if (isClosed)
                throw new InvalidOperationException("Cannot detach because the Visual Studio environment has been closed.");
        }

        return host.RunAsync(() =>
        {
            try
            {
                DetachCore();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Exception during DetachCore execution.");
                throw;
            }
        });
    }

    /// <summary>
    /// Releases all COM references held on the DTE without calling <c>Quit</c>, leaving the instance running.
    /// Must be called on the STA thread.
    /// </summary>
    private void DetachCore()
    {
        if (vsDte is null)
        {
            log.LogDebug("No DTE instance to detach from");
            return;
        }

        log.LogInformation("Detaching from the Visual Studio Development Tools Environment (DTE) without closing it...");
        ReleaseSolutionReferences();

        if (!isAttached)
        {
            // An instance created with UserControl = false shuts itself down once the last automation
            // reference is released. Hand control to the user so devenv.exe survives the detach.
            log.LogWarning("Detaching from a DTE instance created by this environment. The instance is left running and will not be closed by this environment.");
            try
            {
                vsDte.UserControl = true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while setting UserControl on vsDte before detaching. The instance may close itself when released.");
            }
        }

        try
        {
            Marshal.FinalReleaseComObject(vsDte);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Exception while releasing vsDte COM object");
        }
        finally
        {
            vsDte = null;
            dteProcessId = null;
            isAttached = false;
            isDteHealthy = true;
        }
    }

    /// <summary>
    /// Unsubscribes build events, cancels any pending build and releases the solution, solution build,
    /// build event and project COM references. Does not touch the DTE itself. Must be called on the STA thread.
    /// </summary>
    private void ReleaseSolutionReferences()
    {
        if (vsBuildEvent is not null)
        {
            try
            {
                vsBuildEvent.OnBuildBegin -= OnBuildBegin;
                vsBuildEvent.OnBuildDone -= OnBuildDone;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while unregistering build event handlers");
            }
            
            try
            {
                Marshal.FinalReleaseComObject(vsBuildEvent);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while releasing vsBuildEvent COM object");
            }
            finally
            {
                vsBuildEvent = null;
            }
        }

        // Complete any pending build tasks with cancellation
        lock (buildLock)
        {
            if (buildCompletionSource != null)
            {
                log.LogWarning("Cancelling pending build operation during DTE shutdown");
                buildCompletionSource.TrySetCanceled();
                buildCompletionSource = null;
            }
            buildDepth = 0;
        }

        if (vsSolutionBuild is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(vsSolutionBuild);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while releasing vsSolutionBuild COM object");
            }
            finally
            {
                vsSolutionBuild = null;
            }
        }

        if (vsSolution is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(vsSolution);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while releasing vsSolution COM object");
            }
            finally
            {
                vsSolution = null;
            }
        }

        if (vsProject is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(vsProject);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Exception while releasing vsProject COM object");
            }
            finally
            {
                vsProject = null;
            }
        }
    }
    #endregion

    #region Retry logic for COM calls
    /// <summary>
    /// COM error code indicating the server is busy and the call should be retried.
    /// This is a transient condition, not a fatal error.
    /// </summary>
    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);

    /// <summary>
    /// COM error code indicating the call was rejected by the message filter.
    /// This is a transient condition, not a fatal error.
    /// </summary>
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);

    /// <summary>
    /// COM error code indicating the RPC server is unavailable.
    /// This typically means the process has crashed or terminated.
    /// </summary>
    private const int RPC_S_SERVER_UNAVAILABLE = unchecked((int)0x800706BA);

    /// <summary>
    /// COM error code indicating the connection has been disconnected.
    /// This typically means the process has terminated.
    /// </summary>
    private const int RPC_E_DISCONNECTED = unchecked((int)0x80010007);

    /// <summary>
    /// Determines whether a <see cref="COMException"/> represents a transient rejection
    /// that should be retried (busy state), rather than a fatal error.
    /// </summary>
    /// <param name="ex">The COM exception to evaluate.</param>
    /// <returns><see langword="true"/> if the call should be retried; otherwise <see langword="false"/>.</returns>
    private static bool IsRetryable(COMException ex)
    {
        int hr = ex.HResult;
        return hr == RPC_E_SERVERCALL_RETRYLATER ||
               hr == RPC_E_CALL_REJECTED;
    }

    /// <summary>
    /// Determines whether a <see cref="COMException"/> represents a fatal error
    /// indicating the DTE process has crashed or terminated.
    /// </summary>
    private static bool IsFatalDteError(COMException ex)
    {
        int hr = ex.HResult;
        return hr == RPC_S_SERVER_UNAVAILABLE ||
               hr == RPC_E_DISCONNECTED;
    }

    /// <summary>
    /// Retries an asynchronous action up to <paramref name="maxRetries"/> times when Visual Studio
    /// rejects the COM call with a transient error.
    /// </summary>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="actionName">A descriptive name used in log messages.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="delayMilliseconds">Delay in milliseconds between retries.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRetries"/> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Thrown when all retry attempts are exhausted.</exception>
    private async Task RetryAsync(Func<Task> action, string actionName, int maxRetries = 10, int delayMilliseconds = 2000)
    {
        if (maxRetries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), $"maxRetries must be at least 1 for {actionName}");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (COMException ex) when (IsRetryable(ex))
            {
                log.LogDebug(
                    "[VisualStudio] {action} rejected by VS, retry {attempt}/{maxRetries}",
                    actionName, attempt, maxRetries);
                LogDteProcessDiagnostics(actionName);

                if (attempt == maxRetries)
                    throw;

                await Task.Delay(delayMilliseconds);
            }
        }
        throw new InvalidOperationException($"Exceeded maximum retry attempts for action: {actionName}");
    }

    /// <summary>
    /// Retries an asynchronous function returning <typeparamref name="T"/> up to <paramref name="maxRetries"/>
    /// times when Visual Studio rejects the COM call with a transient error.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="action">The asynchronous function to execute.</param>
    /// <param name="actionName">A descriptive name used in log messages.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="delayMilliseconds">Delay in milliseconds between retries.</param>
    /// <returns>The result of the function on success.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRetries"/> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Thrown when all retry attempts are exhausted.</exception>
    private async Task<T> RetryAsync<T>(Func<Task<T>> action, string actionName, int maxRetries = 10, int delayMilliseconds = 2000)
    {
        if (maxRetries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), $"maxRetries must be at least 1 for {actionName}");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (COMException ex) when (IsRetryable(ex))
            {
                log.LogDebug(
                    "[VisualStudio] {action} rejected by VS, retry {attempt}/{maxRetries}",
                    actionName, attempt, maxRetries);
                LogDteProcessDiagnostics(actionName);

                if (attempt == maxRetries)
                    throw;

                await Task.Delay(delayMilliseconds);
            }
        }
        throw new InvalidOperationException($"Exceeded maximum retry attempts for action: {actionName}");
    }
    #endregion

    #region DTE Attachment
    /// <summary>
    /// Attaches to an existing TcXaeShell DTE instance using its moniker from the Running Object Table.
    /// Will select the first project in the solution after attaching. Does not call Quit on dispose since we did not create the instance.
    /// </summary>
    /// <param name="instance">The <see cref="RunningDteInstance"/> to attach to.</param>
    /// <param name="openXaeUi">Whether to show the XAE user interface.</param>
    /// <param name="userControl">Whether to enable user control (prevents auto-close of the VS process).</param>
    /// <exception cref="InvalidOperationException">Thrown when attachment fails or DTE is already attached.</exception>
    internal async Task AttachToRunningDte(RunningDteInstance instance, bool openXaeUi = true, bool userControl = true)
    {
        if (vsDte is not null)
            throw new InvalidOperationException("DTE is already initialized. Cannot attach to another instance.");

        log.LogDebug("Attaching to running DTE instance: {displayName}", instance.DisplayName);

        await host.RunAsync(() =>
        {
            try
            {
                vsDte = DteHelper.GetDteByMoniker(instance.Moniker);
                isAttached = true;

                vsDte.UserControl = userControl;
                vsDte.SuppressUI = !openXaeUi;
                vsDte.ToolWindows.ErrorList.ShowErrors = true;
                vsDte.ToolWindows.ErrorList.ShowMessages = true;
                vsDte.ToolWindows.ErrorList.ShowWarnings = true;

                log.LogDebug("Successfully attached to DTE instance");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to attach to running DTE instance");
                throw;
            }
        });

        await SetSolutionHandler();
        await Task.Delay(TimeSpan.FromSeconds(2));
        vsProject = await GetProjectItemByIndex(1);
    }
    #endregion

    #region DTE and Solution handling
    /// <summary>
    /// Creates a DTE2 instance for the specified TcXaeShell version
    /// and configures UI visibility, user control, and error list settings.
    /// </summary>
    /// <param name="tcXaseShellDteVersion">The TcXaeShell DTE ProgID (e.g. <c>TcXaeShell.DTE.15.0</c>).</param>
    /// <param name="openXaeUi">Whether to show the XAE user interface.</param>
    /// <param name="userControl">Whether to enable user control (prevents auto-close of the VS process).</param>
    /// <exception cref="Exception">Thrown when the ProgID is invalid or the DTE instance cannot be created.</exception>
    private async Task CreateDte(string tcXaseShellDteVersion, bool openXaeUi, bool userControl)
    {
        // Retrieve the DTE object
        Type? t = Type.GetTypeFromProgID(progID: tcXaseShellDteVersion, throwOnError: true) 
            ?? throw new Exception("Passed visualStudioDteVersion was invalid");

        var pidsBeforeCreate = System.Diagnostics.Process.GetProcesses().Select(p => p.Id).ToHashSet();

        // Create an instance of the DTE object
        await host.RunAsync(() =>
        {
            vsDte = Activator.CreateInstance(type: t) as DTE2
            ?? throw new Exception("Failed to create an instance of the visualStudioVersion");
        });

        // Mark that we created a new instance (call Quit on dispose)
        isAttached = false;

        // The OLE message filter is registered for the lifetime of the STA host thread (see StaComHost),
        // so busy-call retries already cover the CreateInstance call above and everything below.
        // A freshly started devenv.exe is still initializing here, so keep the explicit retry as a
        // second line of defense (it also covers RPC_E_CALL_REJECTED, which the filter does not retry).
        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                vsDte!.UserControl = userControl; // have devenv.exe (VS environment) automatically close and cleanup when using automation. Hides UI if true.
                vsDte!.SuppressUI = !openXaeUi;

                // Make sure all types of errors in the error list are collected
                vsDte!.ToolWindows.ErrorList.ShowErrors = true;
                vsDte!.ToolWindows.ErrorList.ShowMessages = true;
                vsDte!.ToolWindows.ErrorList.ShowWarnings = true;
            });
        }, "Configuring new DTE instance");

        // Resolve the devenv.exe PID for the force-kill fallback and diagnostics. Done after the
        // configuration above so VS has finished initializing, and retried because a starting VS
        // answers MainWindow with RPC_E_CALL_REJECTED, which the message filter does not retry.
        try
        {
            dteProcessId = await RetryAsync(() =>
            {
                return host.RunAsync(() => GetProcessIdFromDte(vsDte!));
            }, "Resolving devenv process id");
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not determine devenv process id for cleanup fallback");
        }
    }

    /// <summary>
    /// Initializes the solution, solution build, and build event handlers from the current DTE instance.
    /// Subscribes to <see cref="BuildEvents.OnBuildBegin"/> and <see cref="BuildEvents.OnBuildDone"/>
    /// to track build lifecycle.
    /// </summary>
    /// <exception cref="Exception">Thrown when the DTE has not been created.</exception>
    private async Task SetSolutionHandler()
    {
        if (vsDte is null)
            throw new Exception("Failed to retrieve solution handler because DTE was not set");
        await host.RunAsync(() =>
        {
            vsSolution = (Solution2)vsDte.Solution;
            vsSolutionBuild = (SolutionBuild2)vsDte.Solution.SolutionBuild;
            vsBuildEvent = vsDte.Events.BuildEvents;
            vsBuildEvent.OnBuildBegin += OnBuildBegin;
            vsBuildEvent.OnBuildDone += OnBuildDone;
        });
    }

    /// <summary>
    /// Creates a new TwinCAT solution in the specified working directory using the given DTE version.
    /// </summary>
    /// <param name="xaeDte">The TcXaeShell DTE ProgID to use.</param>
    /// <param name="solutionName">The name of the new solution.</param>
    /// <param name="workDir">The directory where the solution will be created.</param>
    /// <param name="openXaeUi">Whether to show the XAE user interface.</param>
    /// <param name="userControl">Whether to enable user control of the VS process.</param>
    internal async Task CreateSolution(string xaeDte, string solutionName, string workDir, bool openXaeUi = false, bool userControl = false)
    {
        await CreateDte(xaeDte, openXaeUi, userControl);
        await SetSolutionHandler();
        await host.RunAsync(() =>
        { 
            vsSolution!.Create(workDir, solutionName);
            vsSolution.SaveAs(Path.Combine(workDir, $"{solutionName}.sln"));
        });

    }

    internal async Task AddProjectFromTemplate(string templatePath, string destinationPath, string projectName)
    {
        if (vsSolution is null)
            throw new Exception("Failed to add project from template because solution was not set");

        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                vsProject = vsSolution.AddFromTemplate(templatePath, destinationPath, projectName);
            });
        }, $"Adding project {projectName} from template {templatePath}");
    }

    /// <summary>
    /// Opens an existing TwinCAT solution file. Automatically detects the matching TcXaeShell DTE version
    /// from the solution file header, creates the DTE, opens the solution, and loads the first project.
    /// </summary>
    /// <param name="pathToSolutionFile">The full path to the <c>.sln</c> file.</param>
    /// <param name="openXaeUi">Whether to show the XAE user interface.</param>
    /// <param name="userControl">Whether to enable user control of the VS process.</param>
    /// <param name="beforeOpen">
    /// Optional hook invoked after the DTE is created but before the solution is opened - used to
    /// enable silent mode early, since solution-load-time consistency checks (e.g. a PLC project's
    /// "Released" flag) can otherwise trigger a modal message box before silent mode is ever set,
    /// which blocks forever on a headless machine.
    /// </param>
    internal async Task OpenSolution(string pathToSolutionFile, bool openXaeUi = false, bool userControl = false, Func<Task>? beforeOpen = null)
    {
        string solutionVsVersion = FindVsVersionInSolution(pathToSolutionFile);
        string tcXaseShellDteVersion = CheckTcXaeShellAvailability(solutionVsVersion);
        await CreateDte(tcXaseShellDteVersion, openXaeUi, userControl);
        await SetSolutionHandler();

        if (beforeOpen is not null)
            await beforeOpen();

        log.LogInformation("Opening solution: {path}", pathToSolutionFile);
        await RetryAsync(() =>
        {
            return host.RunAsync(() => vsSolution!.Open(pathToSolutionFile));
        }, $"Opening solution {pathToSolutionFile}");
        log.LogInformation("Finished opening the solution");
        await Task.Delay(TimeSpan.FromSeconds(5)); // Give the UI time to settle before accessing project item
    }

    /// <summary>
    /// Retrieves a project from the solution by its 1-based index, with retry logic for transient COM rejections.
    /// </summary>
    /// <param name="index">The 1-based index of the project in the solution.</param>
    /// <returns>The <see cref="Project"/> at the specified index.</returns>
    /// <exception cref="Exception">Thrown when the solution is not set or contains no projects.</exception>
    private async Task<Project> GetProjectItemByIndex(int index)
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsSolution is null)
                    throw new Exception("Failed to retrieve project because solution was not set");
                else if (vsSolution.Projects.Count == 0)
                    throw new Exception("No projects found in the solution");
                
                foreach (Project p in vsSolution.Projects)
                {
                    log.LogDebug("Project found in solution: {name}", p.Name);
                }
                return vsSolution.Projects.Item(index);
            });
        }, $"Accessing project {index} in solution");
    }

    internal async Task SelectProjectByName(string projectName)
    {
        await host.RunAsync(() =>
        {
            if (vsSolution is null)
                throw new Exception("Failed to retrieve project because solution was not set");
            else if (vsSolution.Projects.Count == 0)
                throw new Exception("No projects found in the solution");
            foreach (Project p in vsSolution.Projects)
            {
                log.LogDebug("Project found in solution: {name}", p.Name);
                if (p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
                {
                    vsProject = p;
                    log.LogInformation("Selected project: {name}", p.Name);
                    return;
                }
            }
            throw new Exception($"Project with name '{projectName}' not found in the solution");
        });
    }
    #endregion

    #region Environment version
    /// <summary>
    /// Find the Visual Studio version used in the solution file
    /// </summary>
    /// <param name="pathToSolutionFile">Relative path to the solution file</param>
    /// <returns>Visual Studio version number</returns>
    private string FindVsVersionInSolution(string pathToSolutionFile)
    {
        /* Find visual studio version */
        string file;
        try
        {
            file = File.ReadAllText(pathToSolutionFile);
        }
        catch (ArgumentException)
        {
            log.LogError("Was unable to read Visual Studio solution file: {path}", pathToSolutionFile);
            return "N/A";
        }

        string pattern = @"^VisualStudioVersion\s+=\s+(?<version>\d+\.\d+)";
        Match match = Regex.Match(file, pattern, RegexOptions.Multiline);

        if (match.Success)
        {
            log.LogInformation("In Visual Studio solution file, found visual studio version {value}", match.Groups[1].Value);
            return match.Groups[1].Value;
        }
        else
        {
            log.LogError("Was unable to find the visual studio version in solution file");
            return "N/A";
        }
    }

    /// <summary>
    /// Find all the TcXaeShell installed on the system
    /// </summary>
    /// <returns>List of TcXaeShell DTE installed</returns>
    internal static List<string> FindInstalledTcXaeShell()
    {
        const string progIdPattern = @"^TcXaeShell\.DTE\.(?<Version>\d+\.\d+)$";
        Regex regex = new Regex(progIdPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        List<string> installedDTEs = new List<string>();
        using (RegistryKey root = Registry.ClassesRoot)
        {
            // Get all top-level keys under HKCR
            foreach (string subKey in root.GetSubKeyNames())
            {
                if (regex.IsMatch(subKey))
                {
                    installedDTEs.Add(subKey);
                }
            }
        }
        return installedDTEs;
    }

    /// <summary>
    /// Find the available DTE (Development Tools Environment) version for the TcXaeShell
    /// </summary>
    /// <param name="visualStudioVersion">Visual Studio version number, e.g. "15.0"</param>
    /// <returns>TcXaeShell DTE version</returns>
    private string CheckTcXaeShellAvailability(string visualStudioVersion)
    {
        var solutionVersion = new Version(visualStudioVersion);
        log.LogDebug("Determining available versions of Visual studio DTE...");
        List<string> knownVersions = FindInstalledTcXaeShell();

        foreach (var known in knownVersions)
        {
            var v = new Version(ExtractTcXaeVersionNumber(known));
            if (v.Major == solutionVersion.Major)
                return $"TcXaeShell.DTE.{v.ToString()}";
        }
        return "N/A";
    }

    /// <summary>
    /// Extracts the version number portion from a TcXaeShell DTE ProgID string
    /// (e.g. <c>TcXaeShell.DTE.15.0</c> → <c>15.0</c>).
    /// </summary>
    /// <param name="dteVersion">The full ProgID string.</param>
    /// <returns>The version number string.</returns>
    /// <exception cref="ArgumentException">Thrown when the ProgID does not match the expected format.</exception>
    private static string ExtractTcXaeVersionNumber(string dteVersion)
    {
        string pattern = @"^TcXaeShell\.DTE\.(?<Version>\d+\.\d+)$";
        Match match = Regex.Match(dteVersion, pattern, RegexOptions.Multiline);
        if (!match.Success)
            throw new ArgumentException("Unable to match version number in Regex: {reg}", dteVersion);

        return match.Groups[1].Value;
    }
    #endregion

    #region Build actions
    /// <summary>
    /// Executes a build action on the STA thread and waits for the corresponding build-done event.
    /// Uses a <see cref="TaskCompletionSource{TResult}"/> to bridge the DTE build events into async/await.
    /// </summary>
    /// <param name="buildAction">The build action to invoke (e.g. clean or build).</param>
    /// <param name="timeoutMs">Timeout in milliseconds before the build is considered hung (default: 5 minutes).</param>
    /// <exception cref="InvalidOperationException">Thrown when a build is already in progress.</exception>
    /// <exception cref="TimeoutException">Thrown when the build does not complete within the timeout period.</exception>
    private async Task RunBuildAsync(Action buildAction, int timeoutMs = 5 * 60 * 1000)
    {
        Task buildTask;
        CancellationTokenSource? dteCrashDetector = null;

        lock (buildLock)
        {
            if (buildCompletionSource != null)
                throw new InvalidOperationException("A build is already in progress.");

            buildDepth = 0;
            buildCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            buildTask = buildCompletionSource.Task;
        }

        try
        {
            // Create a DTE health monitor
            dteCrashDetector = new CancellationTokenSource();
            var healthCheckTask = MonitorDteHealth(dteCrashDetector.Token);

            await host.RunAsync(() =>
            {
                buildAction();
            });

            var completed = await Task.WhenAny(buildTask, Task.Delay(timeoutMs), healthCheckTask);
            
            if (completed == healthCheckTask)
            {
                // DTE crashed during build
                log.LogError("DTE process crashed or became unresponsive during build operation");
                throw new TwinCatException("Visual Studio DTE crashed during build operation");
            }
            else if (completed != buildTask)
            {
                log.LogError("Build operation timed out after {timeout}ms", timeoutMs);
                throw new TimeoutException($"Build operation timed out after waiting for {timeoutMs}ms.");
            }

            await buildTask; // Ensure any exceptions from the build are observed
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800706BA) || // RPC server unavailable
                                       ex.HResult == unchecked((int)0x80010007))   // CLIPBRD_E_CANT_OPEN
        {
            log.LogError(ex, "DTE COM object became invalid during build operation");
            
            lock (buildLock)
            {
                buildCompletionSource?.TrySetException(new TwinCatException("DTE crashed during build", ex));
            }
            
            throw new TwinCatException("Visual Studio DTE crashed during build operation", ex);
        }
        finally
        {
            dteCrashDetector?.Cancel();
            dteCrashDetector?.Dispose();
            
            lock (buildLock)
            {
                buildCompletionSource = null;
                buildDepth = 0; // Reset depth in case of crash
            }
        }
    }

    /// <summary>
    /// Cleans the solution by invoking a clean build via the DTE with retry logic.
    /// Yields on the STA thread after completion to allow the error list to update.
    /// </summary>
    /// <exception cref="Exception">Thrown when the solution builder has not been initialized.</exception>
    internal async Task CleanSolution()
    {
        if (vsSolutionBuild is null)
            throw new Exception("Visual Studio solution builder was not set before cleaning solution");

        await RetryAsync(() =>
        {
            return RunBuildAsync(() => vsSolutionBuild.Clean(false));
        }, "CleanSolution", delayMilliseconds: 5000);

        await host.RunAsync(async () =>
        {
            // Yield to allow UI thread to process build completion events and update the error list before we attempt to access it again
            await Task.Yield();
        });
    }

    /// <summary>
    /// Builds the entire solution via the DTE with retry logic.
    /// Yields on the STA thread after completion to allow the error list to update.
    /// </summary>
    /// <exception cref="Exception">Thrown when the solution builder has not been initialized.</exception>
    internal async Task BuildSolution()
    {
        if (vsSolutionBuild is null)
            throw new Exception("Visual Studio solution builder was not set before building solution");

        await RetryAsync(() =>
        {
            // False = build solution, True = build active project
            return RunBuildAsync(() => vsSolutionBuild.Build(false)); 
        }, "BuildSolution", delayMilliseconds:5000);

        await host.RunAsync(async () =>
        {
            // Yield to allow UI thread to process build completion events and update the error list before we attempt to access it again
            await Task.Yield();
        });
    }

    /// <summary>
    /// Performs a full rebuild by cleaning and then building the solution sequentially.
    /// </summary>
    internal async Task RebuildSolution()
    {
        await CleanSolution();
        await BuildSolution();
    }

    /// <summary>
    /// Handles the DTE <c>OnBuildBegin</c> event by incrementing the build depth counter.
    /// Tracks nested build operations (e.g. a clean that triggers a subsequent build).
    /// </summary>
    /// <param name="scope">The scope of the build (solution, project, etc.).</param>
    /// <param name="action">The build action type (build, clean, rebuild, deploy).</param>
    private void OnBuildBegin(vsBuildScope scope, vsBuildAction action)
    {
        lock (buildLock)
        {
            buildDepth++;

            log.LogDebug(
                "Build started (depth={depth}): Scope={scope}, Action={action}",
                buildDepth, scope, action);
        }
    }

    /// <summary>
    /// Handles the DTE <c>OnBuildDone</c> event by decrementing the build depth counter.
    /// Signals the <see cref="buildCompletionSource"/> when the outermost build operation completes.
    /// </summary>
    /// <param name="scope">The scope of the build (solution, project, etc.).</param>
    /// <param name="action">The build action type (build, clean, rebuild, deploy).</param>
    private void OnBuildDone(vsBuildScope scope, vsBuildAction action)
    {
        lock (buildLock)
        {
            buildDepth--;

            log.LogDebug(
                "Build finished (depth={depth}): Scope={scope}, Action={action}",
                buildDepth, scope, action);

            // Defensive: Ensure depth doesn't go negative
            if (buildDepth < 0)
            {
                log.LogWarning("Build depth went negative - resetting to 0. This may indicate missing OnBuildBegin events.");
                buildDepth = 0;
            }

            if (buildDepth == 0)
            {
                buildCompletionSource?.TrySetResult(true);
            }
            else if (buildDepth < 0)
            {
                // If we somehow went negative, force completion
                log.LogError("Build depth tracking error detected. Forcing build completion.");
                buildCompletionSource?.TrySetResult(true);
                buildDepth = 0;
            }
        }
    }

    /// <summary>
    /// Marks the DTE instance as unhealthy.
    /// </summary>
    /// <param name="reason"></param>
    private void MarkDteAsUnhealthy(string reason)
    {
        if (isDteHealthy)
        {
            isDteHealthy = false;
            log.LogError("DTE marked as unhealthy: {reason}", reason);
        }
    }

    /// <summary>
    /// Monitors the health of the DTE instance during build operations.
    /// Completes the task if the DTE becomes unresponsive or crashes.
    /// </summary>
    private async Task MonitorDteHealth(CancellationToken cancellationToken)
    {
        const int pollIntervalMs = 5000; // Check every 5 seconds
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 3; // Require multiple failures before declaring unhealthy
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(pollIntervalMs, cancellationToken);
                
                try
                {
                    // Simple health check - try to access a lightweight DTE property
                    await host.RunAsync(() =>
                    {
                        if (vsDte is null)
                            throw new InvalidOperationException("DTE reference is null");
                        
                        // Access a lightweight property to verify COM object is responsive
                        _ = vsDte.Version;
                    });
                    
                    // Success - reset failure counter
                    consecutiveFailures = 0;
                }
                catch (COMException ex) when (IsRetryable(ex))
                {
                    // This is a transient "busy" error - NOT a crash
                    // The DTE is just busy processing (e.g., during build)
                    log.LogTrace(
                        "DTE health check received transient busy signal (0x{HResult:X}), this is normal during builds",
                        ex.HResult);
                    consecutiveFailures = 0; // Reset - this is expected behavior
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x800706BA)) // RPC_S_SERVER_UNAVAILABLE
                {
                    // Fatal: RPC server is unavailable - DTE has crashed
                    log.LogError(ex, "DTE health check failed: RPC server unavailable (DTE process likely crashed)");
                    MarkDteAsUnhealthy($"RPC server unavailable (0x{ex.HResult:X})");
                    return; // Exit task to signal crash
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80010007)) // RPC_E_DISCONNECTED
                {
                    // Fatal: Client disconnected - DTE has terminated
                    log.LogError(ex, "DTE health check failed: RPC disconnected (DTE process terminated)");
                    MarkDteAsUnhealthy($"RPC disconnected (0x{ex.HResult:X})");
                    return; // Exit task to signal crash
                }
                catch (COMException ex)
                {
                    // Unknown COM error - could be fatal
                    consecutiveFailures++;
                    log.LogWarning(
                        ex,
                        "DTE health check encountered COM exception (0x{HResult:X}), consecutive failures: {failures}/{max}",
                        ex.HResult, consecutiveFailures, maxConsecutiveFailures);
                    
                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        log.LogError("DTE health check failed {count} consecutive times, marking as unhealthy", consecutiveFailures);
                        MarkDteAsUnhealthy($"Consecutive COM failures (0x{ex.HResult:X})");
                        return; // Exit task to signal crash
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // DTE reference became null - this is fatal
                    log.LogError(ex, "DTE health check failed: DTE reference is null");
                    MarkDteAsUnhealthy("DTE reference is null");
                    return; // Exit task to signal crash
                }
                catch (Exception ex)
                {
                    // Unexpected exception
                    consecutiveFailures++;
                    log.LogWarning(
                        ex,
                        "DTE health check encountered unexpected exception, consecutive failures: {failures}/{max}",
                        consecutiveFailures, maxConsecutiveFailures);
                    
                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        log.LogError("DTE health check failed {count} consecutive times with unexpected errors", consecutiveFailures);
                        MarkDteAsUnhealthy($"Consecutive unexpected failures: {ex.GetType().Name}");
                        return; // Exit task to signal crash
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation - health monitor is being shut down
            log.LogDebug("DTE health monitor cancelled");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fatal exception in DTE health monitoring task");
        }
    }
    #endregion

    #region General methods
    /// <summary>
    /// Executes the <c>File.SaveAll</c> command in the DTE.
    /// </summary>
    internal async Task SaveAll()
    {
        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsDte is null)
                    throw new Exception("Visual Studio DTE was not set before saving all");
        
                vsDte.ExecuteCommand("File.SaveAll");
            });
        }, "Saving all files in Visual Studio");
    }

    /// <summary>
    /// Gets the full path of the currently open solution.
    /// </summary>
    internal async Task<string> GetSolutionName()
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsDte is null)
                    throw new Exception("Visual Studio DTE was not set before getting it");

                return vsDte.Solution.FullName;
            });
        }, "Getting solution name from DTE");
    }

    /// <summary>
    /// Retrieves a named automation object from the DTE.
    /// </summary>
    /// <param name="objName">The registered name of the automation object (e.g. <c>TcAutomationSettings</c>).</param>
    internal async Task<object> GetObjectFromDte(string objName)
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsDte is null)
                    throw new Exception("Visual Studio DTE was not set before getting it");

                return vsDte.GetObject(objName);
            });
        }, $"Getting object from DTE: {objName}");
    }

    /// <summary>
    /// Reads the Visual Studio / TcXaeShell Error List and returns a thread-safe snapshot of all items.
    /// All COM access happens on the STA thread; the returned <see cref="ErrorListEntry"/> objects are plain .NET objects.
    /// Errors, warnings and messages are all made visible before reading, because the DTE only
    /// returns the items that pass the Error List's current filter.
    /// </summary>
    internal async Task<List<ErrorListEntry>> GetErrorListEntries()
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsDte is null)
                    throw new Exception("Visual Studio DTE was not set before getting Error items");

                var errorList = vsDte.ToolWindows.ErrorList;
                errorList.ShowErrors = true;
                errorList.ShowWarnings = true;
                errorList.ShowMessages = true;

                var items = errorList.ErrorItems;
                int count = items.Count;
                var results = new List<ErrorListEntry>(count);

                // ErrorItems is a 1-based COM collection: valid indexes are 1..Count (inclusive)
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        ErrorItem item = items.Item(i);
                        results.Add(new ErrorListEntry
                        {
                            Index = i,
                            Severity = ToSeverity(item.ErrorLevel),
                            Description = SafeGet(() => item.Description) ?? string.Empty,
                            FileName = SafeGet(() => item.FileName) ?? string.Empty,
                            Line = SafeGet(() => item.Line),
                            Column = SafeGet(() => item.Column),
                            Project = SafeGet(() => item.Project) ?? string.Empty
                        });
                    }
                    catch (COMException ex) when (IsRetryable(ex) || IsFatalDteError(ex))
                    {
                        throw; // Let RetryAsync / health monitoring handle it
                    }
                    catch (Exception ex)
                    {
                        // The list can shrink while we iterate (e.g. a build starts) - skip the item
                        log.LogDebug("Could not read Error List item {index}/{count}: {message}", i, count, ex.Message);
                    }
                }

                return results;
            });
        }, "Getting error items from DTE");

        static T? SafeGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch (COMException) { return default; }
        }

        // Maps the DTE build error level to the COM-free ErrorListSeverity
        static ErrorListSeverity ToSeverity(vsBuildErrorLevel level) => level switch
        {
            vsBuildErrorLevel.vsBuildErrorLevelHigh => ErrorListSeverity.Error,
            vsBuildErrorLevel.vsBuildErrorLevelMedium => ErrorListSeverity.Warning,
            _ => ErrorListSeverity.Message
        };
    }

    /// <summary>
    /// Executes a named DTE command.
    /// </summary>
    /// <param name="commandName">The fully qualified command name (e.g. <c>File.SaveAll</c>).</param>
    internal async Task ExecuteDteCommand(string commandName)
    {
        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsDte is null)
                    throw new Exception("Visual Studio DTE was not set before executing command");
                vsDte.ExecuteCommand(commandName);
            });
        }, $"Executing DTE command: {commandName}");
    }

    /// <summary>
    /// Expands a path in the Solution Explorer by navigating through each segment.
    /// </summary>
    /// <param name="parts">The hierarchical path segments to expand.</param>
    /// <returns>The cumulative expanded path string.</returns>
    internal async Task<string> ExpandSolutionExplorerPath(string[] parts)
    {
        UIHierarchy? solutionExplorer = await GetSolutionExplorer();
        UIHierarchyItem item;
        string cumulativePath = "";

        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                foreach (string part in parts)
                {
                    cumulativePath = string.IsNullOrEmpty(cumulativePath) ? part : cumulativePath + "\\" + part;
                    try
                    {
                        item = solutionExplorer!.GetItem(cumulativePath);
                        item.Select(vsUISelectionType.vsUISelectionTypeSelect);
                        item.UIHierarchyItems.Expanded = true;
                    }
                    catch (Exception)
                    {
                        throw new ArgumentException($"Could not find part '{part}' in the Solution Explorer.");
                    }
                }
            });
        }, "Expanding solution explorer path");

        return cumulativePath;
    }

    /// <summary>
    /// Selects an item in the Solution Explorer at the specified path.
    /// </summary>
    /// <param name="itemPath">The full path to the item in the Solution Explorer hierarchy.</param>
    internal async Task SelectSolutionExplorerItem(string itemPath)
    {
        UIHierarchy? solutionExplorer = await GetSolutionExplorer();

        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                try
                {
                    UIHierarchyItem item = solutionExplorer.GetItem(itemPath);
                    item.Select(vsUISelectionType.vsUISelectionTypeSelect);
                }
                catch (Exception)
                {
                    throw new ArgumentException($"Could not find item '{itemPath}' in the Solution Explorer.");
                }
            });
        }, $"Selecting item in solution explorer: {itemPath}");
    }

    /// <summary>
    /// Gets the names of all child items under the specified Solution Explorer path.
    /// </summary>
    /// <param name="path">The parent path in the Solution Explorer.</param>
    /// <returns>A list of child item names.</returns>
    internal async Task<List<string>> GetChildrenOfSolutionPath(string path)
    {
        List<string> children = new();
        UIHierarchy? solutionExplorer = await GetSolutionExplorer();

        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                UIHierarchyItem ncItem = solutionExplorer.GetItem(path);
                UIHierarchyItems items = ncItem.UIHierarchyItems;
                foreach (UIHierarchyItem item in items)
                {
                    children.Add(item.Name);
                }
            });
        }, "Getting children of solution path");

        return children;
    }

    /// <summary>
    /// Activates the Solution Explorer window and returns its <see cref="UIHierarchy"/> handle,
    /// with retry logic for transient COM rejections.
    /// </summary>
    /// <returns>The Solution Explorer <see cref="UIHierarchy"/> instance.</returns>
    /// <exception cref="Exception">Thrown when the DTE is not set or the Solution Explorer cannot be accessed.</exception>
    private async Task<UIHierarchy> GetSolutionExplorer()
    {
        if (vsDte is null)
            throw new Exception("Visual Studio DTE was not set before getting it");

        UIHierarchy? solutionExplorer = null;

        await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                vsDte.Windows.Item(Constants.vsWindowKindSolutionExplorer).Activate();
                solutionExplorer = vsDte.ToolWindows.SolutionExplorer;
            });
        }, "Expanding solution explorer path in DTE");

        return solutionExplorer ?? throw new Exception("Unable do get the Solution Explorer window");
    }

    /// <summary>
    /// Gets the project file extension (without the dot) from the project's UniqueName.
    /// </summary>
    internal async Task<string> GetProjectExtension()
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsProject is null)
                    throw new Exception("Visual Studio project was not set before getting project extension");

                return Path.GetExtension(vsProject.UniqueName).TrimStart('.');
            });
        }, "Getting project extension");
    }

    /// <summary>
    /// Gets TwinCAT System Manager references with retry logic on the STA thread.
    /// </summary>
    internal async Task<(ITcSysManager15 SysManager, ITcConfigManager ConfigManager, ITcSmTreeItem PlcConfig)> GetTwinCatSystemManagerRefs()
    {
        return await RetryAsync(() =>
        {
            return host.RunAsync(() =>
            {
                if (vsProject is null)
                    throw new Exception("Visual Studio project was not set before getting TwinCAT references");

                var sysManager = (ITcSysManager15)vsProject.Object;
                var configManager = (ITcConfigManager)sysManager.ConfigurationManager;
                var plcConfig = (ITcSmTreeItem)sysManager.LookupTreeItem(TreeItems.PLC_CONFIG);

                return (sysManager, configManager, plcConfig);
            });
        }, "Getting TwinCAT System Manager references");
    }

    /// <summary>
    /// Runs an action on the STA thread that owns the DTE and TwinCAT COM objects.
    /// COM objects such as <c>ITcSysManager15</c> are apartment-bound to this thread; accessing them
    /// from any other thread forces COM to marshal the call across apartments, which is what
    /// surfaces as spurious <c>RPC_E_SERVERCALL_RETRYLATER</c>/busy errors.
    /// </summary>
    internal Task RunOnStaThread(Action action) => host.RunAsync(action);

    /// <summary>
    /// Resolves the devenv.exe process id owning the given DTE instance's main window, used as a
    /// fallback to force-kill the process if it later becomes unresponsive to Quit().
    /// </summary>
    private static int? GetProcessIdFromDte(DTE2 dte)
    {
        IntPtr hwnd = new(dte.MainWindow.HWnd);
        if (hwnd == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? null : (int)pid;
    }

    /// <summary>
    /// Forcibly terminates a devenv.exe process that failed to respond to <c>Quit()</c>
    /// (e.g. stuck behind a modal dialog or otherwise hung), so it can't linger and interfere
    /// with subsequent automation.
    /// </summary>
    private void TryKillProcess(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                log.LogWarning("devenv (PID {ProcessId}) did not respond to Quit(); forcibly terminating it", processId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to forcibly terminate devenv process (PID {ProcessId})", processId);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Logs whether the devenv process is responding and lists its visible top-level window titles,
    /// to help distinguish a genuinely busy VS from a hung process or one stuck behind a modal dialog.
    /// </summary>
    private void LogDteProcessDiagnostics(string context)
    {
        if (dteProcessId is not int pid)
        {
            log.LogDebug("[VisualStudio] {Context}: no devenv process id captured for diagnostics", context);
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            log.LogDebug(
                "[VisualStudio] {Context}: devenv PID {Pid} Responding={Responding} MainWindowTitle='{Title}'",
                context, pid, process.Responding, process.MainWindowTitle);

            var siblingPids = System.Diagnostics.Process.GetProcessesByName(process.ProcessName).Select(p => p.Id).ToList();
            log.LogDebug(
                "[VisualStudio] {Context}: {Count} running instance(s) of '{ProcessName}': {Pids}",
                context, siblingPids.Count, process.ProcessName, string.Join(",", siblingPids));

            // Per-thread wait reasons reveal what a stuck-but-responding process is actually blocked
            // on (e.g. LpcReply/an RPC reply that never arrives) without needing a debugger.
            var threadStates = process.Threads
                .Cast<System.Diagnostics.ProcessThread>()
                .Select(t => t.ThreadState == System.Diagnostics.ThreadState.Wait
                    ? $"{t.Id}:{t.ThreadState}/{t.WaitReason}"
                    : $"{t.Id}:{t.ThreadState}")
                .ToList();
            log.LogDebug("[VisualStudio] {Context}: devenv PID {Pid} thread states: {ThreadStates}", context, pid, string.Join(", ", threadStates));

            var titles = new List<string>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != pid || !IsWindowVisible(hWnd))
                    return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                var sb = new System.Text.StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                titles.Add(sb.ToString());
                return true;
            }, IntPtr.Zero);

            if (titles.Count > 0)
                log.LogDebug("[VisualStudio] {Context}: devenv PID {Pid} visible windows: {Titles}", context, pid, string.Join(" | ", titles));
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "[VisualStudio] {Context}: failed to gather devenv process diagnostics for PID {Pid}", context, pid);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    #endregion
}
