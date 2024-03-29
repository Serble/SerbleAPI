namespace SerbleAPI.Data; 

public static class EmailSchemasService {

    private static readonly Dictionary<string, string> LoadedSchemas = new();

    public static string GetEmailSchema(EmailSchema schema, string language = "en") {
        language = LocalisationHandler.LanguageOrDefault(language);
        return schema switch {
            EmailSchema.ConfirmationEmail => GetEmailSchemaFromFile("email_confirm.html", language),
            EmailSchema.AccountDeleted => GetEmailSchemaFromFile("account_deleted.html", language),
            EmailSchema.EmailChanged => GetEmailSchemaFromFile("email_changed.html", language),
            EmailSchema.PurchaseReceipt => GetEmailSchemaFromFile("purchase_receipt.html", language),
            EmailSchema.SubscriptionEnded => GetEmailSchemaFromFile("subscription_ended.html", language),
            EmailSchema.FreeTrialEnding => GetEmailSchemaFromFile("free_trial_ending.html", language),
            _ => throw new ArgumentException("Invalid schema")
        };
    }

    private static string GetEmailSchemaFromFile(string file, string language) {
        if (LoadedSchemas.TryGetValue(file, out string? fromFile)) {
            return fromFile;
        }
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Translations", language, file);
        string schema = File.ReadAllText(path);
        LoadedSchemas.Add(file, schema);
        return schema;
    }

}

public enum EmailSchema {
    ConfirmationEmail,
    AccountDeleted,
    EmailChanged,
    PurchaseReceipt,
    SubscriptionEnded,
    FreeTrialEnding
}