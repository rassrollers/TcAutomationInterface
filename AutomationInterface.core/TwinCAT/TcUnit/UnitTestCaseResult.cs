namespace AutomationInterface.core;

/// <summary>
/// A single TcUnit test case, parsed from the <c>Test name=...</c>, <c>Test class name=...</c> and <c>Test status=...</c> lines.
/// </summary>
public sealed class UnitTestCaseResult
{
    /// <summary>The test class (suite) name, e.g. <c>PRG_TEST.fb_RingBuffer</c>.</summary>
    public string Suite { get; init; } = string.Empty;

    /// <summary>The test name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The status as reported by TcUnit, e.g. <c>PASS</c> or <c>FAIL</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Number of asserts in the test.</summary>
    public int? NumberOfAsserts { get; init; }

    /// <summary>Test duration as reported by TcUnit (seconds), if available.</summary>
    public string? Duration { get; init; }

    /// <summary><see langword="true"/> when the status is <c>PASS</c>.</summary>
    public bool Passed => Status.Equals("PASS", StringComparison.OrdinalIgnoreCase);

    /// <summary><see langword="true"/> when the status is <c>FAIL</c>.</summary>
    public bool Failed => Status.Equals("FAIL", StringComparison.OrdinalIgnoreCase);
}
