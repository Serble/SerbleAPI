using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

public interface IAntiSpamService {
    public Task<bool> Check(AntiSpamHeader header, HttpContext context,
        SerbleAuthorizationHeaderType authType = SerbleAuthorizationHeaderType.Null, User? user = null);
}
