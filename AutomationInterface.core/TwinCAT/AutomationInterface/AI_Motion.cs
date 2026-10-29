using Microsoft.Extensions.Logging;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing MOTION management code.
/// </summary>
public partial class AutomationInterface
{
    /// <summary>
    /// Adds a motion task to the current XAE project's NC configuration.
    /// </summary>
    /// <param name="taskName">The name of the motion task.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the NC configuration reference is unavailable or the project is not an XAE project.</exception>
    internal void AddMotionTask(string taskName = "NC-Task")
    {
        if (ncConfig is null)
            throw new AutomationInterfaceException("Motion reference was not set");
        else if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("Adding Motion tasks is only supported for XAE projects");

        log.LogDebug("Adding a new Motion task named: {name}", taskName);
        ncConfig!.CreateChild(taskName, 1, null, null);
    }

    /// <summary>
    /// Adds a motion axis to the first motion task in the current XAE project.
    /// </summary>
    /// <param name="axisName">The name of the motion axis.</param>
    /// <exception cref="AutomationInterfaceException">Thrown when the system or NC configuration reference is unavailable or the project is not an XAE project.</exception>
    internal void AddMotionAxis(string axisName = "Axis 1")
    {
        if (sysManager is null || ncConfig is null)
            throw new AutomationInterfaceException("Motion reference was not set");
        else if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException("Adding Motion axes is only supported for XAE projects");

        var ncName = ncConfig.Child[1].Name;
        var axes = sysManager.LookupTreeItem($"{TreeItems.NC_CONFIG}^{ncName}^Axes");
        axes.CreateChild(axisName, 1, null, null);
    }

    /// <summary>
    /// Reload the Motion elements in the Solution Explorer. This method only works with the TwinCAT XAE IDE open (UiXae = true).
    /// </summary>
    /// <remarks>This method locates each Motion element within the Solution Explorer and reloads its
    /// project file. Use this method to refresh the state of NC axes after external changes or updates.</remarks>
    internal async Task ReloadMotionElements()
    {
        if (projectType != TcProjectExtension.tsproj)
            throw new AutomationInterfaceException($"Motion functions are only available for XAE project types. Current type: {projectType.ToString()}");
        
        var solutionFile = await vsEnv.GetSolutionName();
        string solutionName = Path.GetFileNameWithoutExtension(solutionFile);
        string[] pathSegments = {
            solutionName,
            solutionName,
            "MOTION"
        };
        
        var ncPath = await vsEnv.ExpandSolutionExplorerPath(pathSegments);
        var ncChildren = await vsEnv.GetChildrenOfSolutionPath(ncPath);

        if (ncChildren == null || ncChildren.Count == 0)
            throw new TwinCatException($"No Motion tasks found in the Solution Explorer at path: {ncPath}");

        var ncAxesPath = new string[] { solutionName, solutionName, "MOTION", ncChildren[0], "Axes" };
        var ncAxes = await vsEnv.ExpandSolutionExplorerPath(ncAxesPath);
        var ncAxesChildren = await vsEnv.GetChildrenOfSolutionPath(ncAxes);

        foreach (string child in ncAxesChildren)
        {
            log.LogInformation("Reloading Motion element: {ncAxis}", child);
            await vsEnv.SelectSolutionExplorerItem(Path.Combine(ncAxes, child));
            await vsEnv.ExecuteDteCommand("OtherContextMenus.Projects.ReloadProjectFile");
        }
    }
}
