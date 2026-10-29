using System.Text;
using TwinCAT.Ads;

namespace AutomationInterface.core;

/// <summary>
/// File access on a TwinCAT target through the ADS file API of the TwinCAT system service
/// (AMS port 10000). This is the same mechanism the PLC blocks <c>FB_FileOpen</c>, <c>FB_FileRead</c>
/// and <c>FB_FileClose</c> use, so it works for Windows, Windows CE and TwinCAT/BSD targets.
/// </summary>
internal sealed class AdsFileAccess
{
    // Index groups of the system service file API
    private const uint FileOpen = 120;
    private const uint FileClose = 121;
    private const uint FileRead = 122;

    // Open mode flags (index offset of FileOpen)
    private const uint ModeRead = 0x0001;
    private const uint ModeBinary = 0x0010;

    /// <summary>Bytes requested per read; the system service returns fewer at the end of the file.</summary>
    private const int ChunkSize = 16 * 1024;

    /// <summary>
    /// Upper bound on the content read into memory. Guards against paths that never report end of file,
    /// e.g. device files such as <c>/dev/zero</c> on TwinCAT/BSD or a log that grows faster than it is read.
    /// </summary>
    private const long MaxFileSize = 64L * 1024 * 1024;

    private readonly AdsClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdsFileAccess"/> class.
    /// </summary>
    /// <param name="client">A client connected to the system service (port 10000) of the target.</param>
    public AdsFileAccess(AdsClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Reads a whole file from the target.
    /// </summary>
    /// <param name="remotePath">The path on the target, e.g. <c>/home/Administrator/tcUnitResults.xml</c> or <c>C:\TwinCAT\results.xml</c>.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="AdsErrorException">Thrown when the target rejects the request, e.g. when the file does not exist (0x70C) or access is denied.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the content exceeds <see cref="MaxFileSize"/>.</exception>
    public byte[] ReadAllBytes(string remotePath)
    {
        uint handle = Open(remotePath, ModeRead | ModeBinary);
        try
        {
            return ReadToEnd(handle);
        }
        finally
        {
            Close(handle);
        }
    }

    private uint Open(string remotePath, uint mode)
    {
        // The path is sent as a null-terminated string; the handle comes back as a 32 bit value
        byte[] path = Encoding.UTF8.GetBytes(remotePath + '\0');
        byte[] handle = new byte[sizeof(uint)];

        client.ReadWrite(FileOpen, mode, handle, path);
        return BitConverter.ToUInt32(handle, 0);
    }

    private byte[] ReadToEnd(uint handle)
    {
        using var content = new MemoryStream();
        byte[] chunk = new byte[ChunkSize];

        // Zero bytes marks the end of the file. A short read before that is possible in principle,
        // so keep going until the target reports nothing left rather than stopping at the first short chunk.
        int read;
        do
        {
            read = client.ReadWrite(FileRead, handle, chunk, ReadOnlyMemory<byte>.Empty);
            if (content.Length + read > MaxFileSize)
                throw new InvalidOperationException($"The file exceeds the maximum supported size of {MaxFileSize / (1024 * 1024)} MiB.");

            content.Write(chunk, 0, read);
        }
        while (read > 0);

        return content.ToArray();
    }

    private void Close(uint handle)
    {
        client.ReadWrite(FileClose, handle, Memory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
    }
}
