namespace AutomationInterface.core;

/// <summary>
/// Result of a TcUnit test run, parsed from the Error List.
/// Summary numbers are <see langword="null"/> when the corresponding TcUnit line was not found.
/// </summary>
public sealed class UnitTestResult
{
    /// <summary><see langword="true"/> when TcUnit reported <c>TESTS FINISHED RUNNING</c>.</summary>
    public bool Finished { get; init; }

    /// <summary>Number of test suites.</summary>
    public int? TestSuites { get; init; }

    /// <summary>Number of tests.</summary>
    public int? Tests { get; init; }

    /// <summary>Number of successful tests.</summary>
    public int? SuccessfulTests { get; init; }

    /// <summary>Number of failed tests.</summary>
    public int? FailedTests { get; init; }

    /// <summary>Test duration as reported by TcUnit (seconds), if available.</summary>
    public string? Duration { get; init; }

    /// <summary>
    /// <see langword="true"/> when finished, the summary reports no failed tests, no <c>FAILED TEST</c> lines
    /// were found and no test case has a failed status.
    /// </summary>
    public bool Passed => Finished && (FailedTests ?? 0) == 0 && Failures.Count == 0 && !TestCases.Any(t => t.Failed);

    /// <summary>The test suites in the order TcUnit reported them, each with its test cases.</summary>
    public List<UnitTestSuiteResult> Suites { get; init; } = new();

    /// <summary>All test cases of all suites, in the order TcUnit reported them.</summary>
    public IEnumerable<UnitTestCaseResult> TestCases => Suites.SelectMany(s => s.TestCases);

    /// <summary>The <c>FAILED TEST ...</c> lines (failed asserts) reported by TcUnit, without the ADS logger prefix.</summary>
    public List<string> Failures { get; init; } = new();

    /// <summary>
    /// All TcUnit related Error List entries with duplicates removed, in the order TcUnit reported them.
    /// The descriptions still carry the ADS logger prefix (timestamp and task).
    /// </summary>
    public List<ErrorListEntry> Messages { get; init; } = new();
}
