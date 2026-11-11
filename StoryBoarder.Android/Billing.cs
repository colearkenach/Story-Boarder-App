using Android.BillingClient.Api;
using Android.Content;
using Avalonia.Threading;
using StoryBoarder.Services;

namespace StoryBoarder.Android;

/// <summary>
/// The one-time "Premium" purchase that removes ads, through Google Play Billing. Ownership is checked with Google
/// Play whenever the app starts or comes back to the foreground, so Premium carries over to reinstalls and new
/// phones (it belongs to the Google account) and goes away again if the purchase is refunded.
/// </summary>
internal sealed class Billing : Java.Lang.Object, IPremiumStore, IPurchasesUpdatedListener
{
    /// <summary>
    /// The product to create in Play Console (Monetize with Play → Products → One-time products), with its price.
    /// Play Console only allows that once a build with this billing code has been uploaded to a testing track.
    /// </summary>
    public const string ProductId = "remove_ads";

    private const string PrefsName = "billing", PremiumKey = "premium";

    private BillingClient? _client;
    private ProductDetails? _product;
    private ISharedPreferences? _prefs;
    private TaskCompletionSource<string?>? _purchase;
    private bool _refreshing;
    private bool _available; // Google Play Billing answered: Play Store installed, signed in, reachable
    private bool _connecting;

    public static Billing Instance { get; } = new();

    public bool IsPremium { get; private set; }
    public string? Price { get; private set; }
    public event Action? Changed;

    public void Start(Context context)
    {
        if (_client is not null) return;
        _prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        // The last known answer, so a Premium user doesn't see an ad flash up while Google Play replies.
        IsPremium = _prefs?.GetBoolean(PremiumKey, false) ?? false;
        _client = BillingClient.NewBuilder(context)
            .SetListener(this)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .EnableAutoServiceReconnection()
            .Build();
        _ = ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_connecting) return; // start-up and resume both ask; one attempt at a time
        _connecting = true;
        try
        {
            var result = await _client!.StartConnectionAsync(() => _available = false);
            // No Play Store, or not signed in: keep the last known answer, and try again on the next resume.
            _available = result.ResponseCode == BillingResponseCode.Ok;
            if (_available) await RefreshAsync();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            _available = false;
        }
        finally
        {
            _connecting = false;
        }
    }

    /// <summary>Asks Google Play what the user owns and what Premium costs. Called on start and on every resume.</summary>
    public async Task RefreshAsync()
    {
        if (_client is null || _refreshing) return;
        if (!_available || !_client.IsReady)
        {
            await ConnectAsync();
            return;
        }
        var client = _client;
        _refreshing = true;
        try
        {
            var owned = await client.QueryPurchasesAsync(
                QueryPurchasesParams.NewBuilder().SetProductType(BillingClient.ProductType.Inapp).Build());
            if (owned.Result.ResponseCode == BillingResponseCode.Ok)
                await HandlePurchasesAsync(owned.Purchases, everythingOwned: true);

            var product = QueryProductDetailsParams.Product.NewBuilder()
                .SetProductId(ProductId)
                .SetProductType(BillingClient.ProductType.Inapp)
                .Build();
            var details = await client.QueryProductDetailsAsync(
                QueryProductDetailsParams.NewBuilder().SetProductList([product]).Build());
            if (details.Result.ResponseCode == BillingResponseCode.Ok)
            {
                _product = details.ProductDetailsList?.FirstOrDefault(p => p.ProductId == ProductId);
                SetState(IsPremium, _product?.GetOneTimePurchaseOfferDetails()?.FormattedPrice);
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            // Keep what we knew; the next resume tries again.
        }
        finally
        {
            _refreshing = false;
        }
    }

    public async Task<string?> PurchaseAsync()
    {
        if (IsPremium) return null;
        // Answer at once rather than wait on Google Play's own retries, which take several seconds.
        if (!_available || _client is not { IsReady: true } client)
        {
            if (_client is not null) _ = ConnectAsync();
            return "Google Play isn't available right now. Check that you're online and signed in to the Play Store, then try again.";
        }
        // The price normally loads at startup; if it hasn't, give Google Play a few seconds at most.
        if (_product is null) await Task.WhenAny(RefreshAsync(), Task.Delay(TimeSpan.FromSeconds(4)));
        if (_product is null) return "Premium isn't available yet. Please try again later.";
        if (MainActivity.Current is not { } activity) return null;

        var flow = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(_product).Build()])
            .Build();
        _purchase = new TaskCompletionSource<string?>();
        var launch = client.LaunchBillingFlow(activity, flow);
        if (launch.ResponseCode == BillingResponseCode.ItemAlreadyOwned)
        {
            _purchase = null;
            await RefreshAsync();
            return null;
        }
        if (launch.ResponseCode != BillingResponseCode.Ok)
        {
            _purchase = null;
            return "The purchase couldn't be started. Please try again.";
        }
        // Google Play's purchase sheet is up; the outcome arrives in OnPurchasesUpdated.
        return await _purchase.Task;
    }

    public void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases) =>
        Dispatcher.UIThread.Post(() => _ = OnPurchasesUpdatedAsync(result, purchases));

    private async Task OnPurchasesUpdatedAsync(BillingResult result, IList<Purchase>? purchases)
    {
        string? message = null;
        if (result.ResponseCode == BillingResponseCode.Ok)
        {
            await HandlePurchasesAsync(purchases, everythingOwned: false);
            var pending = purchases?.Any(p => p.Products.Contains(ProductId) && p.PurchaseState == PurchaseState.Pending) == true;
            message = pending
                ? "Your payment is pending. Ads will turn off as soon as it goes through."
                : IsPremium ? "Thanks for going Premium! Ads are off." : null;
        }
        else if (result.ResponseCode == BillingResponseCode.ItemAlreadyOwned)
        {
            await RefreshAsync();
        }
        else if (result.ResponseCode != BillingResponseCode.UserCancelled)
        {
            message = "The purchase didn't go through. Please try again.";
        }
        _purchase?.TrySetResult(message);
        _purchase = null;
    }

    // A query lists everything the user owns, so Premium missing from it means not owned (e.g. refunded);
    // purchase updates only list what just changed.
    private async Task HandlePurchasesAsync(IList<Purchase>? purchases, bool everythingOwned)
    {
        var owned = false;
        foreach (var purchase in purchases ?? [])
        {
            if (!purchase.Products.Contains(ProductId) || purchase.PurchaseState != PurchaseState.Purchased) continue;
            owned = true;
            // Google refunds purchases that aren't acknowledged within 3 days. If this fails (offline), the next
            // refresh sees the purchase again and retries.
            if (!purchase.IsAcknowledged)
            {
                try
                {
                    await _client!.AcknowledgePurchaseAsync(
                        AcknowledgePurchaseParams.NewBuilder().SetPurchaseToken(purchase.PurchaseToken).Build());
                }
                catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
                {
                }
            }
        }
        if (owned || everythingOwned) SetState(owned, Price);
    }

    public void SimulatePremium(bool premium) => SetState(premium, Price);

    private void SetState(bool premium, string? price)
    {
        if (premium == IsPremium && price == Price) return;
        IsPremium = premium;
        Price = price;
        _prefs?.Edit()?.PutBoolean(PremiumKey, premium)?.Apply();
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}
