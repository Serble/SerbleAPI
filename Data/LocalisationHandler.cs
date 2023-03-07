using GeneralPurposeLib;
using SerbleAPI.Data.Schemas;
using YamlDotNet.RepresentationModel;

namespace SerbleAPI.Data; 

public static class LocalisationHandler {

    private static string[]? _supportLanguages;
    private const string DefaultLanguage = "eng";
    private static Dictionary<string, Dictionary<string, string>>? _translations;

    public static Dictionary<string, string> GetTranslations(string? language) {
        LoadSupportedLanguages();  // This makes _translations not null so ignore nullable warning
        language = LanguageOrDefault(language);
        return _translations![language];
    }
    
    public static Dictionary<string, string> GetTranslations(User? user) {
        return GetTranslations(user?.Language);
    }

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
        _supportLanguages ??= File.ReadAllLines(Path.Combine("Translations", "supported-languages.txt"));

        if (_translations != null) {
            return;
        }
        
        // Load Translations
        _translations = new Dictionary<string, Dictionary<string, string>>();
        foreach (string lang in _supportLanguages) {
            string translationPath = Path.Combine("Translations", lang, "translations.yaml");
            if (!File.Exists(translationPath)) {
                Logger.Error($"Translation file for language {lang} does not exist");
                continue;
            }
            string translationYaml = File.ReadAllText(translationPath);
            StringReader reader = new(translationYaml);
            YamlStream yaml = new();
            yaml.Load(reader);
            YamlMappingNode? root = (YamlMappingNode) yaml.Documents[0].RootNode;
            if (root == null!) {
                Logger.Error($"Translation file for language {lang} is empty");
                continue;
            }
            Dictionary<string, string> translations = new();
            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children) {
                string key = entry.Key.ToString();
                string value = entry.Value.ToString();
                translations.Add(key, value);
            }
            _translations.Add(lang, translations);
        }
        
        // Add any keys in the default language but not in other languages to all languages
        if (!_translations.ContainsKey(DefaultLanguage)) {
            Logger.Error($"Default language {DefaultLanguage} does not exist");
            return;
        }
        Dictionary<string, string> defaultTranslations = _translations[DefaultLanguage];
        foreach (string lang in _supportLanguages) {
            if (lang == DefaultLanguage) continue;
            if (!_translations.ContainsKey(lang)) {
                Logger.Error($"Language {lang} does not exist");
                continue;
            }
            Dictionary<string, string> translations = _translations[lang];
            foreach (KeyValuePair<string, string> entry in defaultTranslations) {
                translations.TryAdd(entry.Key, entry.Value);
            }
        }
    }

}