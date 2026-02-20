using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

public interface IEmailConfirmationService {
    void SendConfirmationEmail(User user);
}
