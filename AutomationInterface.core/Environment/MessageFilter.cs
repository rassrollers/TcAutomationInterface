using System.Runtime.InteropServices;
using Microsoft.VisualStudio.OLE.Interop;

namespace AutomationInterface.core;

/// <summary>
/// Implements <see cref="IOleMessageFilter"/> to handle COM call rejections and retries
/// when communicating with the Visual Studio DTE. Each STA thread must register its own filter
/// via <see cref="Register"/> before making COM calls, and revoke it via <see cref="Revoke"/> on cleanup.
/// </summary>
public class MessageFilter : IOleMessageFilter
{
    /// <summary>
    /// Registers this message filter on the current thread to handle rejected COM calls.
    /// </summary>
    public static void Register()
    {
        IOleMessageFilter newFilter = new MessageFilter();
        CoRegisterMessageFilter(newFilter, out _);
    }

    /// <summary>
    /// Revokes the currently registered message filter on the current thread.
    /// </summary>
    public static void Revoke()
    {
        IOleMessageFilter? oldFilter = null;
        CoRegisterMessageFilter(null, out oldFilter);
    }

    /// <summary>
    /// Handles an incoming call on this thread. Returns <c>SERVERCALL_ISHANDLED</c> (0)
    /// to accept all incoming calls.
    /// </summary>
    /// <param name="dwCallType">The type of call (cyclic, input-sync, etc.).</param>
    /// <param name="hTaskCaller">Handle to the task making the call.</param>
    /// <param name="dwTickCount">Elapsed time since the call was made.</param>
    /// <param name="lpInterfaceInfo">Pointer to interface information.</param>
    /// <returns>Always returns 0 (<c>SERVERCALL_ISHANDLED</c>).</returns>
    int IOleMessageFilter.HandleInComingCall(int dwCallType,
      System.IntPtr hTaskCaller, int dwTickCount, System.IntPtr
      lpInterfaceInfo)
    {
        //Return the flag SERVERCALL_ISHANDLED.
        return 0;
    }

    /// <summary>
    /// Called when an outgoing COM call is rejected. If the rejection reason is
    /// <c>SERVERCALL_RETRYLATER</c>, retries after 500ms; otherwise cancels the call.
    /// </summary>
    /// <param name="hTaskCallee">Handle to the task being called.</param>
    /// <param name="dwTickCount">Elapsed time since the original call.</param>
    /// <param name="dwRejectType">The reason for rejection.</param>
    /// <returns>Retry delay in milliseconds (&gt;= 0), or -1 to cancel.</returns>
    int IOleMessageFilter.RetryRejectedCall(System.IntPtr
      hTaskCallee, int dwTickCount, int dwRejectType)
    {
        // flag = SERVERCALL_RETRYLATER.
        if (dwRejectType == (int)SERVERCALL.SERVERCALL_RETRYLATER)
        {
            // Retry the thread call immediately if return >=0 & 
            return 500; // milliseconds
        }
        // Too busy; cancel call.
        return -1; // Cancel
    }

    /// <summary>
    /// Called when a message is pending during an outgoing COM call.
    /// Returns <c>PENDINGMSG_WAITDEFPROCESS</c> to continue waiting and process default messages.
    /// </summary>
    /// <param name="hTaskCallee">Handle to the task being called.</param>
    /// <param name="dwTickCount">Elapsed time since the original call.</param>
    /// <param name="dwPendingType">The type of pending message.</param>
    /// <returns><c>PENDINGMSG_WAITDEFPROCESS</c> to continue default processing.</returns>
    int IOleMessageFilter.MessagePending(System.IntPtr hTaskCallee,
      int dwTickCount, int dwPendingType)
    {
        //Return the flag PENDINGMSG_WAITDEFPROCESS.
        return (int)PENDINGMSG.PENDINGMSG_WAITDEFPROCESS;
    }

    // Implement the IOleMessageFilter interface.
    [DllImport("Ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter oldFilter);
}
