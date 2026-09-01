using PosSaas.Infrastructure.Security;
using Xunit;

namespace PosSaas.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashThenVerify_RoundTripsSucceeds()
    {
        var hash = PasswordHasher.Hash("Demo@123");

        Assert.True(PasswordHasher.Verify("Demo@123", hash));
    }

    [Fact]
    public void Verify_FailsForWrongPassword()
    {
        var hash = PasswordHasher.Hash("Demo@123");

        Assert.False(PasswordHasher.Verify("NotTheRightPassword", hash));
    }

    [Fact]
    public void Hash_IsSalted_SoTwoHashesOfSamePasswordDiffer()
    {
        var hash1 = PasswordHasher.Hash("Demo@123");
        var hash2 = PasswordHasher.Hash("Demo@123");

        Assert.NotEqual(hash1, hash2);
        // ...but both still verify correctly, proving the difference is only the salt.
        Assert.True(PasswordHasher.Verify("Demo@123", hash1));
        Assert.True(PasswordHasher.Verify("Demo@123", hash2));
    }
}
