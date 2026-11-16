using System.Runtime.InteropServices;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Sends one item to the Recycle Bin through the shell's IFileOperation for runs with nobody watching. It never shows any
/// window, and it never lets the shell delete for good: the shell announces every delete to the progress sink first, saying
/// whether the item goes to the Recycle Bin, and any delete that wouldn't is refused before it happens.
/// </summary>
internal static class ShellRecycle
{
    /// <summary>
    /// What happened to the item. <see cref="GoneUnconfirmed"/>: it is gone, but the shell never said it went to the Recycle Bin.
    /// <see cref="Unfinished"/>: the shell didn't finish in time or the run was cancelled; the item may still be moving to the bin.
    /// </summary>
    internal enum Outcome { Recycled, WouldBePermanent, Failed, GoneUnconfirmed, Unfinished }

    /// <summary>How long a run waits for the shell before it stops waiting. Moving a big folder to the bin can take a while.</summary>
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(10);

    private const uint FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200, FOF_NOERRORUI = 0x400;
    private const uint FOF_NO_UI = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;
    internal const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;
    private const int S_OK = 0;
    private const int HRESULT_ERROR_CANCELLED = unchecked((int)0x800704C7);

    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>
    /// Runs on its own single-threaded apartment thread, as IFileOperation requires. Stops waiting when <paramref name="ct"/> is
    /// cancelled or after <paramref name="waitLimit"/> (default <see cref="WaitLimit"/>) and then reports <see cref="Outcome.Unfinished"/>;
    /// any delete the shell hasn't started by then is refused, and a delete for good is refused at any time.
    /// </summary>
    public static Outcome Send(string path, CancellationToken ct = default, TimeSpan? waitLimit = null)
    {
        var outcome = Outcome.Failed;
        var sink = new Sink();
        var done = new ManualResetEventSlim(false);   // not disposed: the shell thread may still set it after the wait ends
        var thread = new Thread(() =>
        {
            try { outcome = SendOnThisThread(path, sink); }
            catch (Exception) { outcome = Outcome.Failed; }   // never let a shell failure end the app; nothing is reported as recycled
            finally { done.Set(); }
        }) { IsBackground = true, Name = "Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished;
        try { finished = done.Wait(waitLimit ?? WaitLimit, ct); }
        catch (OperationCanceledException) { finished = done.IsSet; }
        if (finished) return outcome;   // the event's set and wait order the write before this read
        sink.Stop();
        return Outcome.Unfinished;
    }

    private static Outcome SendOnThisThread(string path, Sink sink)
    {
        var type = Type.GetTypeFromCLSID(ClsidFileOperation, throwOnError: true)!;
        var op = (IFileOperation)Activator.CreateInstance(type)!;
        var item = IntPtr.Zero;
        uint cookie = 0;
        try
        {
            var iid = IidShellItem;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item) < 0 || item == IntPtr.Zero) return Outcome.Failed;
            op.SetOperationFlags(FOF_ALLOWUNDO | FOF_NO_UI | FOFX_RECYCLEONDELETE);
            op.Advise(sink, out cookie);
            op.DeleteItem(item, IntPtr.Zero);
            try { op.PerformOperations(); }
            catch (COMException) { /* a refused or failed delete; what happened is read below */ }
            if (sink.Refused) return Outcome.WouldBePermanent;
            if (File.Exists(path) || Directory.Exists(path)) return Outcome.Failed;
            // Gone without the shell ever announcing the delete: nothing says it went to the Recycle Bin, so don't claim it did.
            return sink.Allowed ? Outcome.Recycled : Outcome.GoneUnconfirmed;
        }
        finally
        {
            if (cookie != 0) try { op.Unadvise(cookie); } catch (COMException) { }
            if (item != IntPtr.Zero) Marshal.Release(item);
            Marshal.FinalReleaseComObject(op);
        }
    }

    /// <summary>Lets a delete through only when the shell says it goes to the Recycle Bin.</summary>
    [ComVisible(true)]
    private sealed class Sink : IFileOperationProgressSink
    {
        private volatile bool _refused, _allowed, _stopped;

        public bool Refused => _refused;
        public bool Allowed => _allowed;

        /// <summary>Nobody waits for the result any more: refuse any delete the shell hasn't started yet.</summary>
        public void Stop() => _stopped = true;

        public int PreDeleteItem(uint dwFlags, IntPtr psiItem)
        {
            if (_stopped) return HRESULT_ERROR_CANCELLED;
            if ((dwFlags & TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0) { _allowed = true; return S_OK; }
            _refused = true;
            return HRESULT_ERROR_CANCELLED;
        }

        public int PostDeleteItem(uint dwFlags, IntPtr psiItem, int hrDelete, IntPtr psiNewlyCreated) => S_OK;

        public int StartOperations() => S_OK;
        public int FinishOperations(int hrResult) => S_OK;
        public int PreRenameItem(uint dwFlags, IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName) => S_OK;
        public int PostRenameItem(uint dwFlags, IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrRename, IntPtr psiNewlyCreated) => S_OK;
        public int PreMoveItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName) => S_OK;
        public int PostMoveItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IntPtr psiNewlyCreated) => S_OK;
        public int PreCopyItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName) => S_OK;
        public int PostCopyItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IntPtr psiNewlyCreated) => S_OK;
        public int PreNewItem(uint dwFlags, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName) => S_OK;
        public int PostNewItem(uint dwFlags, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes, int hrNew, IntPtr psiNewItem) => S_OK;
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => S_OK;
        public int ResetTimer() => S_OK;
        public int PauseTimer() => S_OK;
        public int ResumeTimer() => S_OK;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IntPtr psiItem);
        void ApplyPropertiesToItems(IntPtr punkItems);
        void RenameItem(IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        void RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        void MoveItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        void CopyItem(IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
        void CopyItems(IntPtr punkItems, IntPtr psiDestinationFolder);
        void DeleteItem(IntPtr psiItem, IntPtr pfopsItem);
        void DeleteItems(IntPtr punkItems);
        void NewItem(IntPtr psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hrResult);
        [PreserveSig] int PreRenameItem(uint dwFlags, IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostRenameItem(uint dwFlags, IntPtr psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrRename, IntPtr psiNewlyCreated);
        [PreserveSig] int PreMoveItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostMoveItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IntPtr psiNewlyCreated);
        [PreserveSig] int PreCopyItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostCopyItem(uint dwFlags, IntPtr psiItem, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IntPtr psiNewlyCreated);
        [PreserveSig] int PreDeleteItem(uint dwFlags, IntPtr psiItem);
        [PreserveSig] int PostDeleteItem(uint dwFlags, IntPtr psiItem, int hrDelete, IntPtr psiNewlyCreated);
        [PreserveSig] int PreNewItem(uint dwFlags, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostNewItem(uint dwFlags, IntPtr psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes, int hrNew, IntPtr psiNewItem);
        [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
