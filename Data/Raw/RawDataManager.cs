using GeneralPurposeLib;
using Newtonsoft.Json;

namespace SerbleAPI.Data.Raw; 

public static class RawDataManager {
    
    public static string[] EnglishWords = null!;
    
    public static void LoadRawData() {
        Logger.Info("Loading words.txt");
        string jsonArray = File.ReadAllText("Data/Raw/words.txt");
        EnglishWords = JsonConvert.DeserializeObject<string[]>(jsonArray)!;
    }
    
}