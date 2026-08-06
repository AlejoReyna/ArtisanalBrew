using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System.Security.Cryptography;
using ThisCafeteria.Web.Services.Blockchain;
using ThisCafeteria.Infrastructure.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaTransactionBuilderTests
{
    // Live devnet manifest (deployments/solana-devnet.json). Custody accounts are the vault PDA's
    // Token-2022 associated token accounts, so they are exact, real on-chain ATA-derivation vectors.
    private const string VaultPda = "2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB";
    private const string CafeMint = "C7g7g34QzvmAiP4HMmdjWLgfV9Y8FSF4GcAXK97HLQEg";
    private const string CoffeeMint = "7F5VXQaAQpyPMEqxHc3kWpeSPswuxcca3rBdkKypCiGX";
    private const string CafeCustody = "93JrssccHqhntjw2VR33jbrZfbgahznVB4hzffERAPCK";
    private const string CoffeeCustody = "FK9c4gmVUxnJcBkio5iBerE9JsiBYMb2y94pioxzPrba";
    private const string Token2022 = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";

    [Theory]
    [InlineData(VaultPda, CafeMint, CafeCustody)]
    [InlineData(VaultPda, CoffeeMint, CoffeeCustody)]
    public void DerivesTheSameAssociatedTokenAccountAsTheLiveDeployment(string owner, string mint, string expected)
    {
        var ata = SolanaTransactionBuilder.DeriveAssociatedTokenAccount(
            SolanaTransactionBuilder.DecodeKey(owner),
            SolanaTransactionBuilder.DecodeKey(mint),
            SolanaTransactionBuilder.DecodeKey(Token2022));

        SolanaTransactionBuilder.EncodeKey(ata).Should().Be(expected);
    }

    [Fact]
    public void OnCurveTestSeparatesRealKeysFromProgramDerivedAddresses()
    {
        // A createMint keypair is a real Ed25519 public key -> on curve.
        SolanaTransactionBuilder.IsOnCurve(SolanaTransactionBuilder.DecodeKey(CafeMint)).Should().BeTrue();
        // The vault is a program-derived address -> deliberately off curve.
        SolanaTransactionBuilder.IsOnCurve(SolanaTransactionBuilder.DecodeKey(VaultPda)).Should().BeFalse();
    }

    [Fact]
    public void MintToCheckedEncodesTagAmountAndDecimals()
    {
        var instruction = SolanaTransactionBuilder.MintToChecked(
            SolanaTransactionBuilder.DecodeKey(CafeMint),
            SolanaTransactionBuilder.DecodeKey(CafeCustody),
            SolanaTransactionBuilder.DecodeKey(VaultPda),
            SolanaTransactionBuilder.DecodeKey(Token2022),
            amount: 1_000_000_000UL,
            decimals: 9);

        instruction.Data[0].Should().Be(14);
        BitConverter.ToUInt64(instruction.Data, 1).Should().Be(1_000_000_000UL);
        instruction.Data[9].Should().Be(9);
        instruction.Accounts.Should().HaveCount(3);
        instruction.Accounts[2].IsSigner.Should().BeTrue(); // mint authority signs
        instruction.Accounts[0].IsWritable.Should().BeTrue();
        instruction.Accounts[1].IsWritable.Should().BeTrue();
    }

    [Fact]
    public void CompilesAMessageWithTheFeePayerFirstAndDeduplicatedAccounts()
    {
        var authority = RandomKey();
        var owner = RandomKey();
        var mint = SolanaTransactionBuilder.DecodeKey(CafeMint);
        var tokenProgram = SolanaTransactionBuilder.DecodeKey(Token2022);
        var ata = SolanaTransactionBuilder.DeriveAssociatedTokenAccount(owner, mint, tokenProgram);
        var blockhash = RandomKey();

        var create = SolanaTransactionBuilder.CreateAssociatedTokenAccountIdempotent(authority, ata, owner, mint, tokenProgram);
        var mintTo = SolanaTransactionBuilder.MintToChecked(mint, ata, authority, tokenProgram, 5UL, 9);
        var (message, keys) = SolanaTransactionBuilder.CompileMessage(authority, blockhash, new[] { create, mintTo });

        keys[0].Should().Equal(authority); // fee payer + signer first
        keys.Count(k => k.AsSpan().SequenceEqual(mint)).Should().Be(1); // mint appears once despite two instructions
        message[0].Should().Be(1); // exactly one required signature (the authority)
        message.Length.Should().BeGreaterThan(32);
    }

    [Fact]
    public void SignsWithEd25519AndTheSignatureVerifiesAgainstTheEmbeddedPublicKey()
    {
        var (secret64, publicKey) = GenerateKeypair();
        var message = RandomKey();

        var signature = SolanaTransactionBuilder.SignEd25519(message, secret64);
        SolanaTransactionBuilder.PublicKeyFromSecret(secret64).Should().Equal(publicKey);

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(message, 0, message.Length);
        verifier.VerifySignature(signature).Should().BeTrue();

        // The signed wire transaction places the 64-byte signature after the 1-byte count prefix.
        var wire = Convert.FromBase64String(SolanaTransactionBuilder.SignTransaction(message, secret64));
        wire[0].Should().Be(1);
        wire.AsSpan(1, 64).ToArray().Should().Equal(signature);
    }

    private static byte[] RandomKey() => RandomNumberGenerator.GetBytes(32);

    private static (byte[] Secret64, byte[] PublicKey) GenerateKeypair()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var secret64 = new byte[64];
        seed.CopyTo(secret64, 0);
        publicKey.CopyTo(secret64, 32);
        return (secret64, publicKey);
    }
}
