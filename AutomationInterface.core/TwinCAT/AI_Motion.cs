using Microsoft.Extensions.Logging;

namespace AutomationInterface.core;

/// <summary>
/// Partial class of Automation Interface containing Motion/NC axis management code.
/// </summary>
public partial class AutomationInterface
{
    /// <summary>
    /// Reload the Motion elements in the Solution Explorer. This method only works with the TwinCAT XAE IDE open (UiXae = true).
    /// </summary>
    /// <remarks>This method locates each Motion element within the Solution Explorer and reloads its
    /// project file. Use this method to refresh the state of NC axes after external changes or updates.</remarks>
    public async Task ReloadMotionElements()
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
