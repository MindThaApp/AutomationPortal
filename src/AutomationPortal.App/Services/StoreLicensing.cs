using AutomationPortal.App.Logic;
using Windows.Services.Store;

namespace AutomationPortal.App.Services;

/// <summary>
/// The Microsoft Store through <see cref="StoreContext"/>. Only used when the app has package identity. Every call is wrapped:
/// trouble is logged and comes back as a failed read, no prices or <see cref="PurchaseStatus.Failed"/>, never as an exception.
/// </summary>
public sealed class StoreLicensing : IStoreLicensing
{
    private readonly object _gate = new();
    private StoreContext? _context;

    public event EventHandler? LicensesChanged;

    /// <summary>The context for reads and the licenses-changed event, made on first use.</summary>
    private StoreContext Context
    {
        get
        {
            lock (_gate)
            {
                if (_context is not null) return _context;
                _context = StoreContext.GetDefault();
                _context.OfflineLicensesChanged += (_, _) => LicensesChanged?.Invoke(this, EventArgs.Empty);
                return _context;
            }
        }
    }

    public async Task<LicenseRead> ReadLicensesAsync()
    {
        try
        {
            var license = await Context.GetAppLicenseAsync();
            // No license, or an inactive app license, isn't an answer to trust: it never lowers the tier.
            if (license is null || !license.IsActive) return LicenseRead.Failed;
            var addOns = new List<AddOnLicense>();
            foreach (var pair in license.AddOnLicenses)
            {
                var l = pair.Value;
                if (l is null) continue;
                // The key and SkuStoreId look like "9NVRQ54GH9MG/0010"; either is matched by its Store id prefix.
                addOns.Add(new AddOnLicense(string.IsNullOrWhiteSpace(l.SkuStoreId) ? pair.Key : l.SkuStoreId, l.IsActive));
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Key != l.SkuStoreId) addOns.Add(new AddOnLicense(pair.Key, l.IsActive));
            }
            return new LicenseRead(true, addOns);
        }
        catch (Exception ex)
        {
            AppLog.Write("Store: reading licenses failed: " + ex.Message);
            return LicenseRead.Failed;
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPricesAsync(IReadOnlyList<string> storeIds)
    {
        var prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await Context.GetAssociatedStoreProductsAsync(new[] { "Durable" });
            if (result.ExtendedError is { } error) AppLog.Write("Store: products: " + error.Message);
            foreach (var pair in result.Products)
            {
                var product = pair.Value;
                var id = storeIds.FirstOrDefault(s => AddOnIds.Matches(product?.StoreId ?? pair.Key, s));
                if (id is not null && product?.Price?.FormattedPrice is { Length: > 0 } price) prices[id] = price;
            }
        }
        catch (Exception ex) { AppLog.Write("Store: reading prices failed: " + ex.Message); }
        return prices;
    }

    public async Task<PurchaseStatus> PurchaseAsync(string storeId, IntPtr ownerHandle)
    {
        try
        {
            // A desktop app has to tell the Store which window owns its purchase window.
            var context = StoreContext.GetDefault();
            if (ownerHandle != IntPtr.Zero)
            {
                try { WinRT.Interop.InitializeWithWindow.Initialize(context, ownerHandle); }
                catch (Exception ex) { AppLog.Write("Store: window for purchase: " + ex.Message); }
            }
            var result = await context.RequestPurchaseAsync(storeId);
            if (result.ExtendedError is { } error) AppLog.Write($"Store: purchase of {storeId} ended {result.Status}: {error.Message}");
            return result.Status switch
            {
                StorePurchaseStatus.Succeeded => PurchaseStatus.Succeeded,
                StorePurchaseStatus.AlreadyPurchased => PurchaseStatus.AlreadyPurchased,
                StorePurchaseStatus.NotPurchased => PurchaseStatus.NotPurchased,
                StorePurchaseStatus.NetworkError => PurchaseStatus.NetworkError,
                StorePurchaseStatus.ServerError => PurchaseStatus.ServerError,
                _ => PurchaseStatus.Failed,
            };
        }
        catch (Exception ex)
        {
            AppLog.Write($"Store: purchase of {storeId} failed: " + ex.Message);
            return PurchaseStatus.Failed;
        }
    }
}
