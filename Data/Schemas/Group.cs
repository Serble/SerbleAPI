namespace SerbleAPI.Data.Schemas;

public class Group {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public Group() {
        Id = Guid.NewGuid().ToString();
        Name = "";
        Description = "";
    }
}
