namespace SerbleAPI.Data;

/// <summary>
/// The stored widths of the app fields an owner supplies, shared by the create and edit routes so
/// the two cannot disagree about what fits.
/// <para>
/// These only keep an over-long value from failing the insert and surfacing as a 500; whatever the
/// columns accept is accepted.
/// </para>
/// </summary>
public static class AppRules {
    /// <summary>The width of the Name column.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The width of the Description column.</summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>
    /// <paramref name="error"/> is null when the name is storable, and otherwise carries a message
    /// meant for the caller.
    /// </summary>
    public static bool TryValidateName(string? raw, out string? error) {
        error = null;

        if (raw is { Length: > MaxNameLength }) {
            error = $"Name cannot be longer than {MaxNameLength} characters";
            return false;
        }
        return true;
    }

    /// <summary>
    /// <paramref name="error"/> is null when the description is storable, and otherwise carries a
    /// message meant for the caller.
    /// </summary>
    public static bool TryValidateDescription(string? raw, out string? error) {
        error = null;

        if (raw is { Length: > MaxDescriptionLength }) {
            error = $"Description cannot be longer than {MaxDescriptionLength} characters";
            return false;
        }
        return true;
    }
}
