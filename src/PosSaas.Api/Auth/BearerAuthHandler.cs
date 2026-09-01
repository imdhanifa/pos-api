using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PosSaas.Infrastructure.Security;

namespace PosSaas.Api.Auth;

/// <summary>
/// Hand-rolled Bearer scheme standing in for Microsoft.AspNetCore.Authentication.JwtBearer
/// (see README) - validates the token via <see cref="SimpleJwtService"/> and maps its claims
/// onto a <see cref="ClaimsPrincipal"/> the same shape JwtBearer would produce, so swapping
/// that package in later touches only Program.cs, not any controller.
/// </summary>
public class BearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";

    private readonly SimpleJwtService _jwtService;

    public BearerAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SimpleJwtService jwtService)
        : base(options, logger, encoder)
    {
        _jwtService = jwtService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = value["Bearer ".Length..].Trim();
            }
        }

        // SignalR's WebSocket/SSE transports can't set custom headers, so the JS/RN client
        // (mobile/src/notifications/NotificationsHub.ts's accessTokenFactory) sends the token as
        // ?access_token=... instead - restricted to /hubs so a token can't leak into every URL's
        // query string/access logs via this path. See Microsoft's documented SignalR JWT pattern.
        if (token is null && Request.Path.StartsWithSegments("/hubs") && Request.Query.TryGetValue("access_token", out var queryToken))
        {
            token = queryToken.ToString();
        }

        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = _jwtService.ValidateToken(token);
        if (claims is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token"));
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString()),
            new Claim("tenantId", claims.TenantId.ToString()),
            new Claim(ClaimTypes.Role, claims.Role)
        }, SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
