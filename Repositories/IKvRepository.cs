namespace SerbleAPI.Repositories;

public interface IKvRepository {
    void Set(string key, string value);
    string? Get(string key);
}
