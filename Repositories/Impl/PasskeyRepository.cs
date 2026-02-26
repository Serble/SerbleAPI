using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class PasskeyRepository(SerbleDbContext db) : IPasskeyRepository {

    private static SavedPasskey Map(DbUserPasskey r) => new() {
        OwnerId                   = r.OwnerId,
        Name                      = r.Name,
        CredentialId              = Convert.FromBase64String(r.CredentialId!),
        PublicKey                 = Convert.FromBase64String(r.PublicKey!),
        SignCount                 = (uint)(r.SignCount ?? 0),
        AaGuid                    = Guid.Parse(r.AaGuid!),
        AttestationClientDataJson = Convert.FromBase64String(r.AttesClientDataJson!),
        Descriptor = new PublicKeyCredentialDescriptor(
            SerbleUtils.EnumFromIndex<PublicKeyCredentialType>(r.DescriptorType ?? 0),
            Convert.FromBase64String(r.DescriptorId!),
            SerbleUtils.FromBitmask<AuthenticatorTransport>(r.DescriptorTransports ?? 0)),
        AttestationFormat = r.AttesFormat,
        Transports        = SerbleUtils.FromBitmask<AuthenticatorTransport>(r.Transports ?? 0),
        IsBackupEligible  = r.BackupEligible ?? false,
        IsBackedUp        = r.BackedUp       ?? false,
        AttestationObject = Convert.FromBase64String(r.AttesObject!),
        DevicePublicKeys  = string.IsNullOrEmpty(r.DevicePublicKeys)
            ? []
            : r.DevicePublicKeys.ParseMda(Convert.FromBase64String)
    };

    private static string CredId(byte[] credId) => Convert.ToBase64String(credId);

    public Task CreatePasskey(SavedPasskey key) {
        db.UserPasskeys.Add(new DbUserPasskey {
            OwnerId              = key.OwnerId,
            Name                 = key.Name,
            CredentialId         = Convert.ToBase64String(key.CredentialId!),
            PublicKey            = Convert.ToBase64String(key.PublicKey!),
            SignCount            = (int)key.SignCount,
            AaGuid               = key.AaGuid!.Value.ToString(),
            AttesClientDataJson  = Convert.ToBase64String(key.AttestationClientDataJson!),
            DescriptorType       = key.Descriptor!.Type.GetIndex(),
            DescriptorId         = Convert.ToBase64String(key.Descriptor.Id),
            DescriptorTransports = key.Descriptor.Transports?.ToBitmask() ?? 0,
            AttesFormat          = key.AttestationFormat,
            Transports           = key.Transports?.ToBitmask() ?? 0,
            BackupEligible       = key.IsBackupEligible,
            BackedUp             = key.IsBackedUp,
            AttesObject          = Convert.ToBase64String(key.AttestationObject!),
            DevicePublicKeys     = key.DevicePublicKeys == null || key.DevicePublicKeys.Length == 0
                ? "" : key.DevicePublicKeys.StringifyMda()
        });
        return db.SaveChangesAsync();
    }

    public async Task<SavedPasskey[]> GetUsersPasskeys(string userId) {
        DbUserPasskey[] vals = await db.UserPasskeys
            .Where(p => p.OwnerId == userId)
            .ToArrayAsync();
        return vals.Select(Map).ToArray();
    }

    public async Task<SavedPasskey?> GetPasskey(byte[] credId) {
        string id = CredId(credId);
        DbUserPasskey? row = await db.UserPasskeys.FirstOrDefaultAsync(p => p.CredentialId == id);
        return row == null ? null : Map(row);
    }

    public async Task<string?> GetUserIdFromPasskeyId(byte[] credId) {
        string id = CredId(credId);
        return await db.UserPasskeys
            .Where(p => p.CredentialId == id)
            .Select(p => p.OwnerId)
            .FirstOrDefaultAsync();
    }

    public async Task SetPasskeySignCount(byte[] credId, int val) {
        string id = CredId(credId);
        DbUserPasskey? row = await db.UserPasskeys.FirstOrDefaultAsync(p => p.CredentialId == id);
        if (row == null) return;
        row.SignCount = val;
        await db.SaveChangesAsync();
    }

    public async Task DeletePasskey(byte[] credId) {
        string id = CredId(credId);
        DbUserPasskey? row = await db.UserPasskeys.FirstOrDefaultAsync(p => p.CredentialId == id);
        if (row == null) return;
        db.UserPasskeys.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task UpdatePasskeyDevicePublicKeys(byte[] credId, byte[][] devicePublicKeys) {
        string id = CredId(credId);
        DbUserPasskey? row = await db.UserPasskeys.FirstOrDefaultAsync(p => p.CredentialId == id);
        if (row == null) return;
        row.DevicePublicKeys = devicePublicKeys.StringifyMda();
        await db.SaveChangesAsync();
    }
}
