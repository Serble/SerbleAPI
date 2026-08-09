namespace SerbleAPI.Data.Schemas;

/// <summary>
/// The event types an app can subscribe to. Stored on a subscription as a comma-separated list of
/// these slugs, and sent to the receiver in the payload's <c>event</c> field and the
/// <c>X-Serble-Event</c> header.
/// <para>
/// Slugs are part of the public contract — rename one and every subscribed app silently stops
/// receiving it, so new behaviour gets a new slug rather than a redefinition of an old one.
/// </para>
/// </summary>
public static class WebhookEventTypes {
    /// <summary>
    /// The app's own balance was charged by a tax cycle. Can reach any app: official apps are taxed
    /// on the same terms as everyone else.
    /// </summary>
    public const string TaxCollected = "tax.collected";

    /// <summary>
    /// The app received a tax distribution. Only ever sent to official apps below their configured
    /// target balance — they are the only recipients of a payout.
    /// </summary>
    public const string TaxPayout = "tax.payout";

    /// <summary>
    /// Sent on demand from the test endpoint. Delivered to the targeted subscription regardless of
    /// what it subscribes to, so an app can verify its endpoint and signature checking before any
    /// real event exists.
    /// </summary>
    public const string Test = "webhook.test";

    /// <summary>Every slug an app may put in a subscription. <see cref="Test"/> is deliverable but not subscribable.</summary>
    public static readonly IReadOnlyList<string> Subscribable = [TaxCollected, TaxPayout];

    public static bool IsSubscribable(string? eventType) =>
        eventType != null && Subscribable.Contains(eventType, StringComparer.Ordinal);

    /// <summary>
    /// The slugs that can actually reach one app. Every app is taxed, so <see cref="TaxCollected"/>
    /// is always on offer; <see cref="TaxPayout"/> is added only for official apps, since they are
    /// the sole recipients of a distribution and advertising it to anyone else would name an event
    /// that can never fire.
    /// <para>
    /// This narrows what is <i>advertised</i>, not what is accepted: subscribing to the other slug
    /// stays legal, so an app that is later promoted or demoted keeps working without a round trip
    /// to re-subscribe.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ForApp(bool isOfficial) =>
        isOfficial ? [TaxCollected, TaxPayout] : [TaxCollected];

    /// <summary>Splits a stored subscription list into its slugs, dropping blanks and duplicates.</summary>
    public static string[] Parse(string? stored) =>
        (stored ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Canonical stored form: known slugs only, deduplicated, in catalog order.</summary>
    public static string Join(IEnumerable<string> eventTypes) {
        HashSet<string> requested = eventTypes.Select(e => (e ?? "").Trim()).ToHashSet(StringComparer.Ordinal);
        return string.Join(',', Subscribable.Where(requested.Contains));
    }

    /// <summary>True if a subscription's stored list covers the given event type.</summary>
    public static bool Subscribes(string? stored, string eventType) =>
        Parse(stored).Contains(eventType, StringComparer.Ordinal);
}
