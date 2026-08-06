using ThisCafeteria.Application.Services.Wallet;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Web.Services.Wallet;
using ThisCafeteria.Web.Models;

namespace ThisCafeteria.IntegrationTests;

public sealed class SolanaWalletChallengeTests(ThisCafeteriaWebApplicationFactory factory) : IClassFixture<ThisCafeteriaWebApplicationFactory>
{
    [Fact]
    public async Task CompletesTheApplicationWalletStandardChallengeAndLoginScenario()
    {
        var client = factory.CreateClient();
        var privateKey = new Ed25519PrivateKeyParameters(RandomNumberGenerator.GetBytes(32), 0);
        var publicKey = Base58Encode(privateKey.GeneratePublicKey().GetEncoded());
        var challengeResponse = await client.PostAsJsonAsync("/api/wallet-auth/solana/challenge",
            new SolanaWalletChallengeRequest(publicKey, "solana-localnet", "Integration wallet"));
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<SolanaWalletChallengeResponse>();
        challenge.Should().NotBeNull();

        var verificationResponse = await client.PostAsJsonAsync("/api/wallet-auth/solana/verify",
            new SolanaWalletVerifyRequest(publicKey, Sign(privateKey, challenge!.Message), challenge.Message, challenge.Nonce, challenge.ChainKey, "Integration wallet"));

        verificationResponse.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await verificationResponse.Content.ReadAsStringAsync());
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("address").GetString().Should().Be(publicKey);
    }

    [Fact]
    public async Task RejectsBindingMismatchAndAtomicallyAllowsOnlyOneConcurrentConsumption()
    {
        _ = factory.CreateClient();
        var privateKey = new Ed25519PrivateKeyParameters(RandomNumberGenerator.GetBytes(32), 0);
        var publicKey = Base58Encode(privateKey.GeneratePublicKey().GetEncoded());
        var chain = new ChainDefinition { Key = "solana-localnet", Family = ChainFamily.Solana, SolanaCluster = "localnet" };
        SolanaWalletChallenge challenge;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            challenge = await scope.ServiceProvider.GetRequiredService<ISolanaWalletChallengeService>()
                .CreateAsync(publicKey, chain, "https://cafe.local", CancellationToken.None);
        }
        var signature = Sign(privateKey, challenge.Message);

        await using (var invalidSignatureScope = factory.Services.CreateAsyncScope())
        {
            var invalidSignature = await invalidSignatureScope.ServiceProvider.GetRequiredService<ISolanaWalletChallengeService>()
                .VerifyAsync(publicKey, challenge.Message, challenge.Nonce, chain.Key, "https://cafe.local", Base58Encode(new byte[64]), CancellationToken.None);
            invalidSignature.Should().Be(SolanaChallengeVerificationError.InvalidSignature);
        }

        await using (var mismatchScope = factory.Services.CreateAsyncScope())
        {
            var mismatch = await mismatchScope.ServiceProvider.GetRequiredService<ISolanaWalletChallengeService>()
                .VerifyAsync(publicKey, challenge.Message, challenge.Nonce, chain.Key, "https://attacker.local", signature, CancellationToken.None);
            mismatch.Should().Be(SolanaChallengeVerificationError.Mismatch);
        }

        async Task<SolanaChallengeVerificationError> ConsumeAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISolanaWalletChallengeService>()
                .VerifyAsync(publicKey, challenge.Message, challenge.Nonce, chain.Key, "https://cafe.local", signature, CancellationToken.None);
        }

        var results = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());

        results.Should().ContainSingle(result => result == SolanaChallengeVerificationError.None);
        results.Should().ContainSingle(result => result == SolanaChallengeVerificationError.Expired);
    }

    private static string Sign(Ed25519PrivateKeyParameters privateKey, string message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var bytes = Encoding.UTF8.GetBytes(message);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Base58Encode(signer.GenerateSignature());
    }

    private static string Base58Encode(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var result = new StringBuilder();
        while (value > 0) { value = BigInteger.DivRem(value, 58, out var remainder); result.Insert(0, alphabet[(int)remainder]); }
        foreach (var item in bytes) { if (item != 0) break; result.Insert(0, '1'); }
        return result.Length == 0 ? "1" : result.ToString();
    }
}
