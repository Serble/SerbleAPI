using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Oidc;

/// <summary>
/// Publishes the OIDC discovery document and JWKS at the well-known locations relying
/// parties (Jenkins, Jellyfin, Nextcloud, ...) probe. Both endpoints are anonymous and
/// only ever expose public information.
/// </summary>
[ApiController]
public class OidcDiscoveryController(IOptions<OidcSettings> settings, IOidcKeyService keys) : ControllerManager {

    private string Issuer => settings.Value.Issuer.TrimEnd('/');

    [HttpGet("/.well-known/openid-configuration")]
    [AllowAnonymous]
    public IActionResult Configuration() {
        return Json(new {
            issuer                                = Issuer,
            authorization_endpoint                = $"{Issuer}/api/v1/oauth/authorize",
            token_endpoint                        = $"{Issuer}/api/v1/oauth/token",
            userinfo_endpoint                     = $"{Issuer}/api/v1/oauth/userinfo",
            jwks_uri                              = $"{Issuer}/.well-known/jwks.json",
            scopes_supported                      = OidcScopes.All,
            response_types_supported              = new[] { "code" },
            response_modes_supported              = new[] { "query" },
            grant_types_supported                 = new[] { "authorization_code", "refresh_token" },
            subject_types_supported               = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post", "none" },
            code_challenge_methods_supported      = new[] { "S256" },
            claims_parameter_supported            = false,
            request_parameter_supported           = false,
            claims_supported = new[] {
                "sub", "iss", "aud", "exp", "iat", "auth_time", "nonce",
                "name", "preferred_username", "email", "email_verified", "groups"
            }
        });
    }

    [HttpGet("/.well-known/jwks.json")]
    [AllowAnonymous]
    public IActionResult Jwks() {
        IEnumerable<object> jwks = keys.GetPublicJsonWebKeySet().Keys.Select(k => new {
            kty = k.Kty,
            use = k.Use,
            alg = k.Alg,
            kid = k.Kid,
            n   = k.N,
            e   = k.E
        });
        return Json(new { keys = jwks });
    }
}
