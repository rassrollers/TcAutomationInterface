using System.Xml;

namespace AutomationInterface.core;

/// <summary>
/// This class it for handling the TwinCAT project file (.tsproj)
/// </summary>
internal class TcProjectXml
{
    private XmlDocument doc = new XmlDocument();
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcProjectXml"/> class by loading the specified <c>.tsproj</c> file.
    /// </summary>
    /// <param name="pathToFile">The full path to the <c>.tsproj</c> file.</param>
    public TcProjectXml(string pathToFile)
    {
        filePath = pathToFile;
        Reset(pathToFile);
    }

    /// <summary>
    /// Reloads the XML document from the specified file, discarding any in-memory changes.
    /// </summary>
    /// <param name="pathToFile">The full path to the <c>.tsproj</c> file to reload.</param>
    public void Reset(string pathToFile)
    {
        doc = new XmlDocument();
        doc.Load(pathToFile);
    }

    /// <summary>
    /// Gets the TwinCAT version string from the project file (e.g. <c>3.1.4024.50</c>).
    /// </summary>
    /// <returns>The TcVersion attribute value.</returns>
    /// <exception cref="TcXmlException">Thrown when the document root or attribute is not found.</exception>
    public string GetTcVersion()
    {
        XmlElement root = doc.DocumentElement ?? throw new TcXmlException("Unable to extract document root from xml file");
        if (root.HasAttribute("TcVersion"))
        {
            return root.GetAttribute("TcVersion");
        }
        throw new TcXmlException("TcVersion attribute not found");
    }

    /// <summary>
    /// Checks whether a project variant is defined in the <c>.tsproj</c> file.
    /// </summary>
    /// <returns><see langword="true"/> if the <c>TcProjectVariant</c> attribute exists; otherwise <see langword="false"/>.</returns>
    /// <exception cref="TcXmlException">Thrown when the document root cannot be extracted.</exception>
    public bool IsTcProjectVariantDefined()
    {
        XmlElement root = doc.DocumentElement ?? throw new TcXmlException("Unable to extract document root from xml file");
        if (root.HasAttribute("TcProjectVariant"))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the project variant name from the <c>.tsproj</c> file.
    /// </summary>
    /// <returns>The TcProjectVariant attribute value.</returns>
    /// <exception cref="TcXmlException">Thrown when the attribute is not found.</exception>
    public string GetTcProjectVariant()
    {
        XmlElement root = doc.DocumentElement ?? throw new TcXmlException("Unable to extract document root from xml file");
        if (root.HasAttribute("TcProjectVariant"))
        {
            return root.GetAttribute("TcProjectVariant");
        }
        throw new TcXmlException("TcProjectVariant attribute not found");
    }

    /// <summary>
    /// Gets whether the TwinCAT version is pinned (fixed) for this project.
    /// </summary>
    /// <returns><see langword="true"/> if the version is fixed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="TcXmlException">Thrown when the document root cannot be extracted.</exception>
    public bool GetTcVersionFixed()
    {
        XmlElement root = doc.DocumentElement ?? throw new TcXmlException("Unable to extract document root from xml file");
        if (root.HasAttribute("TcVersionFixed"))
        {
            return bool.Parse(root.GetAttribute("TcVersionFixed"));
        }
        return false;
    }

    /// <summary>
    /// Adds a license key to the <c>&lt;System&gt;&lt;Licenses&gt;&lt;Target&gt;</c> section of the project file
    /// and saves the document to disk.
    /// </summary>
    /// <param name="licenseKey">The license key string to add as a <c>&lt;ManualSelect&gt;</c> element.</param>
    /// <exception cref="TcXmlException">Thrown when no <c>&lt;System&gt;</c> node is defined in the file.</exception>
    public void AddLicenses(string licenseKey)
    {
        XmlNode? sysNode = doc.SelectSingleNode("//Project/System");
        if (sysNode == null)
            throw new TcXmlException("No <System> defined in .tsproj file");

        // Ensure <Licenses> exists
        XmlElement? licenses = sysNode.SelectSingleNode("Licenses") as XmlElement;
        if (licenses == null)
        {
            licenses = doc.CreateElement("Licenses");
            sysNode.AppendChild(licenses);
        }

        // Ensure <Target> exists inside <Licenses>
        XmlElement? target = licenses.SelectSingleNode("Target") as XmlElement;
        if (target == null)
        {
            target = doc.CreateElement("Target");
            licenses.AppendChild(target);
        }

        // Create <ManualSelect> and add license key
        XmlElement manual = doc.CreateElement("ManualSelect");
        manual.InnerText = licenseKey;
        target.AppendChild(manual);

        doc.Save(filePath);
    }

    /// <summary>
    /// Serializes the in-memory XML document to a string.
    /// </summary>
    /// <returns>The XML string representation of the project file.</returns>
    /// <exception cref="TcXmlException">Thrown when the document is undefined.</exception>
    public string ToXmlString()
    {
        if (doc is null)
            throw new TcXmlException("ToXmlString() - Doc undefined");
        return doc.OuterXml;
    }
}
