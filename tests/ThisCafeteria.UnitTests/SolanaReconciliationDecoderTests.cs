using System.Text.Json;
using FluentAssertions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Worker;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaReconciliationDecoderTests
{
    [Fact]
    public void DecodesShortRewardFundingEventsWithoutChangingTheCanonicalLogIndex()
    {
        var payload = new byte[24];
        BitConverter.GetBytes(2_000UL).CopyTo(payload, 0);
        BitConverter.GetBytes(100UL).CopyTo(payload, 8);
        BitConverter.GetBytes(88UL).CopyTo(payload, 16);
        var data = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.RewardFunded), .. payload]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            meta = new { logMessages = new[] { "Program program invoke [1]", "Program log: instruction", $"Program data: {data}", "Program program success" } }
        }));
        var chain = new ChainDefinition
        {
            Key = "solana-localnet",
            DisplayName = "Solana Localnet",
            Family = ChainFamily.Solana,
            ExplorerTransactionTemplate = "http://localhost/tx/{0}",
            Deployment = new ChainDeployment
            {
                Program = "program",
                Admin = "admin",
                Cafe = "cafe",
                StCafe = "stcafe",
                Coffee = "coffee",
                CafeDecimals = 9,
                StCafeDecimals = 9,
                CoffeeDecimals = 9
            }
        };

        var entries = SolanaReconciliationSupervisor.Decode(chain, "signature", 88, document.RootElement).ToArray();

        entries.Should().ContainSingle();
        entries[0].ActionType.Should().Be("reward_funding");
        entries[0].WalletAddress.Should().Be("admin");
        entries[0].OperationIndex.Should().Be(2);
        entries[0].RawRewardAmount.Should().Be("2000");
        entries[0].RewardAmount.Should().Be(0.000002m);
    }
}
