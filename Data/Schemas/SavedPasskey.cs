namespace SerbleAPI.Data.Schemas;

public class SavedPasskey(string ownerId, byte[] credentialId, byte[] publicKey, int signCount, byte[] aaGuid) {
    public string OwnerId = ownerId;
    public byte[] CredentialId = credentialId;
    public byte[] PublicKey = publicKey;
    public int SignCount = signCount;
    public byte[] AaGuid = aaGuid;
}