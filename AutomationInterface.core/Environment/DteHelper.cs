using EnvDTE;
using EnvDTE80;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace AutomationInterface.core;

/// <summary>
/// Information about a running DTE instance found in the Running Object Table (ROT).
/// </summary>
public class RunningDteInstance
{
    /// <summary>
    /// The display name of the DTE instance (e.g., solution file name or "TcXaeShell.DTE.15.0").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The moniker string used to identify this instance in the ROT.
    /// </summary>
    public string Moniker { get; set; } = string.Empty;

    /// <summary>
    /// The TcXaeShell DTE version (e.g., "15.0").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The process ID of the running DTE instance, if available.
    /// </summary>
    public int? ProcessId { get; set; }
}

[SupportedOSPlatform("windows")]
internal static class DteHelper
{
    [DllImport("ole32.dll")]
    public static extern int GetRunningObjectTable(int reserved, out IntPtr prot);

    [DllImport("ole32.dll")]
    public static extern int CreateBindCtx(int reserved, out IntPtr ppbc);

    /// <summary>
    /// Extracts the version number from a DTE moniker string.
    /// </summary>
    /// <param name="moniker">The moniker string (e.g., "!TcXaeShell.DTE.15.0":12345).</param>
    /// <returns>The version string (e.g., "15.0"), or "Unknown" if not found.</returns>
    public static string ExtractVersionFromMoniker(string moniker)
    {
        try
        {
            Match match = Regex.Match(moniker, @"TcXaeShell\.DTE\.(\d+\.\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Extracts the process ID from a DTE moniker string.
    /// </summary>
    /// <param name="moniker">The moniker string (e.g., "!TcXaeShell.DTE.15.0":12345).</param>
    /// <returns>The process ID, or null if not found.</returns>
    public static int? ExtractProcessIdFromMoniker(string moniker)
    {
        try
        {
            Match match = Regex.Match(moniker, @":(\d+)$");
            return match.Success && int.TryParse(match.Groups[1].Value, out int pid) ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Discovers all running TcXaeShell DTE instances by querying the Running Object Table (ROT).
    /// </summary>
    /// <returns>A list of <see cref="RunningDteInstance"/> objects representing active DTE instances.</returns>
    public static List<RunningDteInstance> FindRunningDteInstances()
    {
        using var rotAccessor = new RunningObjectTableAccessor();
        var enumerator = new RotMonikerEnumerator(rotAccessor);
        var filter = new TcXaeShellDteFilter();
        var instanceFactory = new RunningDteInstanceFactory();

        return enumerator
            .EnumerateMonikers()
            .Where(filter.IsMatch)
            .Select(instanceFactory.Create)
            .Where(instance => instance != null)
            .Cast<RunningDteInstance>()
            .ToList();
    }

    /// <summary>
    /// Retrieves a DTE instance from the ROT by its moniker.
    /// </summary>
    /// <param name="moniker">The moniker string identifying the DTE instance.</param>
    /// <returns>The DTE2 instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the instance cannot be found or retrieved.</exception>
    public static DTE2 GetDteByMoniker(string moniker)
    {
        using var rotAccessor = new RunningObjectTableAccessor();
        var enumerator = new RotMonikerEnumerator(rotAccessor);

        var targetMoniker = enumerator
            .EnumerateMonikers()
            .FirstOrDefault(m => m.DisplayName == moniker);

        if (targetMoniker == null)
            throw new InvalidOperationException($"Could not find DTE instance with moniker: {moniker}");

        targetMoniker.Rot.GetObject(targetMoniker.Moniker, out object comObject);
        return comObject as DTE2 ?? throw new InvalidOperationException("Failed to cast DTE object to DTE2");
    }
}

// Separate classes for each responsibility
[SupportedOSPlatform("windows")]
internal sealed class RunningObjectTableAccessor : IDisposable
{
    private IntPtr _rotPtr;
    private System.Runtime.InteropServices.ComTypes.IRunningObjectTable _rot;

    public RunningObjectTableAccessor()
    {
        int result = DteHelper.GetRunningObjectTable(0, out _rotPtr);
        if (result != 0)
            throw new COMException($"Failed to get Running Object Table", result);

        _rot = (System.Runtime.InteropServices.ComTypes.IRunningObjectTable)
            Marshal.GetObjectForIUnknown(_rotPtr);
    }

    public System.Runtime.InteropServices.ComTypes.IRunningObjectTable Table => _rot;

    public void Dispose()
    {
        if (_rot != null)
        {
            Marshal.ReleaseComObject(_rot);
            Marshal.Release(_rotPtr);
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class RotMonikerEnumerator
{
    private readonly RunningObjectTableAccessor _rotAccessor;

    public RotMonikerEnumerator(RunningObjectTableAccessor rotAccessor)
    {
        _rotAccessor = rotAccessor;
    }

    public IEnumerable<MonikerInfo> EnumerateMonikers()
    {
        _rotAccessor.Table.EnumRunning(out var enumMoniker);
        enumMoniker.Reset();

        var monikers = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
        IntPtr fetchedPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(int)));

        try
        {
            while (enumMoniker.Next(1, monikers, fetchedPtr) == 0)
            {
                var moniker = monikers[0];
                if (moniker == null) continue;

                string displayName = GetDisplayName(moniker);
                
                yield return new MonikerInfo
                {
                    Moniker = moniker,
                    DisplayName = displayName,
                    Rot = _rotAccessor.Table
                };
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fetchedPtr);
            Marshal.ReleaseComObject(enumMoniker);
        }
    }

    private string GetDisplayName(System.Runtime.InteropServices.ComTypes.IMoniker moniker)
    {
        int result = DteHelper.CreateBindCtx(0, out IntPtr bindCtxPtr);
        if (result != 0) return string.Empty;

        var bindCtx = (System.Runtime.InteropServices.ComTypes.IBindCtx)
            Marshal.GetObjectForIUnknown(bindCtxPtr);

        try
        {
            moniker.GetDisplayName(bindCtx, null, out string displayName);
            return displayName;
        }
        finally
        {
            Marshal.ReleaseComObject(bindCtx);
            Marshal.Release(bindCtxPtr);
        }
    }
}

internal record MonikerInfo
{
    public required System.Runtime.InteropServices.ComTypes.IMoniker Moniker { get; init; }
    public required string DisplayName { get; init; }
    public required System.Runtime.InteropServices.ComTypes.IRunningObjectTable Rot { get; init; }
}

internal sealed class TcXaeShellDteFilter
{
    public bool IsMatch(MonikerInfo monikerInfo)
    {
        return monikerInfo.DisplayName.Contains("TcXaeShell.DTE", StringComparison.OrdinalIgnoreCase);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class RunningDteInstanceFactory
{
    public RunningDteInstance? Create(MonikerInfo monikerInfo)
    {
        string version = DteHelper.ExtractVersionFromMoniker(monikerInfo.DisplayName);
        int? processId = DteHelper.ExtractProcessIdFromMoniker(monikerInfo.DisplayName);

        try
        {
            monikerInfo.Rot.GetObject(monikerInfo.Moniker, out object comObject);

            if (comObject is DTE dte)
            {
                try
                {
                    string solutionPath = dte.Solution?.FullName ?? string.Empty;
                    string solutionName = string.IsNullOrWhiteSpace(solutionPath) 
                        ? "No solution" 
                        : Path.GetFileName(solutionPath);
                    
                    return new RunningDteInstance
                    {
                        DisplayName = solutionName,
                        Moniker = monikerInfo.DisplayName,
                        Version = version,
                        ProcessId = processId
                    };
                }
                finally
                {
                    Marshal.ReleaseComObject(dte);
                }
            }
        }
        catch
        {
            return new RunningDteInstance
            {
                DisplayName = $"TcXaeShell {version} (PID: {processId})",
                Moniker = monikerInfo.DisplayName,
                Version = version,
                ProcessId = processId
            };
        }

        return null;
    }
}
