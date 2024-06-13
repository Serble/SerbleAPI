using Fido2NetLib.Objects;
using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage.MySQL;

public partial class MySqlStorageService {
    public void CreatePasskey(SavedPasskey key) {
        using MySqlCommand cmd = new("INSERT INTO serblesite_user_passkeys" +
                                     "(owner_id, credential_id, public_key, sign_count, aa_guid, attes_client_data_json, descriptor_type, descriptor_id, descriptor_transports, attes_format, transports, backup_eligible, backed_up, attes_object, device_public_keys) VALUES" +
                                     "(@owner_id, @credential_id, @public_key, @sign_count, @aa_guid, @attes_client_data_json, @descriptor_type, @descriptor_id, @descriptor_transports, @attes_format, @transports, @backup_eligible, @backed_up, @attes_object, @device_public_keys)", new MySqlConnection(_connectString));
        cmd.Parameters.AddWithValue("@owner_id", key.OwnerId);
        cmd.Parameters.AddWithValue("@credential_id", Convert.ToBase64String(key.CredentialId!));
        cmd.Parameters.AddWithValue("@public_key", Convert.ToBase64String(key.PublicKey!));
        cmd.Parameters.AddWithValue("@sign_count", key.SignCount);
        cmd.Parameters.AddWithValue("@aa_guid", key.AaGuid!.Value.ToString());
        cmd.Parameters.AddWithValue("@attes_client_data_json", Convert.ToBase64String(key.AttestationClientDataJson!));
        cmd.Parameters.AddWithValue("@descriptor_type", key.Descriptor!.Type.GetIndex());
        cmd.Parameters.AddWithValue("@descriptor_id", Convert.ToBase64String(key.Descriptor!.Id));
        cmd.Parameters.AddWithValue("@descriptor_transports", key.Descriptor!.Transports);
        cmd.Parameters.AddWithValue("@attes_format", key.AttestationFormat);
        cmd.Parameters.AddWithValue("@transports", key.Transports!.ToBitmask());
        cmd.Parameters.AddWithValue("@backup_eligible", key.IsBackupEligible);
        cmd.Parameters.AddWithValue("@backed_up", key.IsBackedUp);
        cmd.Parameters.AddWithValue("@attes_object", Convert.ToBase64String(key.AttestationObject!));
        cmd.Parameters.AddWithValue("@device_public_keys", key.DevicePublicKeys!.StringifyMda());
        cmd.ExecuteNonQuery();
    }

    public void GetUsersPasskeys(string userId, out SavedPasskey[] keys) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_user_passkeys WHERE ownerid=@id",
            new MySqlParameter("@id", userId));
        if (!reader.Read()) {
            keys = [];
            return;
        }
        
        List<SavedPasskey> keyList = [];
        do {
            keyList.Add(ReadPasskey(reader));
        } while (reader.Read());

        keys = keyList.ToArray();
    }

    private static SavedPasskey ReadPasskey(MySqlDataReader reader) {
        return new SavedPasskey {
            OwnerId = reader.GetString("owner_id"),
            CredentialId = Convert.FromBase64String(reader.GetString("credential_id")),
            PublicKey = Convert.FromBase64String(reader.GetString("public_key")),
            SignCount = reader.GetUInt32("sign_count"),
            AaGuid = Guid.Parse(reader.GetString("aa_guid")),
            AttestationClientDataJson = Convert.FromBase64String(reader.GetString("attes_client_data_json")),
            Descriptor = new PublicKeyCredentialDescriptor(
                SerbleUtils.EnumFromIndex<PublicKeyCredentialType>(reader.GetInt32("descriptor_type")), 
                Convert.FromBase64String(reader.GetString("descriptor_id")), 
                SerbleUtils.FromBitmask<AuthenticatorTransport>(reader.GetInt32("descriptor_transports"))),
            AttestationFormat = reader.GetString("attes_format"),
            Transports = SerbleUtils.FromBitmask<AuthenticatorTransport>(reader.GetInt32("transports")),
            IsBackupEligible = reader.GetBoolean("backup_eligible"),
            IsBackedUp = reader.GetBoolean("backed_up"),
            AttestationObject = Convert.FromBase64String(reader.GetString("attes_object")),
            DevicePublicKeys = reader.GetString("device_public_keys").ParseMda(Convert.FromBase64String)
        };
    }

    public void IncrementPasskeySignCount(string userId, byte[] credId) {
        throw new NotImplementedException();
    }

    public void GetUserIdFromCredentialId(byte[] credId, out string? userId) {
        throw new NotImplementedException();
    }
}