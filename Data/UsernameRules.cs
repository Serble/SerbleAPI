namespace SerbleAPI.Data;

/// <summary>
/// The stored width of a username, shared by the register and edit routes so the two cannot
/// disagree about what fits.
/// <para>
/// This is not a judgement about what makes a good name -- whatever the column accepts is
/// accepted. It exists only so an over-long name is answered with a 400 instead of failing the
/// insert and surfacing as a 500.
/// </para>
/// </summary>
public static class UsernameRules {
    /// <summary>
    /// The width of the Username column. Counted in UTF-16 units, which is never fewer than the
    /// characters MySQL counts, so a name that passes here always fits.
    /// </summary>
    public const int MaxLength = 255;

    /// <summary>
    /// <paramref name="error"/> is null when the name is storable, and otherwise carries a message
    /// meant for the caller.
    /// </summary>
    public static bool TryValidate(string? raw, out string? error) {
        error = null;

        if (raw is { Length: > MaxLength }) {
            error = $"Username cannot be longer than {MaxLength} characters";
            return false;
        }
        return true;
    }
}
