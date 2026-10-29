using System.Globalization;
using System.Xml.Linq;

namespace AutomationInterface.core;

/// <summary>
/// Parses the xUnit result file TcUnit writes on the target (<c>GVL_PARAM_TCUNIT.XUNITFILEPATH</c>)
/// into a <see cref="UnitTestResult"/>. The file is JUnit compatible, so the same file can be handed
/// to the Jenkins <c>junit</c> step unchanged; this parser is for logging and for deciding pass/fail.
/// </summary>
/// <remarks>
/// Layout written by TcUnit:
/// <code>
/// &lt;testsuites disabled="" failures="0" tests="34" time="2.58"&gt;
///   &lt;testsuite id="0" name="PRG_TEST.fb_RingBuffer" tests="18" failures="0" time="0.018"&gt;
///     &lt;testcase name="Write3k..." classname="PRG_TEST.fb_RingBuffer" time="1.2e-4" status="PASS"&gt;&lt;/testcase&gt;
///     &lt;testcase name="..." classname="..." status="FAIL"&gt;&lt;failure message="EXP: ..., ACT: ..., MSG: ..." type="AssertionError" /&gt;&lt;/testcase&gt;
///   &lt;/testsuite&gt;
/// &lt;/testsuites&gt;
/// </code>
/// </remarks>
public sealed class TcUnitXmlResultParser
{
    private const string FailedTestPrefix = "FAILED TEST";

    /// <summary>
    /// Parses a TcUnit result file.
    /// </summary>
    /// <param name="path">The local path of the downloaded file.</param>
    /// <exception cref="TcXmlException">Thrown when the file is not a TcUnit result file.</exception>
    public UnitTestResult ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return Parse(XDocument.Load(path));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new TcXmlException($"'{path}' is not a valid XML file: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses TcUnit result XML text.
    /// </summary>
    /// <param name="xml">The file content.</param>
    /// <exception cref="TcXmlException">Thrown when the content is not a TcUnit result document.</exception>
    public UnitTestResult Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        try
        {
            return Parse(XDocument.Parse(xml));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new TcXmlException($"The TcUnit result is not valid XML: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a TcUnit result document.
    /// </summary>
    /// <param name="document">The loaded document.</param>
    /// <exception cref="TcXmlException">Thrown when the root element is not <c>testsuites</c>.</exception>
    public UnitTestResult Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root ?? throw new TcXmlException("The TcUnit result document is empty");
        if (root.Name.LocalName != "testsuites")
            throw new TcXmlException($"Expected a 'testsuites' root element in the TcUnit result but found '{root.Name.LocalName}'");

        var suites = root.Elements("testsuite").Select(ParseSuite).ToList();
        var failures = suites
            .SelectMany(s => s.TestCases)
            .SelectMany(t => t.Failures)
            .ToList();

        int? tests = IntAttribute(root, "tests");
        int? failed = IntAttribute(root, "failures") ?? suites.Sum(s => s.FailedTests);
        int? disabled = IntAttribute(root, "disabled");

        return new UnitTestResult
        {
            // TcUnit writes the file after the last suite has finished, so a readable file means a finished run
            Finished = true,
            TestSuites = suites.Count,
            Tests = tests,
            SuccessfulTests = tests is int total && failed is int f ? total - f - (disabled ?? 0) : null,
            FailedTests = failed,
            Duration = root.Attribute("time")?.Value,
            Suites = suites.Select(s => s.Suite).ToList(),
            Failures = failures
        };
    }

    private static (UnitTestSuiteResult Suite, int? FailedTests, List<ParsedTestCase> TestCases) ParseSuite(XElement element)
    {
        string name = element.Attribute("name")?.Value ?? string.Empty;
        var testCases = element.Elements("testcase").Select(tc => ParseTestCase(tc, name)).ToList();

        var suite = new UnitTestSuiteResult
        {
            Id = IntAttribute(element, "id") ?? -1,
            Name = name,
            NumberOfTests = IntAttribute(element, "tests") ?? testCases.Count,
            FailedTests = IntAttribute(element, "failures") ?? testCases.Count(t => t.TestCase.Failed),
            Duration = element.Attribute("time")?.Value,
            TestCases = testCases.Select(t => t.TestCase).ToList()
        };

        return (suite, suite.FailedTests, testCases);
    }

    private static ParsedTestCase ParseTestCase(XElement element, string suiteName)
    {
        string name = element.Attribute("name")?.Value ?? string.Empty;
        string className = element.Attribute("classname")?.Value ?? suiteName;
        var failureElements = element.Elements("failure").ToList();

        var testCase = new UnitTestCaseResult
        {
            Suite = className,
            Name = name,
            Status = element.Attribute("status")?.Value ?? (failureElements.Count > 0 ? "FAIL" : "PASS"),
            NumberOfAsserts = IntAttribute(element, "assertions"),
            Duration = element.Attribute("time")?.Value
        };

        // Same shape as the Error List line, so both sources read alike in the log
        var failures = failureElements
            .Select(f => $"{FailedTestPrefix} '{className}@{name}', {f.Attribute("message")?.Value ?? f.Value}")
            .ToList();

        return new ParsedTestCase(testCase, failures);
    }

    private static int? IntAttribute(XElement element, string name)
    {
        return int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private sealed record ParsedTestCase(UnitTestCaseResult TestCase, List<string> Failures);
}
