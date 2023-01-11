using System.Text;

namespace SerbleAPI.Data; 

public static class SerbleUtils {
    public static string Base64Encode(string plainText) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        
    public static string Base64Decode(string base64EncodedData) => 
        Encoding.UTF8.GetString(Convert.FromBase64String(base64EncodedData));
    
    public static IEnumerable<T> ToSingleItemEnumerable<T>(this T item) {
        yield return item;
    }

}