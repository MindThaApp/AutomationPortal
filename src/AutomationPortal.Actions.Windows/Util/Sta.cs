namespace Flowmate.Actions.Windows;

internal static class Sta
{
    /// <summary>Runs work on a fresh single-threaded-apartment thread (for shell and COM objects that want one).</summary>
    public static Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Flowmate STA" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    public static Task Run(Action work) => Run<object?>(() => { work(); return null; });
}
