namespace SerbleAPI.Data.Schemas; 

// DO NOT REMOVE SETTERS OR MAKE THEM PRIVATE, IT BREAKS THE JSON SERIALIZATION
public class NewOAuthApp {
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
}