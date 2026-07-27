using Microsoft.Extensions.Logging;
using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all System related code.
/// </summary>
public partial class AutomationInterface
{
    /// <summary>
    /// Configures a USB license dongle for the build server under the Real-Time License tree item.
    /// </summary>
    internal void SetUsbLicense()
    {
        if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("USB license dongle setup is only supported for XAE projects");

        log.LogInformation("Setting up USB license dongle");
        string testxml = realTimeLicense!.ProduceXml();
        Console.WriteLine(testxml);
        // TODO: Not tested properly, need to verify that the child item is created correctly and that the license is recognized by the system.
        // Also need to determine if additional parameters are needed for the dongle item.
        ITcSmTreeItem usbDongle = realTimeLicense!.CreateChild("BuildDongle", 0, null, "EsbBox 254 (Dynamic)");
    }
}