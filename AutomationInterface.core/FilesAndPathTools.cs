namespace AutomationInterface.core;

/// <summary>
/// Provides static helper methods for searching files and directories within a workspace.
/// </summary>
public class FilesAndPathTools
{
    /// <summary>
    /// Recursively searches for a file by name starting from the specified folder path.
    /// Skips directories that contain <c>.git</c> in their path.
    /// </summary>
    /// <param name="folderPath">The root directory to begin the search.</param>
    /// <param name="fileName">The name of the file to find (case-insensitive).</param>
    /// <returns>The full path to the file if found; otherwise <see langword="null"/>.</returns>
    static public string FindFile(string folderPath, string fileName)
    {
        try
        {
            // Check all files in the current folder
            foreach (var file in Directory.GetFiles(folderPath))
            {
                if (Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return file; // Return the full path of the file
                }
            }
            // Recursively check subdirectories
            foreach (var subfolder in Directory.GetDirectories(folderPath))
            {
                if (subfolder.Contains(".git"))
                    { continue; }
                string foundFile = FindFile(subfolder, fileName);
                if (!string.IsNullOrEmpty(foundFile))
                {
                    return foundFile; // Return the full path if found in subfolders
                }
            }
        }
        catch
        {
            throw;
        }
        return null!;
    }

    /// <summary>
    /// Represents a discovered TwinCAT solution with its associated file paths.
    /// </summary>
    /// <param name="Name">The solution name without extension.</param>
    /// <param name="SolutionFilePath">The full path to the <c>.sln</c> file.</param>
    /// <param name="ProjectPath">The full path to the <c>.tsproj</c> project file.</param>
    public record SolutionFiles(string Name, string SolutionFilePath, string ProjectPath);

    /// <summary>
    /// Finds all files with the specified extension under the given root path.
    /// </summary>
    /// <param name="rootPath">The root directory to search.</param>
    /// <param name="extension">The file extension to match (e.g. <c>.library</c>).</param>
    /// <returns>A list of full file paths matching the extension.</returns>
    static public List<string> FindFileExtenstions(string rootPath, string extension)
    {
        List<string> foundFiles = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(rootPath, $"*{extension}", SearchOption.AllDirectories))
            {
                foundFiles.Add(file);
            }
        }
        catch
        {
            throw;
        }
        return foundFiles;
    }

    /// <summary>
    /// Discovers all TwinCAT solution files (<c>.sln</c>) under the root path and pairs each with its <c>.tsproj</c> project file.
    /// </summary>
    /// <param name="rootPath">The root directory to search for solution files.</param>
    /// <returns>A list of <see cref="SolutionFiles"/> records containing the solution and project paths.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the parent directory of a solution file cannot be determined.</exception>
    /// <exception cref="FileNotFoundException">Thrown when no <c>.tsproj</c> file is found alongside a solution.</exception>
    static public List<SolutionFiles> FindSolutionFiles(string rootPath)
    {
        List<SolutionFiles> foundFiles = new List<SolutionFiles>();
        try
        {
            foreach (var solution in Directory.GetFiles(rootPath, $"*.sln", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(solution);
             
                string solutionRoot = Path.GetDirectoryName(solution)!;
                if (solutionRoot is null)
                    throw new DirectoryNotFoundException($"Directory not found for solution {name} in path {solution}");

                string[] projectPath = Directory.GetFiles(solutionRoot, "*.tsproj", SearchOption.AllDirectories);
                if (projectPath.Length > 0)
                {
                    foundFiles.Add(new SolutionFiles(name, solution, projectPath[0]));
                    continue;
                }
                else
                    throw new FileNotFoundException($"No project file found for solution {name} in path {solution}");
            }
        }
        catch
        {
            throw;
        }
        return foundFiles;
    }
}
