using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IPasskeyRepository {
    /// <summary>Stores the passkey along with its credential row, setting <see cref="SavedPasskey.UserCredentialId"/>.</summary>
    Task CreatePasskey(SavedPasskey key);
    Task<SavedPasskey[]> GetUsersPasskeys(string userId);
    Task<SavedPasskey?> GetPasskey(byte[] credId);
    Task<string?> GetUserIdFromPasskeyId(byte[] credId);
    Task SetPasskeySignCount(byte[] credId, int val);
    Task UpdatePasskeyDevicePublicKeys(byte[] credId, byte[][] devicePublicKeys);
}
