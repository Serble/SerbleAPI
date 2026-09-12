using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AppEditRequest {
    public string Field { get; set; }
    public string NewValue { get; set; }
    
    public AppEditRequest(string field, string newValue) {
        Field = field;
        NewValue = newValue;
    }

    public OAuthApp ApplyChanges(OAuthApp target) {
        switch (Field.ToLower()) {
            case "name":
                if (NewValue == "") {
                    throw new ArgumentException("Name cannot be empty");
                }
                if (!AppRules.TryValidateName(NewValue, out string? nameError)) {
                    throw new ArgumentException(nameError);
                }
                target.Name = NewValue;
                break;
            
            case "description":
                if (!AppRules.TryValidateDescription(NewValue, out string? descriptionError)) {
                    throw new ArgumentException(descriptionError);
                }
                target.Description = NewValue;
                break;
            
            case "redirect_uri":
                // Checked on the way in rather than at redirect time: the field is a ';'-separated
                // list, so an unchecked write here appends a target the authorize endpoint will
                // then treat as registered.
                if (!RedirectUriRules.TryValidateField(NewValue, out string? redirectUriError)) {
                    throw new ArgumentException(redirectUriError);
                }
                target.RedirectUri = NewValue;
                break;

            default:
                throw new ArgumentException("Field doesn't exist");
        }
        return target;
    }
    
    public bool TryApplyChanges(OAuthApp target, out OAuthApp newUser, out string msg) {
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
