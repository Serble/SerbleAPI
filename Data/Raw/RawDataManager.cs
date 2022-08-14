using GeneralPurposeLib;

namespace SerbleAPI.Data.Raw; 

public static class RawDataManager {
    
    public static string[] EnglishWords;
    
    public static void LoadRawData() {
        Logger.Info("Loading words.txt");
        EnglishWords = File.ReadAllLines("Data/Raw/words.txt");
    }
    
}