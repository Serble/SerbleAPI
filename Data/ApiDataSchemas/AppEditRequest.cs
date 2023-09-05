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
                target.Name = NewValue;
                break;
            
            case "description":
                target.Description = NewValue;
                break;
            
            case "redirect_uri":
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
