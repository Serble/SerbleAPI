using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AntiSpamHeader {
    
    [FromHeader]
    // Either:
    // ReCaptcha | recaptcha TOKEN
    // Turnstile | turnstile TOKEN
    // Testing Bypass | bypass testing
    // Or be logged in with a verified email
    public string SerbleAntiSpam { get; set; } = null!;
}