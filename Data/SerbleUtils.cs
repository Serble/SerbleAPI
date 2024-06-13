using System.Text;

namespace SerbleAPI.Data; 

public static class SerbleUtils {
    private static Random _random = new Random();
    
    public static string Base64Encode(string plainText) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));

    public static string Base64Encode(this byte[] data) =>
        Convert.ToBase64String(data);
        
    public static string Base64Decode(string base64EncodedData) => 
        Encoding.UTF8.GetString(Convert.FromBase64String(base64EncodedData));
    
    public static IEnumerable<T> ToSingleItemEnumerable<T>(this T item) {
        yield return item;
    }
    
    public static string RandomString(int length) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[_random.Next(s.Length)]).ToArray());
    }

}