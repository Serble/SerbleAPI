using System.Text.RegularExpressions;
using GeneralPurposeLib;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AccountEditRequest {
    public string Field { get; set; }
    public string NewValue { get; set; }
    
    public AccountEditRequest(string field, string newValue) {
        Field = field;
        NewValue = newValue;
    }

    private User ApplyChanges(User target) {
        switch (Field.ToLower()) {
            case "username":
                // Check if username is taken
                if (NewValue == "") {
                    throw new ArgumentException("Username cannot be empty");
                }
                Program.StorageService!.GetUserFromName(NewValue, out User? existingUser);
                if (existingUser != null) {
                    throw new ArgumentException("Username is already taken");
                }
                target.Username = NewValue;
                break;
            
            case "password":
                if (NewValue.Length > 256) {
                    throw new ArgumentException("Password cannot be longer than 256 characters");
                }

                target.PasswordSalt ??= SerbleUtils.RandomString(64);
                target.PasswordHash = (NewValue + target.PasswordSalt).Sha256Hash();
                break;
            
            case "email":
                if (!Regex.IsMatch(NewValue, @"(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*|""(?:[\x01-\x08\x0b\x0c\x0e-\x1f\x21\x23-\x5b\x5d-\x7f]|\\[\x01-\x09\x0b\x0c\x0e-\x7f])*"")@(?:(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?|\[(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?|[a-z0-9-]*[a-z0-9]:(?:[\x01-\x08\x0b\x0c\x0e-\x1f\x21-\x5a\x53-\x7f]|\\[\x01-\x09\x0b\x0c\x0e-\x7f])+)\])")) {
                    Logger.Debug("Email is not valid: " + NewValue);
                    throw new ArgumentException("Invalid email");
                }
                target.Email = NewValue;
                break;
            
            case "language":  // LAN-RG (Language-Region region is optional)
                if (!Regex.IsMatch(NewValue, @"^[a-z]{3}(-[A-Z]{2})?$")) {
                    throw new ArgumentException("Invalid language");
                }
                target.Language = NewValue;
                break;
            
            case "totpenabled":
                switch (NewValue.ToLower()) {
                    case "true":
                        target.TotpEnabled = true;
                        target.TotpSecret ??= SerbleUtils.RandomString(128);
                        break;
                    case "false":
                        target.TotpEnabled = false;
                        target.TotpSecret = null;
                        break;
                    default:
                        throw new ArgumentException("Invalid value for ToptEnabled");
                }

                break;

            default:
                throw new ArgumentException("Field doesn't exist");
        }
        return target;
    }
    
    public bool TryApplyChanges(User target, out User newUser, out string msg) {
        try {
            newUser = ApplyChanges(target);
            msg = "Success";
            return true;
        } catch (ArgumentException e) {
            msg = e.Message;
            newUser = target;
            return false;
        }
    }
    
}
