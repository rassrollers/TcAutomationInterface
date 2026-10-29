using Microsoft.Extensions.Logging;
using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all library management related code.
/// </summary>
public partial class AutomationInterface
{
    #region Library references
    // Library references
    private ITcSmTreeItem? plcLibraryReference = null;
    private ITcPlcLibraryManager? plcLibraryManager = null;

    /// <summary>
    /// Locates the PLC library references by resolving the <c>References</c> node
    /// from the IEC PLC project path in the system manager tree.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when the IEC PLC project is not set or the library reference cannot be found.
    /// </exception>
    private void FindLibraryRefs()
    {
        if (plcIecProject is null)
            throw new AutomationInterfaceException("IEC PLC project reference was not set");
        
        Retry(() =>
        {
            dynamic plcIecObject = plcIecProject;
            string pathName = plcIecObject.PathName;
            plcLibraryReference = (ITcSmTreeItem)sysManager!.LookupTreeItem($"{pathName}^References");
            plcLibraryManager = (ITcPlcLibraryManager)plcLibraryReference;
            log.LogInformation("Found PLC project libraries");
        }, actionName: "PlcLibraryReference", maxRetries: 5, delayMilliseconds: 1000);
        
        if (plcLibraryReference is null)
            throw new AutomationInterfaceException("Was unable to determine the project library object");
    }
    #endregion

    #region Library item extractor
    /// <summary>
    /// Produces XML from the library tree item and wraps it in a <see cref="PlcProjectXml"/> instance.
    /// </summary>
    /// <returns>A <see cref="PlcProjectXml"/> representing the library metadata.</returns>
    /// <exception cref="AutomationInterfaceException">Thrown when XML production fails.</exception>
    private PlcProjectXml ExtractLibraryXml()
    {
        log.LogDebug("Modifying Library info xml!");
        PlcProjectXml? LibXml = null;

        Retry(() =>
        {
            LibXml = new PlcProjectXml(plcLibraryReference!.ProduceXml());
        }, actionName: "LibraryXmlProduce", maxRetries: 5, delayMilliseconds: 1000);

        if (LibXml is null)
            throw new AutomationInterfaceException("Failed to produce xml from the Library tree item");

        return LibXml;
    }
    #endregion

    /// <summary>
    /// Configures the TcUnit library reference to enable publishing test results to the specified path.
    /// Modifies the library XML parameters and saves all changes.
    /// </summary>
    /// <param name="resultPath">The file path on the target where TcUnit results will be written (e.g. <c>/home/Administrator/TcUnitResults.xml</c>).</param>
    internal async Task SetupTcUnitLibrary(string resultPath)
    {
        log.LogInformation("Setting up TcUnit library to publish to {path}", resultPath);
        PlcProjectXml libXml = ExtractLibraryXml();
        libXml.SetTcUnitPublish(resultPath);
        plcLibraryReference!.ConsumeXml(libXml.ToXmlString());
        await vsEnv.SaveAll();
    }

    /// <summary>
    /// Installs all <c>.library</c> files found recursively in the specified directory into the system library repository.
    /// Skips libraries that are already installed.
    /// </summary>
    /// <param name="directoryPath">The root directory to scan for <c>.library</c> files.</param>
    internal void InstallLibrariesFromDirectory(string directoryPath)
    {
        var libraries = Directory.GetFiles(directoryPath, "*.library", SearchOption.AllDirectories);
        foreach (var library in libraries)
        {
            string libName = Path.GetFileNameWithoutExtension(library);
            log.LogInformation("Installing {library} from {path}", libName, library);
            Retry(() =>
            {
                try
                {
                    plcLibraryManager!.InstallLibrary("System", library, false);
                }
                catch (IOException ex) when (ex.Message.Contains("Library already exists"))
                {
                    log.LogInformation("Library {library} already exists, skipping installation", libName);
                }
            }, $"Installing_Library_{libName}");
        }
    }
}