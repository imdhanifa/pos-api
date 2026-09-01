using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PosSaas.Infrastructure.Security;

/// <summary>Decoded, validated claims from a token issued by <see cref="SimpleJwtService"/>.</summary>
public sealed record JwtClaims(Guid UserId, Guid TenantId, string Role);

/// <summary>
/// Hand-rolled HS256 JWT issue/validate, standing in for
/// Microsoft.AspNetCore.Authentication.JwtBearer (see README) - same three-segment
/// header.payload.signature shape and HMACSHA256 signing that package expects, so swapping
/// it in later is additive, not a token-format change. Used by
/// <see cref="PosSaas.Api.Auth.BearerAuthHandler"/> (Api project) to authenticate requests.
/// </summary>
public class SimpleJwtService
{
    private readonly byte[] _secret;
    private readonly TimeSpan _expiry;

    public SimpleJwtService(string secret, TimeSpan? expiry = null)
    {
        _secret = Encoding.UTF8.GetBytes(secret);
        _expiry = expiry ?? TimeSpan.FromHours(12);
    }

    public string IssueToken(Guid userId, Guid tenantId, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new
        {
            sub = userId.ToString(),
            tenantId = tenantId.ToString(),
            role,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(_expiry).ToUnixTimeSeconds()
        };

        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Sign($"{headerSegment}.{payloadSegment}");

        return $"{headerSegment}.{payloadSegment}.{signature}";
    }

    public JwtClaims? ValidateToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}");
        if (expectedSignature.Length != parts[2].Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedSignature), Encoding.ASCII.GetBytes(parts[2])))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = payload.RootElement;

            var exp = root.GetProperty("exp").GetInt64();
            if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow)
            {
                return null; // expired
            }

            var userId = Guid.Parse(root.GetProperty("sub").GetString()!);
            var tenantId = Guid.Parse(root.GetProperty("tenantId").GetString()!);
            var role = root.GetProperty("role").GetString()!;

            return new JwtClaims(userId, tenantId, role);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null; // malformed payload
        }
    }

    private string Sign(string data)
    {
        using var hmac = new HMACSHA256(_secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
