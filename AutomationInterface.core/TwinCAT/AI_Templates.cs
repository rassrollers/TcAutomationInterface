using Microsoft.Win32;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all project template related code.
/// </summary>
public partial class AutomationInterface
{
    internal void CreateProjectFromTemplate(string templateName, string projectName)
    {
        if (plcConfig is null)
            throw new AutomationInterfaceException("PLC configuration tree item reference was not set");

        Retry(() =>
        {
            plcConfig.CreateChild(projectName, 0, "", templateName);
        }, "Creating project from template");
    }

    #region Templates helper
    /// <summary>
    /// Gets whether the operating system is 64-bit, used to determine the correct registry path for TwinCAT.
    /// </summary>
    private static bool Is64BitOperatingSystem
    {
        get { return Environment.Is64BitOperatingSystem; }
    }

    /// <summary>
    /// Gets the currently installed TwinCAT 3 version from the Windows registry
    /// (<c>HKLM\Software\[Wow6432Node\]Beckhoff\TwinCAT3\CurrentVersion</c>).
    /// </summary>
    /// <exception cref="ApplicationException">Thrown when the registry key or version value cannot be read.</exception>
    private static Version CurrentTwinCATVersion
    {
        get
        {
            string path = Is64BitOperatingSystem
                ? "Software\\Wow6432Node\\Beckhoff\\TwinCAT3"
                : "Software\\Beckhoff\\TwinCAT3";

            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
            {
                if (key is null)
                    throw new ApplicationException($"Could not open registry key: HKLM\\{path}");

                object? value = key.GetValue("CurrentVersion");
                if (value is null)
                    throw new ApplicationException("Could not determine actual TwinCAT Version!");

                string ret = value.ToString()!;
                return new Version(ret);
            }
        }
    }

    /// <summary>
    /// Gets the TwinCAT 3 installation directory from the Windows registry
    /// (<c>HKLM\Software\[Wow6432Node\]Beckhoff\TwinCAT3\{Major.Minor}\InstallDir</c>).
    /// </summary>
    /// <exception cref="ApplicationException">Thrown when the registry key or install directory value cannot be read.</exception>
    private static string TwinCATInstallDir
    {
        get
        {
            Version tcVer = CurrentTwinCATVersion;

            string path = Is64BitOperatingSystem
                ? "Software\\Wow6432Node\\Beckhoff\\TwinCAT3"
                : "Software\\Beckhoff\\TwinCAT3";

            string subKeyPath = string.Format("{0}\\{1}", path, tcVer.ToString(2));

            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKeyPath))
            {
                if (key is null)
                    throw new ApplicationException($"Could not open registry key: HKLM\\{subKeyPath}");

                object? value = key.GetValue("InstallDir");
                if (value is null)
                    throw new ApplicationException($"Could not determine TwinCAT InstallDir. Registry value 'InstallDir' not found in HKLM\\{subKeyPath}");

                string installDir = value.ToString()!;
                if (string.IsNullOrWhiteSpace(installDir))
                    throw new ApplicationException($"TwinCAT InstallDir is empty or invalid in registry key: HKLM\\{subKeyPath}");

                return installDir;
            }
        }
    }

    private static readonly string PlcProjectTemplateName = "TwinCAT PLC Project.tspproj";
    private static readonly string XaeProjectTemplateName = "TwinCAT Project.tsproj";
    private static readonly string EmptyPlcTemplateName = "Empty PLC Template";
    private static readonly string StandardPlcTemplateName = "Standard PLC Template";

    /// <summary>
    /// Gets the full path to the XAE project (.tsproj) template used in the TwinCAT XAE solution.
    /// </summary>
    internal static string XaeProjectTemplatePath
    {
        get => Path.Combine(TwinCATInstallDir, @"Components\Base\PrjTemplate", XaeProjectTemplateName);
    }

    /// <summary>
    /// Gets the full path to the PLC project (.tspproj) template used in the TwinCAT XAE solution.
    /// </summary>
    internal static string PlcProjectTemplate
    {
        get => Path.Combine(TwinCATInstallDir, @"Components\Base\PlcTemplate", PlcProjectTemplateName);
    }

    /// <summary>
    /// Gets the full path to the Empty PLC project (.plcproj) template used in the TwinCAT XAE projects.
    /// </summary>
    internal static string EmptyPlcProjectTemplate
    {
        get => EmptyPlcTemplateName;
    }

    /// <summary>
    /// Gets the full path to the Standard PLC project (.plcproj) template used in the TwinCAT XAE projects.
    /// </summary>
    internal static string StandardPlcProjectTemplate
    {
        get => StandardPlcTemplateName;
    }
    #endregion
}
