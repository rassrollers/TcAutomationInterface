namespace AutomationInterface.core;

/// <summary>
/// Result of a clean rebuild of the solution.
/// </summary>
public sealed class BuildResult
{
    /// <summary><see langword="true"/> when the build finished without errors in the Error List.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Wall clock duration of clean + build.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The Error List after the build.</summary>
    public ErrorListResult ErrorList { get; init; } = new();
}
