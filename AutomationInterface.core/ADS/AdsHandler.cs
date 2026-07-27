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
}
