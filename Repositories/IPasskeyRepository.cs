using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IPasskeyRepository {
    void CreatePasskey(SavedPasskey key);
    SavedPasskey[] GetUsersPasskeys(string userId);
    SavedPasskey? GetPasskey(byte[] credId);
    string? GetUserIdFromPasskeyId(byte[] credId);
    void SetPasskeySignCount(byte[] credId, int val);
    void DeletePasskey(byte[] credId);
    void UpdatePasskeyDevicePublicKeys(byte[] credId, byte[][] devicePublicKeys);
}
