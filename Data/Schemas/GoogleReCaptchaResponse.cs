using Newtonsoft.Json;

namespace SerbleAPI.Data.Schemas; 

public class GoogleReCaptchaResponse {
    
    [JsonProperty("success")]
    public bool Success { get; set; }
    
    [JsonProperty("score")]
    public double Score { get; set; }
    
    [JsonProperty("action")]
    public string Action { get; set; } = null!;

    [JsonProperty("challenge_ts")]
    public string ChallengeTs { get; set; } = null!;

    [JsonProperty("hostname")]
    public string Hostname { get; set; } = null!;

    [JsonProperty("error-codes")]
    public string[] ErrorCodes { get; set; } = null!;

    public GoogleReCaptchaResponse(bool success) {
        Success = success;
        Score = 0;
        Action = "";
        ChallengeTs = "";
        Hostname = "";
        ErrorCodes = Array.Empty<string>();
    }
    
    public GoogleReCaptchaResponse(double score, string action, string challengeTs, string hostname, string[] errorCodes) {
        Score = score;
        Action = action;
        ChallengeTs = challengeTs;
        Hostname = hostname;
        ErrorCodes = errorCodes;
    }

    public GoogleReCaptchaResponse() { }
}