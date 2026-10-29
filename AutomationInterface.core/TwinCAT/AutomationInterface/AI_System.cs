using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all SYSTEM related code.
/// </summary>
public partial class AutomationInterface
{
    /// <summary>
    /// Adds a license dongle to the SYSTEM under the License tree item.
    /// </summary>
    /// <param name="name">The name of the license dongle.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the SYSTEM license reference is unavailable or the project is not an XAE project.</exception>
    internal void AddLicenseDongle(string name = "Dongle 1")
    {
        if (realTimeLicense is null)
            throw new AutomationInterfaceException("RealTimeLicense reference was not set");
        else if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("license dongle setup is only supported for XAE projects");

        log.LogDebug("Adding a license dongle named: {name}", name);
        ITcSmTreeItem usbDongle = realTimeLicense!.CreateChild(name, 0, null, null);
    }

    /// <summary>
    /// Adds a real-time task under the SYSTEM additional tasks tree item.
    /// </summary>
    /// <param name="name">The name of the real-time task.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the additional tasks reference is unavailable or the project is not an XAE project.</exception>
    internal void AddTask(string name)
    {
        if (realTimeAdditionalTasks is null)
            throw new AutomationInterfaceException("RealTimeAdditionalTasks reference was not set");
        else if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("Adding tasks is only supported for XAE projects");

        log.LogDebug("Adding a new task named: {name}", name);
        ITcSmTreeItem newTask = realTimeAdditionalTasks!.CreateChild(name, 1, null, null);
    }
}