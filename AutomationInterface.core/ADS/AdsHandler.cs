using Microsoft.Extensions.Logging;
using TwinCAT.Ads;

namespace AutomationInterface.core;

public class AdsHandler : IDisposable
{
    private readonly ILogger log;
    AdsClient adsClient;

    public AdsHandler(ILogger logger)
    {
        log = logger;
        adsClient = new AdsClient();
    }

    public void Dispose()
    {
        Disconnect();
        adsClient.Dispose();
    }

    public void Connect(string NetId, int NetPort)
    {
        try
        {
            adsClient.Connect(NetId, NetPort);
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to connect to TwinCAT system with NetId: {netId} and Port: {netPort}. ADS return code: {code}", NetId, NetPort, ex.ErrorCode);
        }
    }

    public void Disconnect()
    {
        adsClient.Close();
    }

    public bool IsConnected()
    {
        return adsClient.IsConnected;
    }

    public bool IsTwinCatInRunMode()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        try
        {
            var state = adsClient.ReadState().AdsState;
            return state == AdsState.Run;
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to read TwinCAT state. ADS return code: {code}", ex.ErrorCode);
            return false;
        }
    }

    public void SetTwinCatInRunMode()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        try
        {
            var state = adsClient.ReadState();
            
            adsClient.WriteControl(new StateInfo { AdsState = AdsState.Reset, DeviceState = state.DeviceState });
            log.LogInformation("Set TwinCAT to RUN mode.");
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to set TwinCAT to RUN mode. ADS return code: {code}", ex.ErrorCode);
        }
    }

    public void SetTwinCatInConfigMode()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        try
        {
            var state = adsClient.ReadState();

            adsClient.WriteControl(new StateInfo { AdsState = AdsState.Reconfig, DeviceState = state.DeviceState });
            log.LogInformation("Set TwinCAT to CONFIG mode.");
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to set TwinCAT to CONFIG mode. ADS return code: {code}", ex.ErrorCode);
        }
    }

    public string GetTwinCatStatus()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        try
        {
            var state = adsClient.ReadState().AdsState;
            return $"TwinCAT State: {state.ToString()}";
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to read TwinCAT state. ADS return code: {code}", ex.ErrorCode);
            return string.Empty;
        }
    }
    
    public string GetDeviceInfo()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        try
        {
            var deviceInfo = adsClient.ReadDeviceInfo();
            return $"Device Info: {deviceInfo.Name}, {deviceInfo.Version}";
        }
        catch (AdsErrorException ex)
        {
            log.LogError(ex, "Failed to read TwinCAT device info. ADS return code: {code}", ex.ErrorCode);
            return string.Empty;
        }
    }

    /// <summary>
    /// Reads a file from the target through the ADS file API of the system service.
    /// The handler must be connected to port 10000 (<see cref="AmsPort.SystemService"/>).
    /// </summary>
    /// <param name="remotePath">The path on the target, e.g. <c>/home/Administrator/tcUnitResults.xml</c>.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not connected to the system service, or when the file exceeds the maximum supported size.</exception>
    /// <exception cref="TwinCatException">Thrown when the target rejects the request, e.g. the file does not exist or access is denied.</exception>
    public byte[] ReadFile(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        EnsureConnectedToSystemService();

        try
        {
            var content = new AdsFileAccess(adsClient).ReadAllBytes(remotePath);
            log.LogInformation("Read {bytes} byte(s) from '{path}' on the target", content.Length, remotePath);
            return content;
        }
        catch (AdsErrorException ex)
        {
            throw new TwinCatException($"Failed to read '{remotePath}' from the target. ADS return code: {ex.ErrorCode}", ex);
        }
    }

    /// <summary>
    /// Downloads a file from the target to the local file system.
    /// </summary>
    /// <param name="remotePath">The path on the target.</param>
    /// <param name="localPath">The local file to write, or a directory in which the remote file name is used. An existing file is overwritten.</param>
    /// <returns>The full path of the written local file.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not connected to the system service, or when the file exceeds the maximum supported size.</exception>
    /// <exception cref="TwinCatException">Thrown when the target rejects the request, e.g. the file does not exist or access is denied.</exception>
    public string DownloadFile(string remotePath, string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        byte[] content = ReadFile(remotePath);

        string fullPath = Path.GetFullPath(localPath);
        if (Directory.Exists(fullPath) || localPath.EndsWith(Path.DirectorySeparatorChar) || localPath.EndsWith(Path.AltDirectorySeparatorChar))
            fullPath = Path.Combine(fullPath, RemoteFileName(remotePath));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        log.LogInformation("Downloaded '{remote}' to '{local}'", remotePath, fullPath);
        return fullPath;
    }

    private void EnsureConnectedToSystemService()
    {
        if (!IsConnected())
            throw new InvalidOperationException("Not connected to any TwinCAT system.");

        int port = adsClient.Address.Port;
        if (port != (int)AmsPort.SystemService)
            throw new InvalidOperationException($"File access requires a connection to the system service (port {(int)AmsPort.SystemService}), but the handler is connected to port {port}.");
    }

    private static readonly char[] RemotePathSeparators = ['/', '\\'];

    /// <summary>Gets the file name of a target path, whether it uses '/' (TwinCAT/BSD) or '\' (Windows) separators.</summary>
    private static string RemoteFileName(string remotePath)
    {
        int separator = remotePath.LastIndexOfAny(RemotePathSeparators);
        return separator >= 0 ? remotePath[(separator + 1)..] : remotePath;
    }
}
