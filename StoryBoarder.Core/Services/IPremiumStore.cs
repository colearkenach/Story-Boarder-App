namespace StoryBoarder.Services;

/// <summary>
/// The one-time "Premium" purchase that removes ads. The Android app provides it through Google Play Billing; the
/// desktop app has no ads, so it has none.
/// </summary>
public interface IPremiumStore
{
    /// <summary>True once the user owns Premium: no ads.</summary>
    bool IsPremium { get; }

    /// <summary>The price in the user's own currency (e.g. "$1.99"), or null until the store has answered.</summary>
    string? Price { get; }

    /// <summary>Raised on the UI thread when <see cref="IsPremium"/> or <see cref="Price"/> changes.</summary>
    event Action? Changed;

    /// <summary>Shows the store's purchase sheet. Returns a message for the user, or null if there's nothing to say.</summary>
    Task<string?> PurchaseAsync();

    /// <summary>Pretends to own (or not own) Premium, to try the ad-free app before the product exists. Debug builds only.</summary>
    void SimulatePremium(bool premium);
}
