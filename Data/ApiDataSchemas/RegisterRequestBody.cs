namespace SerbleAPI.Data.ApiDataSchemas; 

public class RegisterRequestBody {
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}