namespace SerbleAPI.Data; 

public static class EmailSchemasService {

    private static readonly Dictionary<string, string> LoadedSchemas = new();

    public static string GetEmailSchema(EmailSchema schema) {
        return schema switch {
            EmailSchema.ConfirmationEmail => GetEmailSchemaFromFile("email_confirm.html"),
            _ => throw new ArgumentException("Invalid schema")
        };
    }

    private static string GetEmailSchemaFromFile(string file) {
        if (LoadedSchemas.ContainsKey(file)) {
            return LoadedSchemas[file];
        }
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "EmailSchemas", file);
        string schema = File.ReadAllText(path);
        LoadedSchemas.Add(file, schema);
        return schema;
    }

}

public enum EmailSchema {
    ConfirmationEmail
}