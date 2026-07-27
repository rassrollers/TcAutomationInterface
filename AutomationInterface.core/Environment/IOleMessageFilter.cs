using System.Runtime.InteropServices;

namespace AutomationInterface.core;

/// <summary>
/// COM import interface for the OLE message filter, used to handle rejected or pending
/// COM calls between apartment threads. This interface allows the application to retry
/// calls that Visual Studio rejects when it is busy.
/// <see href="https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-imessagefilter"/>
/// </summary>
[ComImport(), Guid("00000016-0000-0000-C000-000000000046"),
InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleMessageFilter
{
    /// <summary>
    /// Called by COM when an incoming call arrives on the server thread.
    /// </summary>
    /// <param name="dwCallType">Type of call, sync or async.</param>
    /// <param name="hTaskCaller">Handler to the task that is making the call.</param>
    /// <param name="dwTickCount">Elapsed time since the call was made.</param>
    /// <param name="lpInterfaceInfo">Pointer to information about the interface.</param>
    /// <returns>A <c>SERVERCALL</c> value indicating how to handle the call.</returns>
    [PreserveSig]
    int HandleInComingCall(
        int dwCallType,
        IntPtr hTaskCaller,
        int dwTickCount,
        IntPtr lpInterfaceInfo);

    /// <summary>
    /// Called by COM when an outgoing call has been rejected by the callee.
    /// </summary>
    /// <param name="hTaskCallee">Handle to the task being called.</param>
    /// <param name="dwTickCount">Elapsed time since the original call.</param>
    /// <param name="dwRejectType">Reason for rejection.</param>
    /// <returns>-1 to cancel, 0 for immediate retry, or &gt;0 for delay in ms before retry.</returns>
    [PreserveSig]
    int RetryRejectedCall(
        IntPtr hTaskCallee,
        int dwTickCount,
        int dwRejectType);

    /// <summary>
    /// Called by COM when a Windows message arrives while waiting for a pending outgoing call.
    /// </summary>
    /// <param name="hTaskCallee">Handle to the task being called.</param>
    /// <param name="dwTickCount">Elapsed time since the original call.</param>
    /// <param name="dwPendingType">Type of pending message, e.g. user input, system event.</param>
    /// <returns>A <c>PENDINGMSG</c> value indicating how to process the pending message.</returns>
    [PreserveSig]
    int MessagePending(
        IntPtr hTaskCallee,
        int dwTickCount,
        int dwPendingType);
}
