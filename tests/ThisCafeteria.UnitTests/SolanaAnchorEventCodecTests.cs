using System.Text.Json;
using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaAnchorEventCodecTests
{
    private const string Program = "Trusted1111111111111111111111111111111111";

    [Fact]
    public void UsesTheAnchorEventTypeNameAndCanonicalLogIndex()
    {
        var payload = Enumerable.Range(0, 56).Select(value => (byte)value).ToArray();
        var encoded = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.Deposit), .. payload]);
        using var document = JsonDocument.Parse($$"""["Program {{Program}} invoke [1]", "Program data: {{encoded}}", "Program {{Program}} success"]""");

        var events = SolanaAnchorEventCodec.Decode(document.RootElement, Program, SolanaAnchorEventCodec.Deposit);

        events.Should().ContainSingle();
        events[0].Name.Should().Be("DepositEvent");
        events[0].LogIndex.Should().Be(1);
        events[0].Payload.Should().Equal(payload);
        SolanaAnchorEventCodec.Decode(document.RootElement, Program, "Deposit").Should().BeEmpty();
    }

    [Fact]
    public void DecodesTheShortRewardFundingPayload()
    {
        var payload = new byte[24];
        BitConverter.GetBytes(42UL).CopyTo(payload, 0);
        var encoded = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.RewardFunded), .. payload]);
        using var document = JsonDocument.Parse($$"""["Program {{Program}} invoke [1]", "Program data: {{encoded}}", "Program {{Program}} success"]""");

        var events = SolanaAnchorEventCodec.Decode(document.RootElement, Program, SolanaAnchorEventCodec.RewardFunded);

        events.Should().ContainSingle();
        BitConverter.ToUInt64(events[0].Payload, 0).Should().Be(42UL);
        events[0].LogIndex.Should().Be(1);
    }

    [Fact]
    public void RejectsMatchingEventDataEmittedByAnotherProgramDuringCpi()
    {
        var encoded = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.Deposit), .. new byte[56]]);
        using var document = JsonDocument.Parse($$"""
        [
          "Program {{Program}} invoke [1]",
          "Program Attacker11111111111111111111111111111111 invoke [2]",
          "Program data: {{encoded}}",
          "Program Attacker11111111111111111111111111111111 success",
          "Program {{Program}} success"
        ]
        """);

        SolanaAnchorEventCodec.Decode(document.RootElement, Program, SolanaAnchorEventCodec.Deposit).Should().BeEmpty();
    }
}
