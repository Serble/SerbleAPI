using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

public interface ITurnstileCaptchaService {
    Task<GoogleReCaptchaResponse> VerifyCaptcha(string token);
}
