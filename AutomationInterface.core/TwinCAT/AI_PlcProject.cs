using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all PLC project related code.
/// </summary>
public partial class AutomationInterface
{
    #region Project references
    private const string VERSION_INFO_DEFAULT = "TYPE VERSION_INFO:\nSTRUCT\n" +
        "\t\tMAJOR: UINT := 0;\n" +
        "\t\tMINOR: UINT := 0;\n" +
        "\t\tBUILD: UINT := 0;\n" +
        "\t\tREVISION: UINT := 0;\n" +
        "\t\tCOMMIT_HASH: STRING := \"\";\n" +
        "\t\tCOMMIT_TIME: STRING := \"\";\n" +
        "END_STRUCT\nEND_TYPE";
    // PLC project references
    private ITcPlcProject? plcProject = null;
    private ITcSmTreeItem? plcProjectTreeItem = null;
    // IEC PLC project references
    private ITcPlcIECProject? plcIecProject = null;
    private ITcSmTreeItem? plcIecProjectTreeItem = null;

    /// <summary>
    /// Locates the PLC project references by iterating the children of the PLC configuration tree item.
    /// Sets <see cref="plcProjectTreeItem"/> and <see cref="plcProject"/>.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when the PLC configuration is not set or the PLC project cannot be found.
    /// </exception>
    private void FindPlcProjectRefs()
    {
        if (plcConfig is null)
            throw new AutomationInterfaceException("PLC configuration reference was not set");
        
        Retry(() =>
        {
            foreach (ITcSmTreeItem item in plcConfig!)
            {
                if (item is ITcPlcProject project)
                {
                    plcProjectTreeItem = item;
                    plcProject = project;
                }
            }
        }, actionName: "PlcProjectReference", maxRetries: 5, delayMilliseconds: 1000);

        if (plcProjectTreeItem is null)
            throw new AutomationInterfaceException("Was unable to determine the PLC project tree item");

        if (plcProject is null)
            throw new AutomationInterfaceException("Was unable to determine the PLC project");
    }

    /// <summary>
    /// Locates the IEC PLC project references by iterating the children of the PLC project tree item.
    /// Sets <see cref="plcIecProjectTreeItem"/> and <see cref="plcIecProject"/>.
    /// </summary>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when the PLC project tree item is not set or the IEC project cannot be found.
    /// </exception>
    private void FindIecPlcProjectRefs()
    {
        if (plcProjectTreeItem is null)
            throw new AutomationInterfaceException("PLC project tree item was not set");
        
        Retry(() =>
        {
            foreach (ITcSmTreeItem item in plcProjectTreeItem!)
            {
                if (item is ITcPlcIECProject project)
                {
                    log.LogInformation("Found PLC project: {project}", item.Name);
                    plcIecProjectTreeItem = item;
                    plcIecProject = project;
                    break;
                }
            }
        }, actionName: "PlcIecProjectReference", maxRetries: 5, delayMilliseconds: 1000);

        if (plcIecProjectTreeItem is null)
            throw new AutomationInterfaceException("Was unable to determine the PLC project object tree item");

        if (plcIecProject is null)
            throw new AutomationInterfaceException("Was unable to determine the PLC project object");
    }
    #endregion

    #region Project item extractors
    /// <summary>
    /// Produces XML from the IEC PLC project tree item and wraps it in a <see cref="PlcProjectXml"/> instance.
    /// </summary>
    /// <returns>A <see cref="PlcProjectXml"/> representing the current PLC project metadata.</returns>
    /// <exception cref="AutomationInterfaceException">Thrown when XML production fails.</exception>
    private PlcProjectXml ExtractPlcProjectXml()
    {
        log.LogDebug("Modifying Project info xml!");
        PlcProjectXml? projectXml = null;
        Retry(() =>
        {
            projectXml = new PlcProjectXml(plcIecProjectTreeItem!.ProduceXml());
        }, actionName: "ProjectXmlProduce", maxRetries: 5, delayMilliseconds: 1000);

        if (projectXml is null)
            throw new AutomationInterfaceException("Failed to produce xml from the PLC project tree item");

        return projectXml;
    }

    /// <summary>
    /// Extract the PLC declaration area of a child from a relative path.
    /// Can lookup any child of the PLC project, like POU, DUT, etc.
    /// https://infosys.beckhoff.com/english.php?content=../content/1033/tc3_automationinterface/242732427.html
    /// </summary>
    /// <param name="pathName">The relative path name for the PLC file, separated by the '^', e.g. "PRG^Main"</param>
    /// <returns>A interface for accessing the text in the declaration area</returns>
    /// <exception cref="AutomationInterfaceException"></exception>
    private ITcPlcDeclaration ExtractPlcDeclaration(string pathName)
    {
        ITcPlcDeclaration? plcDeclaration = null;
        Retry(() =>
        {
            plcDeclaration = (ITcPlcDeclaration)plcIecProjectTreeItem!.LookupChild(pathName);
        }, actionName: "ExtractPlcDeclaration", maxRetries: 5, delayMilliseconds: 1000);

        if (plcDeclaration is null)
            throw new AutomationInterfaceException($"Failed to extract PLC declaration from POU: {pathName}");

        return plcDeclaration;
    }

    /// <summary>
    /// Retrieves the PLC implementation associated with the specified POU path, retrying the operation if necessary.
    /// </summary>
    /// <param name="pathName">The path name of the POU to extract the PLC implementation from.</param>
    /// <returns>The extracted PLC implementation.</returns>
    /// <exception cref="AutomationInterfaceException">Thrown when the PLC implementation cannot be extracted from the specified POU path.</exception>
    private ITcPlcImplementation ExtractPlcImplementation(string pathName)
    {
        ITcPlcImplementation? plcImplementation = null;
        Retry(() =>
        {
            plcImplementation = (ITcPlcImplementation)plcIecProjectTreeItem!.LookupChild(pathName);
        }, actionName: "ExtractPlcImplementation", maxRetries: 5, delayMilliseconds: 1000);
        if (plcImplementation is null)
            throw new AutomationInterfaceException($"Failed to extract PLC implementation from POU: {pathName}");
        return plcImplementation;
    }
    #endregion

    #region Project configuration
    /// <summary>
    /// Enables the boot project autostart setting on the PLC project.
    /// </summary>
    internal void GenerateBootProject()
    {
        plcProject!.BootProjectAutostart = true;
        //plcProject!.GenerateBootProject(bActivate: true);
    }

    /// <summary>
    /// Gets the name of the PLC project tree item.
    /// </summary>
    /// <returns>The PLC project name.</returns>
    internal string GetProjectName()
    {
        return plcProjectTreeItem!.Name;
    }
    #endregion

    /// <summary>
    /// Updates the <c>VERSION_INFO</c> POU in the PLC project with explicit version metadata.
    /// Injects major, minor, build, revision, commit hash, commit time and build time.
    /// </summary>
    /// <param name="version">Version in <c>major.minor.build.revision</c> format (for example <c>1.0.0.0</c>).</param>
    /// <param name="commitHash">Commit hash to inject.</param>
    /// <param name="commitTime">Commit timestamp to inject.</param>
    /// <exception cref="AutomationInterfaceException">
    /// Thrown when one or more keys could not be matched in the version file declarations.
    /// </exception>
    internal void UpdateVersionFile(string version, string commitHash, string commitTime)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));
        if (string.IsNullOrWhiteSpace(commitHash))
            throw new ArgumentException("Commit hash cannot be null or empty", nameof(commitHash));
        if (string.IsNullOrWhiteSpace(commitTime))
            throw new ArgumentException("Commit time cannot be null or empty", nameof(commitTime));

        Version parsedVersion = Version.Parse(version);
        int major = parsedVersion.Major;
        int minor = parsedVersion.Minor;
        int build = parsedVersion.Build >= 0 ? parsedVersion.Build : 0;
        int revision = parsedVersion.Revision >= 0 ? parsedVersion.Revision : 0;

        if (!ValidateProgramItemPath("Version"))
            CreateProgramItem("Version", ProgramItemsTypes.Folder.ToString());

        if (!ValidateProgramItemPath("Version^VERSION_INFO"))
            CreateProgramItem("VERSION_INFO", ProgramItemsTypes.Struct.ToString(), "Version");

        ITcPlcDeclaration versionDecl = ExtractPlcDeclaration("Version^VERSION_INFO");
        string declarationString = versionDecl.DeclarationText;
        declarationString = VERSION_INFO_DEFAULT;
        log.LogDebug("Version file before inject (escaped): {declaration}", declarationString);

        Dictionary<string, string> injectData = new()
        {
            { "MAJOR", major.ToString() },
            { "MINOR", minor.ToString() },
            { "BUILD", build.ToString() },
            { "REVISION", revision.ToString() },
            { "COMMIT_HASH", $"\'{commitHash}\'" },
            { "COMMIT_TIME", $"\'{commitTime}\'" },
        };

        StringBuilder stringBuilder = DictInjectToXmlString(declarationString, injectData);
        if (injectData.Count != 0)
        {
            log.LogError("Leftover keys after injecting data");
            foreach (KeyValuePair<string, string> kvp in injectData)
            {
                log.LogDebug("Was unable to locate {key}", kvp.Key);
            }
            throw new AutomationInterfaceException("Was unable to update version file with one or more keys");
        }

        // Write the injected data to version POU
        Retry(() =>
        {
            versionDecl.DeclarationText = stringBuilder.ToString().TrimEnd('\r', '\n');
        }, actionName: "VersionInfoFileInject", maxRetries: 5, delayMilliseconds: 1000);
        log.LogDebug("Version file after inject:\n{declaration}", versionDecl.DeclarationText);
    }

    /// <summary>
    /// Saves the PLC project as a versioned library file. Sets the version from Git info,
    /// marks the library as released, and exports it to the specified output directory.
    /// </summary>
    /// <param name="outputDir">The directory to save the library file to.</param>
    /// <param name="version">The Git information used for versioning.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the library is already marked as released.</exception>
    public async Task SaveLibraryFile(string outputDir, string version)
    {
        PlcProjectXml projectXml = ExtractPlcProjectXml();
        if (projectXml!.GetTcLibReleased())
            throw new AutomationInterfaceException("Library is already in released setting! Unable to edit!");

        projectXml.SetTcLibVersion(version).SetTcLibReleased(true);

        // Save the changes to the XML
        log.LogInformation("Saving project information changes");
        Retry(() =>
        {
            plcIecProjectTreeItem!.ConsumeXml(projectXml.ToXmlString());
        }, actionName: "ProjectXmlConsume");

        await vsEnv.SaveAll();

        SaveAsLibrary(outputDir, version, projectXml);
    }

    /// <summary>
    /// Exports the PLC project as a <c>.library</c> file with a filename composed of company, project title, and version.
    /// </summary>
    /// <param name="outputDir">The output directory for the library file.</param>
    /// <param name="shortGitVersion">The Git information used to construct the filename.</param>
    /// <param name="projectXml">The PLC project XML containing company and title metadata.</param>
    private void SaveAsLibrary(string outputDir, string shortGitVersion, PlcProjectXml projectXml)
    {
        string filename = $"{projectXml.GetCompanyTitle()}_{projectXml.GetProjectTitle()}_{shortGitVersion}.library";
        filename = Regex.Replace(filename, @"\s+", "_");
        string fullDestination = Path.Join(outputDir, filename);
        log.LogInformation("Saving library {file} at {destination}", filename, Path.GetDirectoryName(fullDestination));
        plcIecProject!.SaveAsLibrary(fullDestination);
    }

    /// <summary>
    /// Injects values from a dictionary into a TwinCAT declaration string by matching variable names.
    /// Each matched key is removed from <paramref name="injectData"/> so callers can detect leftover (unmatched) entries.
    /// </summary>
    /// <param name="declarationString">The original declaration text (e.g. from a VERSION_INFO POU).</param>
    /// <param name="injectData">
    /// A mutable dictionary of variable name → value pairs to inject. Matched keys are removed after injection.
    /// </param>
    /// <returns>A <see cref="StringBuilder"/> containing the modified declaration text.</returns>
    /// <exception cref="AutomationInterfaceException">Thrown when the declaration string is null or empty.</exception>
    private StringBuilder DictInjectToXmlString(string declarationString, Dictionary<string, string> injectData)
    {
        if (string.IsNullOrEmpty(declarationString))
            throw new AutomationInterfaceException("Declaration string was null/empty");

        StringBuilder stringBuilder = new();
        string[] lines = declarationString.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (string line in lines)
        {
            // Skips the lines that does not contain any declarations
            int colonIndex = line.IndexOf(':');
            if (colonIndex == -1)
            {
                log.LogDebug("No declaration in line {line}. Skipping", line);
                stringBuilder.AppendLine(line);
                continue;
            }
            string key = line.Substring(0, colonIndex).Trim();

            // Declaration was found, but the injection data does not contain a matching declaration
            if (!injectData.TryGetValue(key, out string? value))
            {
                log.LogDebug("Declaration in line, but not found in dictionary: {key}", key);
                stringBuilder.AppendLine(line);
                continue;
            }

            // Inject the value from the dictionary into the line
            int assignIndex = line.IndexOf(":=", colonIndex);
            int semicolonIndex = line.IndexOf(';', assignIndex);
            string beforeValue = line.Substring(0, assignIndex);  // Variable : Datatype
            string afterValue = line.Substring(semicolonIndex + 1); // If line contains any comments
            string newLine = $"{beforeValue} := {value};{afterValue}"; // Variable : Datatype := Value; // Comments

            log.LogDebug("Injecting {value} for {key}", value, key);
            stringBuilder.AppendLine(newLine);
            
            // Remove key as it has been injected
            injectData.Remove(key);
        }

        return stringBuilder;
    }
}
