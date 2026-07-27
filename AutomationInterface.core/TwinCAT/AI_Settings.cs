namespace AutomationInterface.core;

/// <summary>
/// Contains constant strings for TwinCAT Automation Interface DTE object names.
/// </summary>
public class AutomationInterfaceSettings
{
    /// <summary>
    /// DTE object name for TwinCAT Automation Settings (silent mode, etc.).
    /// <see href="https://infosys.beckhoff.com/content/1033/tc3_automationinterface/2489025803.html"/>
    /// </summary>
    public const string TC_AUTOMATION_SETTINGS = "TcAutomationSettings";

    /// <summary>
    /// DTE object name for TwinCAT Remote Manager (TC version selection).
    /// <see href="https://infosys.beckhoff.com/content/1033/tc3_automationinterface/2507904267.html"/>
    /// </summary>
    public const string TC_REMOTE_MANAGER = "TcRemoteManager";
}

/// <summary>
/// Specifies the type of build operation to perform.
/// </summary>
public enum BuildType
{
    /// <summary>Build and save as a TwinCAT library file.</summary>
    Library,
    /// <summary>Build as a deployable TwinCAT project.</summary>
    Project
}

/// <summary>
/// Contains constant path identifiers for looking up items in the TwinCAT system manager tree.
/// <see href="https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242772107.html"/>
/// </summary>
public class TreeItems
{
    /// <summary>I/O configuration tree item path.</summary>
    public const string IO_CONFIG = "TIIC";
    /// <summary>I/O configuration → I/O Devices tree item path.</summary>
    public const string IO_CONFIG_IO_DEVICES = "TIID";
    /// <summary>Real-Time configuration tree item path.</summary>
    public const string RT_CONFIG = "TIRC";
    /// <summary>Real-Time configuration → License tree item path.</summary>
    public const string RT_CONFIF_LICENSE = "TIRC^License";
    /// <summary>Real-Time configuration → Route Settings tree item path.</summary>
    public const string RT_CONFIG_ROUTE_SETTINGS = "TIRR";
    /// <summary>Real-Time configuration → Additional Tasks tree item path.</summary>
    public const string RT_CONFIG_ADDITIONAL_TASKS = "TIRT";
    /// <summary>Real-Time configuration → Real-Time Settings tree item path.</summary>
    public const string RT_CONFIG_RT_SETTINGS = "TIRS";
    /// <summary>PLC configuration tree item path.</summary>
    public const string PLC_CONFIG = "TIPC";
    /// <summary>NC configuration tree item path.</summary>
    public const string NC_CONFIG = "TINC";
    /// <summary>CNC configuration tree item path.</summary>
    public const string CNC_CONFIG = "TICC";
    /// <summary>CAM configuration tree item path.</summary>
    public const string CAM_CONFIG = "TIAC";
}

public enum ProgramItemsTypes : int
{
    Folder = 601,
    // POU
    Program = 602,
    Function = 603,
    FunctionBlock = 604,
    // DUT
    Enum = 605,
    Struct = 606,
    Union = 607,
    //Alias = 623,  // not working at the moment.
    // GVL
    GVL = 615,
    ParameterList = 629,
    // Interface
    Interface = 618,
    // VISU
    Visualization = 619
}
