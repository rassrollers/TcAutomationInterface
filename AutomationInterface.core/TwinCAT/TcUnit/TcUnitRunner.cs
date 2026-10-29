using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace AutomationInterface.core;

/// <summary>
/// Monitors and reports TcUnit test execution results by polling the Visual Studio Error List.
/// </summary>
[SupportedOSPlatform("windows")]
class TcUnitRunner
{
    private readonly ILogger log;
    private readonly VisualStudioEnvironment vsEnv;

    private const string TCUNIT_FINISHED = "TESTS FINISHED RUNNING";
    private const string TCUNIT_FAILED_TEST = "FAILED TEST";

    // The ADS logger prefixes every line in the Error List, e.g.
    // "19-10-2026 10:42:06 639 ms | 'PlcTask' (350): | Test suite ID=0 'PRG_TEST.fb_RingBuffer'"
    private static readonly Regex AdsPrefixRegex = new(
        @"^(?<ts>.+?\d+\s*ms)\s*\|\s*'(?<src>[^']*)'(?:\s*\((?<port>\d+)\))?\s*:\s*(?<msg>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex TimestampRegex = new(
        @"^(?<date>\S+)\s+(?<time>\d{1,2}:\d{2}:\d{2})\s+(?<ms>\d{1,3})\s*ms$",
        RegexOptions.CultureInvariant);
    private static readonly string[] TimestampFormats =
    {
        "dd-MM-yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss",
        "MM/dd/yyyy HH:mm:ss", "MM-dd-yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss",
        "d-M-yyyy HH:mm:ss", "d.M.yyyy HH:mm:ss", "d/M/yyyy HH:mm:ss", "M/d/yyyy HH:mm:ss"
    };

    // TcUnit per suite / per test lines (after the leading "| " has been removed)
    private static readonly Regex SuiteRegex = new(@"^Test suite ID=(?<id>\d+) '(?<name>.*)'$", RegexOptions.CultureInvariant);
    private static readonly Regex SuiteSummaryRegex = new(@"^ID=(?<id>\d+) number of tests=(?<tests>\d+), number of failed tests=(?<failed>\d+), duration=(?<duration>\S+)$", RegexOptions.CultureInvariant);
    private static readonly Regex TestNameRegex = new(@"^Test name=(?<name>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex TestClassRegex = new(@"^Test class name=(?<name>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex TestStatusRegex = new(@"^Test status=(?<status>\w+), number of asserts=(?<asserts>\d+), duration=(?<duration>\S+)$", RegexOptions.CultureInvariant);

    // TcUnit summary lines, e.g. "Test suites: 3", "Tests: 12", "Successful tests: 11", "Failed tests: 1", "Duration: 0.12"
    // Case-sensitive on purpose: "Tests:" must not match "Successful tests:" / "Failed tests:".
    private static readonly Regex TestSuitesRegex = new(@"^Test suites:\s*(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex TestsRegex = new(@"^Tests:\s*(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex SuccessfulRegex = new(@"^Successful tests:\s*(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FailedRegex = new(@"^Failed tests:\s*(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DurationRegex = new(@"^Duration:\s*([\d.,]+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Initializes a new instance of the <see cref="TcUnitRunner"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="vsEnv">The Visual Studio environment used to access the Error List.</param>
    public TcUnitRunner(ILogger logger, VisualStudioEnvironment vsEnv)
    {
        log = logger;
        this.vsEnv = vsEnv;
    }

    /// <summary>
    /// Checks whether TcUnit has finished executing all tests by looking for the
    /// <c>TESTS FINISHED RUNNING</c> message in the Error List (any severity).
    /// </summary>
    /// <returns><see langword="true"/> if TcUnit reports that tests have finished; otherwise <see langword="false"/>.</returns>
    public async Task<bool> IsTcUnitDone()
    {
        var entries = await vsEnv.GetErrorListEntries();
        return entries.Any(e => e.Description.Contains(TCUNIT_FINISHED, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the Error List and parses the TcUnit results (suites, test cases, summary numbers and failed tests).
    /// </summary>
    public async Task<UnitTestResult> GetResults()
    {
        var entries = await vsEnv.GetErrorListEntries();
        return Parse(entries);
    }

    /// <summary>
    /// Parses TcUnit output from an Error List snapshot.
    /// TcUnit (through the ADS logger) reports every line twice in the Error List; only the first copy is used.
    /// </summary>
    /// <param name="entries">The Error List snapshot.</param>
    internal static UnitTestResult Parse(IReadOnlyList<ErrorListEntry> entries)
    {
        var lines = entries
            .DistinctBy(e => e.Description, StringComparer.Ordinal)
            .Select(TcUnitLine.TryCreate)
            .OfType<TcUnitLine>()
            .OrderBy(l => l.Timestamp ?? DateTime.MaxValue)
            .ThenBy(l => l.Entry.Description, StringComparer.Ordinal)
            .ThenBy(l => l.Entry.Index)
            .ToList();

        var suites = new List<UnitTestSuiteResult>();
        var failures = new List<string>();
        UnitTestSuiteResult? suite = null;
        string? testName = null;
        string? testClass = null;

        foreach (var line in lines)
        {
            string text = line.Text;
            Match m;

            if ((m = SuiteRegex.Match(text)).Success)
            {
                suite = new UnitTestSuiteResult { Id = int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture), Name = m.Groups["name"].Value };
                suites.Add(suite);
                testName = testClass = null;
            }
            else if ((m = SuiteSummaryRegex.Match(text)).Success)
            {
                int id = int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture);
                if (suite is null || suite.Id != id)
                {
                    suite = new UnitTestSuiteResult { Id = id };
                    suites.Add(suite);
                }
                suite.NumberOfTests = int.Parse(m.Groups["tests"].Value, CultureInfo.InvariantCulture);
                suite.FailedTests = int.Parse(m.Groups["failed"].Value, CultureInfo.InvariantCulture);
                suite.Duration = m.Groups["duration"].Value;
            }
            else if ((m = TestNameRegex.Match(text)).Success)
            {
                testName = m.Groups["name"].Value;
            }
            else if ((m = TestClassRegex.Match(text)).Success)
            {
                testClass = m.Groups["name"].Value;
            }
            else if ((m = TestStatusRegex.Match(text)).Success)
            {
                if (suite is null)
                {
                    suite = new UnitTestSuiteResult { Id = -1 };
                    suites.Add(suite);
                }
                suite.TestCases.Add(new UnitTestCaseResult
                {
                    Suite = testClass ?? suite.Name,
                    Name = testName ?? string.Empty,
                    Status = m.Groups["status"].Value,
                    NumberOfAsserts = int.Parse(m.Groups["asserts"].Value, CultureInfo.InvariantCulture),
                    Duration = m.Groups["duration"].Value
                });
                testName = testClass = null;
            }
            else if (text.Contains(TCUNIT_FAILED_TEST, StringComparison.Ordinal))
            {
                failures.Add(text);
            }
        }

        return new UnitTestResult
        {
            Finished = lines.Any(l => l.Text.Contains(TCUNIT_FINISHED, StringComparison.OrdinalIgnoreCase)),
            TestSuites = MatchInt(lines, TestSuitesRegex),
            Tests = MatchInt(lines, TestsRegex),
            SuccessfulTests = MatchInt(lines, SuccessfulRegex),
            FailedTests = MatchInt(lines, FailedRegex),
            Duration = MatchString(lines, DurationRegex),
            Suites = suites,
            Failures = failures,
            Messages = lines.Select(l => l.Entry).ToList()
        };
    }

    /// <summary>
    /// Reads the Error List and logs the TcUnit results.
    /// </summary>
    public async Task PrintResultsFromUnitTest()
    {
        PrintResults(await GetResults());
    }

    /// <summary>
    /// Logs the TcUnit results through a <see cref="UnitTestResultReporter"/>.
    /// </summary>
    /// <param name="result">The parsed TcUnit results.</param>
    public void PrintResults(UnitTestResult result)
    {
        new UnitTestResultReporter(log).Report(result);
    }

    private static int? MatchInt(IEnumerable<TcUnitLine> lines, Regex regex)
    {
        var value = MatchString(lines, regex);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
    }

    private static string? MatchString(IEnumerable<TcUnitLine> lines, Regex regex)
    {
        foreach (var line in lines)
        {
            var m = regex.Match(line.Text);
            if (m.Success)
                return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>
    /// A TcUnit line from the Error List with the ADS logger prefix removed.
    /// </summary>
    private sealed record TcUnitLine(ErrorListEntry Entry, DateTime? Timestamp, string Source, string Text)
    {
        /// <summary>
        /// Splits the Error List description into the ADS logger prefix and the message, and returns
        /// the line when it comes from TcUnit. TcUnit lines start with "|" (after the prefix) or contain
        /// "FAILED TEST" / "TcUnit". Returns <see langword="null"/> for other Error List items.
        /// </summary>
        public static TcUnitLine? TryCreate(ErrorListEntry entry)
        {
            DateTime? timestamp = null;
            string source = string.Empty;
            string message = entry.Description.Trim();

            var m = AdsPrefixRegex.Match(message);
            if (m.Success)
            {
                timestamp = ParseTimestamp(m.Groups["ts"].Value);
                source = m.Groups["src"].Value;
                message = m.Groups["msg"].Value.Trim();
            }

            bool isTcUnit = message.StartsWith('|')
                || message.Contains(TCUNIT_FAILED_TEST, StringComparison.Ordinal)
                || message.Contains("TcUnit", StringComparison.OrdinalIgnoreCase);
            if (!isTcUnit)
                return null;

            string text = message.TrimStart('|').Trim();
            return new TcUnitLine(entry, timestamp, source, text);
        }

        private static DateTime? ParseTimestamp(string value)
        {
            var m = TimestampRegex.Match(value.Trim());
            if (!m.Success)
                return null;

            string dateTime = $"{m.Groups["date"].Value} {m.Groups["time"].Value}";
            if (DateTime.TryParseExact(dateTime, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result)
                || DateTime.TryParse(dateTime, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out result))
            {
                return result.AddMilliseconds(int.Parse(m.Groups["ms"].Value, CultureInfo.InvariantCulture));
            }
            return null;
        }
    }
}
