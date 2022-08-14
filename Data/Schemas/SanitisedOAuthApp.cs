namespace SerbleAPI.Data.Schemas; 

// DO NOT REMOVE SETTERS OR MAKE THEM PRIVATE, IT BREAKS THE JSON SERIALIZATION
public class SanitisedOAuthApp {
    public string OwnerId { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public SanitisedOAuthApp(OAuthApp app) {
        OwnerId = app.OwnerId;
        Id = app.Id;
        Name = app.Name;
        Description = app.Description;
    }

}