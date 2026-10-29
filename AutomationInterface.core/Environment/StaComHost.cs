using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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
    private const uint WM_APP_RUN_QUEUE = 0x8000; // WM_APP

    private readonly Thread thread;
    private readonly TaskScheduler scheduler;
    private readonly StaSynchronizationContext context;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaComHost"/> class.
    /// Starts a background STA thread with a Win32 message loop and captures its <see cref="TaskScheduler"/>.
    /// The OLE message filter is registered on the thread for the lifetime of the host.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the OLE message filter cannot be registered on the STA thread.</exception>
    [SupportedOSPlatform("windows")]
    public StaComHost()
    {
        var schedulerTcs = new TaskCompletionSource<TaskScheduler>();
        context = new StaSynchronizationContext();

        thread = new Thread(() =>
        {
            bool oleInitialized = false;
            try
            {
                // OleInitialize (not just CoInitializeEx/STA) is required for CoRegisterMessageFilter
                // to actually take effect; without it, COM busy calls throw RPC_E_SERVERCALL_RETRYLATER
                // immediately instead of being retried transparently by the RPC layer.
                Marshal.ThrowExceptionForHR(OleInitialize(IntPtr.Zero));
                oleInitialized = true;

                // The OLE message filter is per-thread, so register it once for the lifetime of this
                // apartment. Every COM call dispatched through this host (including the initial
                // CoCreateInstance and Running Object Table lookups) is then covered by busy-call retries.
                MessageFilter.Register();

                // Force creation of this thread's message queue so that PostThreadMessage from other
                // threads succeeds before the first GetMessage call.
                PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

                // Install a SynchronizationContext that really marshals onto THIS thread. The default
                // SynchronizationContext posts to the thread pool, which would silently run every
                // "STA" callback on an MTA pool thread instead (where message filters cannot be registered).
                context.Bind(GetCurrentThreadId());
                SynchronizationContext.SetSynchronizationContext(context);
            }
            catch (Exception ex)
            {
                // Surface startup failures through the constructor instead of hanging it on an
                // unfinished scheduler task and crashing the process with an unhandled thread exception.
                schedulerTcs.SetException(ex);
                if (oleInitialized)
                    OleUninitialize();
                return;
            }

            schedulerTcs.SetResult(TaskScheduler.FromCurrentSynchronizationContext());

            RunMessageLoop();

            // Tear down in reverse order. A failed revoke is not actionable here: the apartment is
            // going away with this thread anyway, and there is no logger on the host.
            MessageFilter.Revoke();
            OleUninitialize();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        // GetResult() rethrows the original startup exception instead of an AggregateException.
        scheduler = schedulerTcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets a value indicating whether the calling thread is the hosted STA thread.
    /// </summary>
    public bool IsOnStaThread => Thread.CurrentThread == thread;

    /// <summary>
    /// Schedules an <see cref="Action"/> to run on the STA thread.
    /// When already called from the STA thread the action runs inline, so nested dispatches
    /// (and callers that block on the returned task) cannot deadlock the message loop.
    /// </summary>
    /// <param name="action">The action to execute on the STA thread.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task RunAsync(Action action)
    {
        if (IsOnStaThread)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        return Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);
    }

    /// <summary>
    /// Schedules a <see cref="Func{T}"/> to run on the STA thread and returns its result.
    /// When already called from the STA thread the function runs inline (see <see cref="RunAsync(Action)"/>).
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute on the STA thread.</param>
    /// <returns>A <see cref="Task{T}"/> containing the result of the function.</returns>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        if (IsOnStaThread)
        {
            try
            {
                return Task.FromResult(func());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

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
        if (IsOnStaThread)
            throw new InvalidOperationException("StaComHost cannot be disposed from its own STA thread.");

        RunAsync(() => PostQuitMessage(0)).Wait();
        thread.Join();
    }

    /// <summary>
    /// Runs the Win32 message pump on the STA thread until a <c>WM_QUIT</c> message is received.
    /// Queued callbacks are executed when the wake-up thread message arrives, and after every
    /// other message as well so that coalesced wake-ups never leave work behind.
    /// </summary>
    private void RunMessageLoop()
    {
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_RUN_QUEUE)
            {
                context.DrainQueue();
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);

            context.DrainQueue();
        }
    }

    /// <summary>
    /// <see cref="SynchronizationContext"/> whose <see cref="Post"/> queues the callback and wakes the
    /// STA message loop with a thread message, so callbacks and task continuations run on the STA thread.
    /// </summary>
    private sealed class StaSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> queue = new();
        private uint nativeThreadId;

        public void Bind(uint threadId) => nativeThreadId = threadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            queue.Enqueue((d, state));
            PostThreadMessage(nativeThreadId, WM_APP_RUN_QUEUE, UIntPtr.Zero, IntPtr.Zero);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (GetCurrentThreadId() == nativeThreadId)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim(false);
            ExceptionDispatchInfo? error = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
                finally { done.Set(); }
            }, null);
            done.Wait();
            error?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void DrainQueue()
        {
            while (queue.TryDequeue(out var item))
                item.callback(item.state);
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

    private const uint PM_NOREMOVE = 0;

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    #endregion
}
