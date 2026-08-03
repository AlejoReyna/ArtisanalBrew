using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace ThisCafeteria.Web.Services.Blockchain;

/// <summary>
/// Minimal, dependency-free builder for the one Solana transaction the CAFE devnet faucet needs:
/// create the claimant's associated token account (idempotent) and mint CAFE to it under the mint
/// authority. There is no Solnet in this repo, so message serialization, program-address derivation,
/// and Ed25519 signing are implemented here against the canonical formats and validated by unit tests
/// (deterministic ATA vectors drawn from the live devnet manifest) plus a read-only
/// <c>simulateTransaction</c> round-trip. Nothing here holds or logs a secret key; the signer bytes are
/// supplied by the caller from an environment variable and used only to produce a signature.
/// </summary>
public static class SolanaTransactionBuilder
{
    // Canonical program IDs (base58). Token-2022 is passed in from the manifest, but these are fixed.
    public const string SystemProgram = "11111111111111111111111111111111";
    public const string AssociatedTokenProgram = "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";

    // Ed25519 field prime p = 2^255 - 19 and curve constant d = -121665/121666 (mod p), for the
    // on-curve test that find_program_address uses to reject bumps landing on a real curve point.
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger D = BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");

    public sealed record AccountMeta(byte[] PublicKey, bool IsSigner, bool IsWritable);

    public sealed record Instruction(byte[] ProgramId, IReadOnlyList<AccountMeta> Accounts, byte[] Data);

    /// <summary>
    /// Derives the canonical associated token account address for <paramref name="owner"/> +
    /// <paramref name="mint"/> under <paramref name="tokenProgram"/>, matching
    /// <c>getAssociatedTokenAddressSync</c>. Seeds are [owner, tokenProgram, mint].
    /// </summary>
    public static byte[] DeriveAssociatedTokenAccount(byte[] owner, byte[] mint, byte[] tokenProgram)
    {
        var associatedTokenProgram = DecodeKey(AssociatedTokenProgram);
        return FindProgramAddress(new[] { owner, tokenProgram, mint }, associatedTokenProgram).Address;
    }

    /// <summary>
    /// Solana's find_program_address: the highest bump (255 downward) whose SHA-256 of
    /// seeds || bump || programId || "ProgramDerivedAddress" is NOT a valid Ed25519 curve point.
    /// </summary>
    public static (byte[] Address, byte Bump) FindProgramAddress(IReadOnlyList<byte[]> seeds, byte[] programId)
    {
        for (var bump = 255; bump >= 0; bump--)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var seed in seeds) hash.AppendData(seed);
            hash.AppendData(new[] { (byte)bump });
            hash.AppendData(programId);
            hash.AppendData(Encoding.ASCII.GetBytes("ProgramDerivedAddress"));
            var candidate = hash.GetHashAndReset();
            if (!IsOnCurve(candidate)) return (candidate, (byte)bump);
        }
        throw new InvalidOperationException("Unable to find a program address off the Ed25519 curve.");
    }

    /// <summary>
    /// RFC 8032 point decompression test: a 32-byte little-endian value is on the Ed25519 curve when a
    /// valid x can be recovered for its y coordinate. Used to reject on-curve PDA candidates.
    /// </summary>
    public static bool IsOnCurve(byte[] compressed)
    {
        if (compressed.Length != 32) return false;
        var data = (byte[])compressed.Clone();
        var sign = (data[31] & 0x80) != 0;
        data[31] &= 0x7f;
        var y = new BigInteger(data, isUnsigned: true, isBigEndian: false);
        if (y >= P) return false;
        var y2 = y * y % P;
        var u = Mod(y2 - 1);
        var v = Mod(D * y2 + 1);
        // x = (u/v)^((p+3)/8), then fix up by sqrt(-1) as needed.
        var uv3 = u * ModPow(v, 3) % P;
        var uv7 = u * ModPow(v, 7) % P;
        var x = uv3 * ModPow(uv7, (P - 5) / 8) % P;
        var vxx = v * x % P * x % P;
        if (Mod(vxx - u) == 0)
        {
            // x is a valid root.
        }
        else if (Mod(vxx + u) == 0)
        {
            x = x * ModPow(2, (P - 1) / 4) % P; // multiply by sqrt(-1)
        }
        else
        {
            return false; // no square root: not on the curve
        }
        if (x == 0 && sign) return false; // -0 is invalid
        return true;
    }

    /// <summary>SPL associated-token create-idempotent instruction (data byte 1).</summary>
    public static Instruction CreateAssociatedTokenAccountIdempotent(byte[] payer, byte[] ata, byte[] owner, byte[] mint, byte[] tokenProgram)
    {
        var accounts = new List<AccountMeta>
        {
            new(payer, IsSigner: true, IsWritable: true),
            new(ata, IsSigner: false, IsWritable: true),
            new(owner, IsSigner: false, IsWritable: false),
            new(mint, IsSigner: false, IsWritable: false),
            new(DecodeKey(SystemProgram), IsSigner: false, IsWritable: false),
            new(tokenProgram, IsSigner: false, IsWritable: false)
        };
        return new Instruction(DecodeKey(AssociatedTokenProgram), accounts, new byte[] { 1 });
    }

    /// <summary>SPL/Token-2022 MintToChecked instruction (tag 14 + amount u64 LE + decimals u8).</summary>
    public static Instruction MintToChecked(byte[] mint, byte[] destination, byte[] authority, byte[] tokenProgram, ulong amount, byte decimals)
    {
        var data = new byte[1 + 8 + 1];
        data[0] = 14;
        BitConverter.TryWriteBytes(data.AsSpan(1, 8), amount); // little-endian on all supported platforms
        data[9] = decimals;
        var accounts = new List<AccountMeta>
        {
            new(mint, IsSigner: false, IsWritable: true),
            new(destination, IsSigner: false, IsWritable: true),
            new(authority, IsSigner: true, IsWritable: false)
        };
        return new Instruction(tokenProgram, accounts, data);
    }

    /// <summary>
    /// Compiles a legacy (v0-less) message, orders/dedupes accounts per the runtime rules, and returns
    /// the serialized message bytes plus the ordered account key list.
    /// </summary>
    public static (byte[] Message, IReadOnlyList<byte[]> AccountKeys) CompileMessage(byte[] feePayer, byte[] recentBlockhash, IReadOnlyList<Instruction> instructions)
    {
        // Aggregate every account across instructions and the fee payer, keeping the strongest flags.
        var metas = new List<AccountMeta> { new(feePayer, IsSigner: true, IsWritable: true) };
        foreach (var instruction in instructions)
        {
            metas.Add(new AccountMeta(instruction.ProgramId, IsSigner: false, IsWritable: false));
            metas.AddRange(instruction.Accounts);
        }

        var merged = new List<AccountMeta>();
        foreach (var meta in metas)
        {
            var existing = merged.FindIndex(item => item.PublicKey.AsSpan().SequenceEqual(meta.PublicKey));
            if (existing < 0) merged.Add(meta);
            else merged[existing] = merged[existing] with { IsSigner = merged[existing].IsSigner || meta.IsSigner, IsWritable = merged[existing].IsWritable || meta.IsWritable };
        }

        // Fee payer must be first; the rest ordered: writable signers, readonly signers, writable
        // non-signers, readonly non-signers. (Fee payer is a writable signer and stays at index 0.)
        var feePayerMeta = merged.First(item => item.PublicKey.AsSpan().SequenceEqual(feePayer));
        var rest = merged.Where(item => !item.PublicKey.AsSpan().SequenceEqual(feePayer)).ToList();
        int Rank(AccountMeta m) => (m.IsSigner, m.IsWritable) switch { (true, true) => 0, (true, false) => 1, (false, true) => 2, _ => 3 };
        var ordered = new List<AccountMeta> { feePayerMeta };
        ordered.AddRange(rest.OrderBy(Rank));

        byte numRequiredSignatures = (byte)ordered.Count(m => m.IsSigner);
        byte numReadonlySigned = (byte)ordered.Count(m => m.IsSigner && !m.IsWritable);
        byte numReadonlyUnsigned = (byte)ordered.Count(m => !m.IsSigner && !m.IsWritable);

        var keys = ordered.Select(m => m.PublicKey).ToList();
        int IndexOf(byte[] key) => keys.FindIndex(k => k.AsSpan().SequenceEqual(key));

        var writer = new List<byte> { numRequiredSignatures, numReadonlySigned, numReadonlyUnsigned };
        WriteCompactU16(writer, keys.Count);
        foreach (var key in keys) writer.AddRange(key);
        writer.AddRange(recentBlockhash);
        WriteCompactU16(writer, instructions.Count);
        foreach (var instruction in instructions)
        {
            writer.Add((byte)IndexOf(instruction.ProgramId));
            WriteCompactU16(writer, instruction.Accounts.Count);
            foreach (var account in instruction.Accounts) writer.Add((byte)IndexOf(account.PublicKey));
            WriteCompactU16(writer, instruction.Data.Length);
            writer.AddRange(instruction.Data);
        }

        return (writer.ToArray(), keys);
    }

    /// <summary>Signs the compiled message with the fee-payer's Ed25519 secret and serializes the wire transaction (base64).</summary>
    public static string SignTransaction(byte[] message, byte[] secretKey64)
    {
        var signature = SignEd25519(message, secretKey64);
        var wire = new List<byte>();
        WriteCompactU16(wire, 1); // one signature
        wire.AddRange(signature);
        wire.AddRange(message);
        return Convert.ToBase64String(wire.ToArray());
    }

    /// <summary>Builds an unsigned wire transaction with a zeroed signature slot, for read-only simulateTransaction (sigVerify:false).</summary>
    public static string SerializeUnsigned(byte[] message)
    {
        var wire = new List<byte>();
        WriteCompactU16(wire, 1);
        wire.AddRange(new byte[64]);
        wire.AddRange(message);
        return Convert.ToBase64String(wire.ToArray());
    }

    public static byte[] SignEd25519(byte[] message, byte[] secretKey64)
    {
        // A Solana secret key is 64 bytes: 32-byte seed || 32-byte public key. BouncyCastle signs from the seed.
        if (secretKey64.Length != 64) throw new ArgumentException("A Solana secret key must be 64 bytes.", nameof(secretKey64));
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(secretKey64, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    /// <summary>The 32-byte public key embedded in a 64-byte Solana secret key.</summary>
    public static byte[] PublicKeyFromSecret(byte[] secretKey64) =>
        secretKey64.Length == 64 ? secretKey64[32..] : throw new ArgumentException("A Solana secret key must be 64 bytes.", nameof(secretKey64));

    public static byte[] DecodeKey(string base58)
    {
        if (!SolanaBase58.TryDecode(base58, out var bytes) || bytes.Length != 32) throw new ArgumentException($"'{base58}' is not a 32-byte Solana public key.");
        return bytes;
    }

    public static string EncodeKey(byte[] key) => SolanaBase58.Encode(key);

    // Solana short-vec (compact-u16) length prefix.
    private static void WriteCompactU16(List<byte> writer, int value)
    {
        var remaining = value;
        while (true)
        {
            var element = (byte)(remaining & 0x7f);
            remaining >>= 7;
            if (remaining == 0) { writer.Add(element); break; }
            writer.Add((byte)(element | 0x80));
        }
    }

    private static BigInteger Mod(BigInteger value) => ((value % P) + P) % P;
    private static BigInteger ModPow(BigInteger value, BigInteger exponent) => BigInteger.ModPow(Mod(value), exponent, P);
}
