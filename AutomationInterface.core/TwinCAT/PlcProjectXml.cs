using System.Xml.Linq;

namespace AutomationInterface.core;

/// <summary>
/// This class if for handling the PLC project file (.plcproj)
/// </summary>
internal class PlcProjectXml
{
    private XDocument? doc = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlcProjectXml"/> class by parsing the provided XML string.
    /// </summary>
    /// <param name="xmlString">The XML string content of the PLC project.</param>
    public PlcProjectXml(string xmlString)
    {
        Reset(xmlString);
    }

    /// <summary>
    /// Replaces the in-memory XML document by parsing the provided string.
    /// </summary>
    /// <param name="xmlString">The new XML string to parse.</param>
    public void Reset(string xmlString)
    {
        doc = XDocument.Parse(xmlString);
    }

    /// <summary>
    /// Retrieves the <c>&lt;ProjectInfo&gt;</c> element from the XML document.
    /// </summary>
    /// <returns>The <see cref="XElement"/> representing the project info node.</returns>
    /// <exception cref="TcXmlException">Thrown when the document is undefined or the element is not found.</exception>
    private XElement GetProjectInfoNode()
    {
        if (doc is null)
            throw new TcXmlException("GetProjectInfoNode() - Document undefined");
        XElement projectNode = doc.Descendants("ProjectInfo").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find ProjectInfo");
        return projectNode;
    }

    /// <summary>
    /// Retrieves the <c>&lt;References&gt;</c> element from the XML document.
    /// </summary>
    /// <returns>The <see cref="XElement"/> representing the references node.</returns>
    /// <exception cref="TcXmlException">Thrown when the document is undefined or the element is not found.</exception>
    private XElement GetReferencesNode()
    {
        if(doc is null)
            throw new TcXmlException("GetReferencesNode() - Document undefined");
        XElement referenceNode = doc.Descendants("References").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find References");
        return referenceNode;
    }

    /// <summary>
    /// Sets the library released state in the PLC project XML.
    /// </summary>
    /// <param name="released"><see langword="true"/> to mark the library as released; otherwise <see langword="false"/>.</param>
    /// <returns>The current instance for fluent chaining.</returns>
    /// <exception cref="TcXmlException">Thrown when the <c>Released</c> element is not found.</exception>
    public PlcProjectXml SetTcLibReleased(bool released)
    {
        XElement releasedNode = GetProjectInfoNode().Descendants("Released").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find Released");
        releasedNode.Value = released ? "true" : "false";
        return this;
    }

    /// <summary>
    /// Gets whether the library is marked as released in the PLC project XML.
    /// </summary>
    /// <returns><see langword="true"/> if the library is released; otherwise <see langword="false"/>.</returns>
    /// <exception cref="TcXmlException">Thrown when the <c>Released</c> element is not found.</exception>
    public bool GetTcLibReleased()
    {
        XElement releasedNode = GetProjectInfoNode().Descendants("Released").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find Released");
        return releasedNode.Value == "true";
    }

    /// <summary>
    /// Sets the library version string in the PLC project XML.
    /// </summary>
    /// <param name="version">The version string to set (e.g. <c>1.2.3</c>).</param>
    /// <returns>The current instance for fluent chaining.</returns>
    /// <exception cref="TcXmlException">Thrown when the <c>Version</c> element is not found.</exception>
    public PlcProjectXml SetTcLibVersion(string version)
    {
        XElement versionNode = GetProjectInfoNode().Descendants("Version").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find Version");
        versionNode.Value = version;
        return this;
    }

    /// <summary>
    /// Gets the project title from the PLC project XML.
    /// </summary>
    /// <returns>The project title string.</returns>
    /// <exception cref="TcXmlException">Thrown when the <c>Title</c> element is not found.</exception>
    public string GetProjectTitle()
    {
        XElement titleNode = GetProjectInfoNode().Descendants("Title").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find ProjectTitle");
        return titleNode.Value;
    }

    /// <summary>
    /// Gets the company name from the PLC project XML.
    /// </summary>
    /// <returns>The company name string.</returns>
    /// <exception cref="TcXmlException">Thrown when the <c>Company</c> element is not found.</exception>
    public string GetCompanyTitle()
    {
        XElement companyNode = GetProjectInfoNode().Descendants("Company").FirstOrDefault()
            ?? throw new TcXmlException("Unable to find CompanyTitle");
        return companyNode.Value;
    }

    /// <summary>
    /// Locates the TcUnit library reference element in the project references XML.
    /// </summary>
    /// <returns>The <see cref="XElement"/> representing the TcUnit library.</returns>
    /// <exception cref="TcXmlException">Thrown when the TcUnit library reference is not found.</exception>
    private XElement GetTcUnitXml()
    {
        XElement tcUnitLib = GetReferencesNode().Descendants("Library").FirstOrDefault(lib =>
            lib.Descendants("LibraryName").Any(name => name.Value == "TcUnit"))
            ?? throw new TcXmlException("Unable to find TcUnit library");
        return tcUnitLib;
    }

    /// <summary>
    /// Configures the TcUnit library parameters to enable result publishing to the specified file path.
    /// Sets <c>XUNITENABLEPUBLISH</c> to <c>TRUE</c> and <c>XUNITFILEPATH</c> to the given path.
    /// </summary>
    /// <param name="resultPath">The target file path for TcUnit XML results.</param>
    /// <exception cref="TcXmlException">Thrown when the TcUnit parameters cannot be found.</exception>
    public void SetTcUnitPublish(string resultPath)
    {
        XElement tcUnitLib = GetTcUnitXml();

        XElement? publishEnableParam = tcUnitLib.Descendants("Parameter").FirstOrDefault(parm =>
            (string?)parm.Element("Name") == "GVL_PARAM_TCUNIT.XUNITENABLEPUBLISH")
            ?? throw new TcXmlException("Unable to find TcUnit publish enable");
        XElement? publishEnableValue = publishEnableParam.Element("Value");
        if (publishEnableValue is null)
            throw new TcXmlException("Unable to locate TcUnit publish enable value");
        publishEnableValue.Value = "TRUE";

        XElement? publishPathParam = tcUnitLib.Descendants("Parameter").FirstOrDefault(parm =>
            (string?)parm.Element("Name") == "GVL_PARAM_TCUNIT.XUNITFILEPATH")
            ?? throw new TcXmlException("Unable to find TcUnit publish path");
        XElement? publishPathValue = publishPathParam.Element("Value");
        if (publishPathValue is null)
            throw new TcXmlException("Unable to locate TcUnit publish path value");
        publishPathValue.Value = $"'{resultPath}'";
    }

    /// <summary>
    /// Serializes the in-memory XML document to a string.
    /// </summary>
    /// <returns>The XML string representation of the PLC project.</returns>
    /// <exception cref="TcXmlException">Thrown when the document is undefined.</exception>
    public string ToXmlString()
    {
        if (doc is null)
            throw new TcXmlException("ToXmlString() - Doc undefined");
        return doc.ToString(); 
    }
}
