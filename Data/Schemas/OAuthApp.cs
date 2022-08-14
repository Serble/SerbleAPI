namespace SerbleAPI.Data.Schemas; 

// DO NOT REMOVE SETTERS OR MAKE THEM PRIVATE, IT BREAKS THE JSON SERIALIZATION
public class OAuthApp {
    public string OwnerId { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ClientSecret { get; set; }

    public OAuthApp(string ownerId) {
        Name = "";
        Description = "";
        OwnerId = ownerId;
        Id = Guid.NewGuid().ToString();
        ClientSecret = Guid.NewGuid().ToString();
    }
    
    public OAuthApp CycleClientSecret() {
        ClientSecret = Guid.NewGuid().ToString();
        return this;
    }
    
}