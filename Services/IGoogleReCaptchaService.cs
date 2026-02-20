using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

public interface IGoogleReCaptchaService {
    Task<GoogleReCaptchaResponse> VerifyReCaptcha(string token);
}
