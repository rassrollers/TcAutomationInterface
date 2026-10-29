namespace AutomationInterface.core;

/// <summary>
/// A TcUnit test suite, parsed from the <c>Test suite ID=...</c> and <c>ID=... number of tests=...</c> lines.
/// </summary>
public sealed class UnitTestSuiteResult
{
    /// <summary>The TcUnit suite ID (-1 when test cases were found before any suite line).</summary>
    public int Id { get; init; }

    /// <summary>The suite name, e.g. <c>PRG_TEST.fb_RingBuffer</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Number of tests in the suite as reported by TcUnit, if available.</summary>
    public int? NumberOfTests { get; internal set; }

    /// <summary>Number of failed tests in the suite as reported by TcUnit, if available.</summary>
    public int? FailedTests { get; internal set; }

    /// <summary>Suite duration as reported by TcUnit (seconds), if available.</summary>
    public string? Duration { get; internal set; }

    /// <summary>The test cases of the suite in the order TcUnit reported them.</summary>
    public List<UnitTestCaseResult> TestCases { get; init; } = new();
}
