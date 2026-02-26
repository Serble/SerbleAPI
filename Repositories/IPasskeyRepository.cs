using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IPasskeyRepository {
    Task CreatePasskey(SavedPasskey key);
    Task<SavedPasskey[]> GetUsersPasskeys(string userId);
    Task<SavedPasskey?> GetPasskey(byte[] credId);
    Task<string?> GetUserIdFromPasskeyId(byte[] credId);
    Task SetPasskeySignCount(byte[] credId, int val);
    Task DeletePasskey(byte[] credId);
    Task UpdatePasskeyDevicePublicKeys(byte[] credId, byte[][] devicePublicKeys);
}
