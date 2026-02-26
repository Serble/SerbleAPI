namespace SerbleAPI.Repositories;

public interface IKvRepository {
    Task Set(string key, string value);
    Task<string?> Get(string key);
}
