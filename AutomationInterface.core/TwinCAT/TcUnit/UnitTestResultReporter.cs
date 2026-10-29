using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AutomationInterface.core;

/// <summary>
/// Logs a <see cref="UnitTestResult"/> in a readable form, regardless of whether it was parsed
/// from the Error List or from the TcUnit result file.
/// </summary>
public sealed class UnitTestResultReporter
{
    private readonly ILogger log;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitTestResultReporter"/> class.
    /// </summary>
    /// <param name="logger">The logger to write the report to.</param>
    public UnitTestResultReporter(ILogger logger)
    {
        log = logger;
    }

    /// <summary>
    /// Logs one line per suite, one line per test case, the failed asserts and the summary.
    /// Failed test cases, failed asserts and a failed summary are logged as errors.
    /// </summary>
    /// <param name="result">The parsed TcUnit results.</param>
    public void Report(UnitTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var suite in result.Suites)
        {
            log.LogInformation("Test suite {id} '{name}': {tests} test(s), {failed} failed, duration {duration} s",
                suite.Id, suite.Name, Format(suite.NumberOfTests), Format(suite.FailedTests), suite.Duration ?? "?");

            foreach (var test in suite.TestCases)
            {
                if (test.Failed)
                    log.LogError("  {status} {name} ({asserts} assert(s), {duration} s)", test.Status, test.Name, Format(test.NumberOfAsserts), test.Duration ?? "?");
                else
                    log.LogInformation("  {status} {name} ({asserts} assert(s), {duration} s)", test.Status, test.Name, Format(test.NumberOfAsserts), test.Duration ?? "?");
            }
        }

        foreach (var failure in result.Failures)
            log.LogError("{failure}", failure);

        if (!result.Finished)
            log.LogWarning("TcUnit has not reported that the tests finished running");

        if (result.Passed)
            log.LogInformation("Test suites: {suites}, tests: {tests}, successful: {successful}, failed: {failed}, duration: {duration} s",
                Format(result.TestSuites), Format(result.Tests), Format(result.SuccessfulTests), Format(result.FailedTests), result.Duration ?? "?");
        else
            log.LogError("Test suites: {suites}, tests: {tests}, successful: {successful}, failed: {failed}, duration: {duration} s",
                Format(result.TestSuites), Format(result.Tests), Format(result.SuccessfulTests), Format(result.FailedTests), result.Duration ?? "?");
    }

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
