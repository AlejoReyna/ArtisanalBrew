using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed record SolanaAnchorEvent(string Name, int LogIndex, byte[] Payload);

public static class SolanaAnchorEventCodec
{
    public const string Deposit = "DepositEvent";
    public const string Redeem = "RedeemEvent";
    public const string RewardClaimed = "RewardClaimedEvent";
    public const string RewardFunded = "RewardFundedEvent";
    public const string TransferCheckpoint = "TransferCheckpointEvent";

    public static IReadOnlyList<SolanaAnchorEvent> Decode(JsonElement logMessages, string trustedProgram, params string[] acceptedNames)
    {
        if (string.IsNullOrWhiteSpace(trustedProgram)) throw new ArgumentException("A trusted Solana program is required.", nameof(trustedProgram));
        var discriminators = acceptedNames.ToDictionary(name => name, EventDiscriminator, StringComparer.Ordinal);
        var result = new List<SolanaAnchorEvent>();
        var invocationStack = new Stack<string>();
        var logIndex = 0;

        foreach (var log in logMessages.EnumerateArray())
        {
            var value = log.GetString() ?? string.Empty;
            if (TryInvocation(value, out var invokedProgram)) invocationStack.Push(invokedProgram);
            else if (value.StartsWith("Program data: ", StringComparison.Ordinal) &&
                     invocationStack.TryPeek(out var activeProgram) &&
                     string.Equals(activeProgram, trustedProgram, StringComparison.Ordinal))
            {
                try
                {
                    var buffer = Convert.FromBase64String(value[14..]);
                    foreach (var (name, discriminator) in discriminators)
                    {
                        if (buffer.AsSpan().StartsWith(discriminator))
                        {
                            result.Add(new SolanaAnchorEvent(name, logIndex, buffer[8..]));
                            break;
                        }
                    }
                }
                catch (FormatException)
                {
                    // Ignore unrelated or malformed program log output. The caller still
                    // requires the expected event before accepting the transaction.
                }
            }
            else if (IsCompletion(value, out var completedProgram) &&
                     invocationStack.TryPeek(out var currentProgram) &&
                     string.Equals(currentProgram, completedProgram, StringComparison.Ordinal))
            {
                invocationStack.Pop();
            }

            logIndex++;
        }

        return result;
    }

    private static bool TryInvocation(string value, out string program)
    {
        const string prefix = "Program ";
        const string marker = " invoke [";
        program = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var markerIndex = value.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
        if (markerIndex <= prefix.Length) return false;
        program = value[prefix.Length..markerIndex];
        return true;
    }

    private static bool IsCompletion(string value, out string program)
    {
        const string prefix = "Program ";
        program = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var successIndex = value.IndexOf(" success", prefix.Length, StringComparison.Ordinal);
        var failedIndex = value.IndexOf(" failed:", prefix.Length, StringComparison.Ordinal);
        var markerIndex = successIndex >= 0 ? successIndex : failedIndex;
        if (markerIndex <= prefix.Length) return false;
        program = value[prefix.Length..markerIndex];
        return true;
    }

    public static byte[] EventDiscriminator(string name) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"event:{name}"))[..8];

    public static byte[] AccountDiscriminator(string name) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"account:{name}"))[..8];
}
