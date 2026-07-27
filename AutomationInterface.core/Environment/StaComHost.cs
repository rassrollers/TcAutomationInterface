using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AutomationInterface.core;

/// <summary>
/// Hosts a dedicated STA (Single-Threaded Apartment) thread with a Win32 message loop
/// for marshaling COM interop calls. All DTE and TwinCAT COM object access must be
/// dispatched through this host to satisfy COM threading requirements.
/// </summary>
public sealed class StaComHost : IDisposable
{
    private readonly Thread thread;
    private readonly TaskScheduler scheduler;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaComHost"/> class.
    /// Starts a background STA thread with a Win32 message loop and captures its <see cref="TaskScheduler"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public StaComHost()
    {
        var schedulerTcs = new TaskCompletionSource<TaskScheduler>();

        thread = new Thread(() =>
        {
            // Install default SynchronizationContext
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            schedulerTcs.SetResult(TaskScheduler.FromCurrentSynchronizationContext());

            RunMessageLoop();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        scheduler = schedulerTcs.Task.Result;
    }

    /// <summary>
    /// Schedules an <see cref="Action"/> to run on the STA thread.
    /// </summary>
    /// <param name="action">The action to execute on the STA thread.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task RunAsync(Action action)
    {
        return Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);
    }

    /// <summary>
    /// Schedules a <see cref="Func{T}"/> to run on the STA thread and returns its result.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute on the STA thread.</param>
    /// <returns>A <see cref="Task{T}"/> containing the result of the function.</returns>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        return Task.Factory.StartNew(
            func,
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);
    }

    /// <summary>
    /// Posts a quit message to the STA thread's message loop and waits for the thread to exit.
    /// </summary>
    public void Dispose()
    {
        RunAsync(() => { PostQuitMessage(0); return 0; }).Wait();
        thread.Join();
    }

    /// <summary>
    /// Runs the Win32 message pump on the STA thread until a <c>WM_QUIT</c> message is received.
    /// </summary>
    private void RunMessageLoop()
    {
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    #region Win32 API

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    #endregion
}
