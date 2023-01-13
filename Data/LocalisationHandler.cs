using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data; 

public static class LocalisationHandler {

    private static string[]? _supportLanguages;
    private const string DefaultLanguage = "en";

    public static string GetPreferredLanguage(HttpRequest request) {
        LoadSupportedLanguages();
        
        if (request.Headers.ContainsKey("Content-Language") && _supportLanguages!.Contains(request.Headers["Content-Language"].First())) {
            return request.Headers["Content-Language"];
        }

        if (!request.Headers.ContainsKey("Accept-Language")) return DefaultLanguage;
        string[] acceptLanguages = request.Headers["Accept-Language"].First().Split(',');
        foreach (string acceptLanguage in acceptLanguages) {
            if (_supportLanguages!.Contains(acceptLanguage)) {
                return acceptLanguage;
            }
        }

        return DefaultLanguage;
    }

    public static string LanguageOrDefault(string? lang) {
        LoadSupportedLanguages();
        if (lang == null) {
            return DefaultLanguage;
        }
        return _supportLanguages!.Contains(lang) ? lang : DefaultLanguage;
    }
    
    public static string LanguageOrDefault(User? usr) {
        LoadSupportedLanguages();
        return usr == null ? DefaultLanguage : LanguageOrDefault(usr.Language);
    }
    
    private static void LoadSupportedLanguages() {
        _supportLanguages ??= File.ReadAllLines(Path.Combine("Data", "EmailSchemas", "supported-languages.txt"));
    }

}