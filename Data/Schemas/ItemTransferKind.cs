namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Why an item changed hands, recorded on each <see cref="ItemTransaction"/>. Stored as an int so
/// additional kinds can be appended without breaking existing rows.
/// </summary>
public enum ItemTransferKind {
    /// <summary>Genesis record: the item was minted by its creator app.</summary>
    Created = 0,
    /// <summary>The item moved between owners via an approved trade proposal.</summary>
    Trade = 1
}
