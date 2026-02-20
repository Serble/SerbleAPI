using System.Security.Cryptography;
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

    /// <summary>
    /// T must be an enum.
    /// </summary>
    /// <param name="e"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static int ToBitmask<T>(this T[] e) {
        return e.Aggregate(0, (current, en) => current | Convert.ToInt32(en));
    }

    public static T[] FromBitmask<T>(int mask) {
        return Enum.GetValues(typeof(T)).Cast<T>().Where(e => (mask & Convert.ToInt32(e)) != 0).ToArray();
    }

    public static int GetIndex(this Enum val) {
        return Array.IndexOf(Enum.GetValues(val.GetType()), val);
    }

    public static T EnumFromIndex<T>(int index) {
        return (T) Enum.GetValues(typeof(T)).GetValue(index)!;
    }

    public static string StringifyMda(this byte[][] arr) {
        StringBuilder sb = new();
        foreach (byte[] bytes in arr) {
            sb.Append(bytes.Base64Encode());
            sb.Append(',');
        }
        return sb.ToString();
    }
    
    public static byte[][] ParseMda(this string str, Func<string, byte[]> parse) {
        string[] split = str.Split(',');
        byte[][] arr = new byte[split.Length][];
        for (int i = 0; i < split.Length; i++) {
            arr[i] = parse(split[i]);
        }
        return arr;
    }
    
    // WARNING: The output of this function cannot change, otherwise passwords will break.
    public static string Sha256Hash(this string str) {
        StringBuilder builder = new();
        foreach (byte t in SHA256.HashData(Encoding.UTF8.GetBytes(str))) {
            builder.Append(t.ToString("x2"));
        }

        return builder.ToString();
    }
    
    public static bool IsNull(this object? obj) {
        return obj == null;
    }
    
    public static T ThrowIfNull<T>(this T? obj) where T : class {
        if (obj == null) {
            throw new Exception("Object is null");
        }
        return obj;
    }
}