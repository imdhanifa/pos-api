using PosSaas.Infrastructure.Security;
using Xunit;

namespace PosSaas.Tests;

public class SimpleJwtServiceTests
{
    private const string Secret = "test-only-secret-at-least-32-characters-long";

    [Fact]
    public void IssuedToken_ValidatesAndRoundTripsClaims()
    {
        var service = new SimpleJwtService(Secret);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = service.IssueToken(userId, tenantId, "Owner");
        var claims = service.ValidateToken(token);

        Assert.NotNull(claims);
        Assert.Equal(userId, claims!.UserId);
        Assert.Equal(tenantId, claims.TenantId);
        Assert.Equal("Owner", claims.Role);
    }

    [Fact]
    public void TamperedSignature_FailsValidation()
    {
        var service = new SimpleJwtService(Secret);
        var token = service.IssueToken(Guid.NewGuid(), Guid.NewGuid(), "Cashier");

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var signature = parts[2];
        // Flip the first character of the signature segment to something guaranteed
        // different, so the recomputed HMAC can never match it.
        var flippedChar = signature[0] == 'A' ? 'B' : 'A';
        var tamperedSignature = flippedChar + signature.Substring(1);
        var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        Assert.Null(service.ValidateToken(tamperedToken));
    }

    [Fact]
    public void ExpiredToken_FailsValidation()
    {
        var service = new SimpleJwtService(Secret, TimeSpan.FromSeconds(-1));
        var token = service.IssueToken(Guid.NewGuid(), Guid.NewGuid(), "Owner");

        Assert.Null(service.ValidateToken(token));
    }
}
