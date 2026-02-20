using Fido2NetLib.Objects;

namespace SerbleAPI.Data.Schemas;

public class SavedPasskey {
    public string OwnerId = null!;
    public string? Name;
    public byte[]? CredentialId;
    public byte[]? PublicKey;
    public uint SignCount = 0;
    public Guid? AaGuid;
    public byte[]? AttestationClientDataJson;
    public PublicKeyCredentialDescriptor? Descriptor;
    public string? AttestationFormat;
    public AuthenticatorTransport[]? Transports;
    public bool IsBackupEligible;
    public bool IsBackedUp;
    public byte[]? AttestationObject;
    public byte[][]? DevicePublicKeys;
}