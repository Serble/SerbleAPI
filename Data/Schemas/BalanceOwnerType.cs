namespace SerbleAPI.Data.Schemas;

/// <summary>
/// The kind of entity that owns a <see cref="Balance"/>. Stored as an int in the
/// <c>Balances</c> table so additional owner kinds can be appended without breaking
/// existing rows.
/// </summary>
public enum BalanceOwnerType {
    User = 0,
    App = 1
}
