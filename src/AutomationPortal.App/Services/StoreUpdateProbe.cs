using AutomationPortal.App.Logic;
using Windows.Services.Store;

namespace AutomationPortal.App.Services;

/// <summary>Asks the Store whether a newer package is waiting. Read only: it never downloads or installs anything.</summary>
internal sealed class StoreUpdateProbe : IStoreUpdateProbe
{
    public async Task<StoreUpdateInfo?> QueryAsync()
    {
        try
        {
            var updates = await Task.Run(() => StoreContext.GetDefault().GetAppAndOptionalStorePackageUpdatesAsync().AsTask()).ConfigureAwait(false);
            if (updates.Count == 0) return new StoreUpdateInfo(false, null);
            Version? newest = null;
            foreach (var u in updates)
            {
                var v = u.Package.Id.Version;
                var version = new Version(v.Major, v.Minor, v.Build, v.Revision);
                if (newest is null || version > newest) newest = version;
            }
            return new StoreUpdateInfo(true, newest?.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Write("Store: update check failed: " + ex.Message);
            return null;
        }
    }
}
