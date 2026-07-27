using TCatSysManagerLib;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing all program items (POU, DUT, GVL, etc.) related code.
/// </summary>
public partial class AutomationInterface
{
    /// <summary>
    /// Creates a program item (POU, GVL, etc.) in the PLC project based on the provided parameters. 
    /// The item is created under the specified path or at the root of the PLC project if no path is provided.
    /// </summary>
    /// <param name="itemName">The name of the program item to create.</param>
    /// <param name="itemType">The type of the program item (e.g., POU, DUT, ITF, GVL, Folders). Use the <see cref="ProgramItemsTypes"/> enum for valid values.</param>
    /// <param name="itemPath">Optional: The path where the program item should be created.</param>
    /// <param name="returnType">Optional: The return type of function type.</param>
    /// <exception cref="AutomationInterfaceException"></exception>
    internal void CreateProgramItem(string itemName, string itemType, string itemPath = "", string returnType = "")
    {
        if (plcIecProject is null || plcIecProjectTreeItem is null)
            throw new AutomationInterfaceException("PLC items tree item reference was not set");

        Enum.TryParse(itemType, ignoreCase: true, out ProgramItemsTypes type);
        var vInfo = GenerateVInfoOptions(type, returnType);

        ITcSmTreeItem programItem;
        if (!string.IsNullOrEmpty(itemPath))
            programItem = plcIecProjectTreeItem.LookupChild(itemPath);
        else
            programItem = plcIecProjectTreeItem;

        if (vInfo.Count > 0)
            programItem.CreateChild(itemName, (int)type, "", vInfo.ToArray());
        else
            programItem.CreateChild(itemName, (int)type, "", null); // Parse null if no optionals
    }

    /// <summary>
    /// Generate the options for the VInfo parameter of the CreateChild method based on the item type and return type (if function).
    /// </summary>
    /// <param name="itemType"></param>
    /// <param name="returnType"></param>
    /// <returns></returns>
    private List<object> GenerateVInfoOptions(ProgramItemsTypes itemType, string returnType)
    {
        var vInfo = new List<object>();
        switch (itemType)
        {
            case ProgramItemsTypes.Program:
            case ProgramItemsTypes.FunctionBlock:
                vInfo.Add(IECLANGUAGETYPES.IECLANGUAGE_ST);
                break;
            case ProgramItemsTypes.Function:
                vInfo.Add(IECLANGUAGETYPES.IECLANGUAGE_ST);
                vInfo.Add(returnType.ToUpper());
                break;
        }

        return vInfo;
    }
}