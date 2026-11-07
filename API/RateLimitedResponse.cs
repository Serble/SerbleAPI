using System.Text.Json.Serialization;

namespace SerbleAPI.API;

/// <summary>
/// The body of every 429, whether the middleware or a handler refused. One shape means a client
/// can read the wait from any refusal, not just the ones the middleware wrote.
/// </summary>
public sealed record RateLimitedResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retry_after")] int RetryAfter) {

    public static int Seconds(TimeSpan retryAfter) => Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

    public static RateLimitedResponse For(TimeSpan retryAfter) {
        int seconds = Seconds(retryAfter);
        return new RateLimitedResponse("too_many_requests", $"Too many requests. Retry in {seconds} seconds.", seconds);
    }
}
