using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using System.Security.Cryptography;
using System.Text.Json;
using ThisCafeteria.Web.Services.Blockchain;
using ThisCafeteria.Infrastructure.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaFaucetSecretTests
{
    [Fact]
    public void FailsClosedWhenTheSecretIsAbsent()
    {
        SolanaFaucetSecret.TryLoad(null, "D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA", out _, out var error).Should().BeFalse();
        error.Should().Contain("not configured");
    }

    [Fact]
    public void LoadsAKeypairFromTheJsonByteArrayFormatWhenItMatchesTheAdministrator()
    {
        var (secret64, administrator) = GenerateKeypair();
        var json = JsonSerializer.Serialize(Array.ConvertAll(secret64, b => (int)b));

        SolanaFaucetSecret.TryLoad(json, administrator, out var loaded, out var error).Should().BeTrue();
        error.Should().BeNull();
        SolanaTransactionBuilder.EncodeKey(SolanaTransactionBuilder.PublicKeyFromSecret(loaded)).Should().Be(administrator);
    }

    [Fact]
    public void LoadsAKeypairFromBase58WhenItMatchesTheAdministrator()
    {
        var (secret64, administrator) = GenerateKeypair();
        var base58 = SolanaTransactionBuilder.EncodeKey(secret64);

        SolanaFaucetSecret.TryLoad(base58, administrator, out _, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void FailsClosedWhenTheKeyDoesNotMatchTheManifestAdministrator()
    {
        var (secret64, _) = GenerateKeypair();
        var json = JsonSerializer.Serialize(Array.ConvertAll(secret64, b => (int)b));

        SolanaFaucetSecret.TryLoad(json, "D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA", out _, out var error).Should().BeFalse();
        error.Should().Contain("does not match the manifest administrator");
    }

    [Fact]
    public void RejectsAKeyOfTheWrongLength()
    {
        var json = JsonSerializer.Serialize(new int[32]);
        SolanaFaucetSecret.TryLoad(json, "D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA", out _, out var error).Should().BeFalse();
        error.Should().Contain("64-byte");
    }

    private static (byte[] Secret64, string Administrator) GenerateKeypair()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var secret64 = new byte[64];
        seed.CopyTo(secret64, 0);
        publicKey.CopyTo(secret64, 32);
        return (secret64, SolanaTransactionBuilder.EncodeKey(publicKey));
    }
}
