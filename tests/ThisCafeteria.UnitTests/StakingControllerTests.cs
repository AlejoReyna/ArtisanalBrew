using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;
using ThisCafeteria.Web.Controllers;
using ThisCafeteria.Web.Services.Wallet;

namespace ThisCafeteria.UnitTests;

public sealed class StakingControllerTests : IDisposable
{
    private const string WalletA = "0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB";
    private const string WalletB = "0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A";
    private const string TxHash = "0xabcdef0123456789abcdef0123456789abcdef0123456789abcdef012345678a";

    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Mock<ICoffeeWeb3Service> _web3Service = new();
    private readonly Mock<IWalletChallengeService> _challengeService = new();

    public StakingControllerTests()
    {
        // One in-memory database name shared by the assertion context and the factory the
        // repository uses, so writes made through the repository are visible to _dbContext.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options);
        _contextFactory = new SingleOptionsContextFactory(options);
    }

    private sealed class SingleOptionsContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private static BlockchainNetworkOptions ConfiguredChain() => new()
    {
        StakingPoolContract = "0x1111111111111111111111111111111111111111",
        PaymentTokenContract = "0x2222222222222222222222222222222222222222",
        CoffeeCoinContract = "0x3333333333333333333333333333333333333333"
    };

    private static IIdentityAccountService CreateIdentityAccountsMock() =>
        Mock.Of<IIdentityAccountService>();

    private StakingController CreateController(
        BlockchainNetworkOptions? chain = null,
        string? sessionWallet = null,
        bool authenticated = false)
    {
        // The ledger service and repository are real, running against the in-memory context, so
        // these tests still exercise the whole record path rather than a mock of the new seam.
        var ledgerService = new StakingLedgerService(
            _web3Service.Object,
            new StakingLedgerRepository(_contextFactory));

        var controller = new StakingController(
            _web3Service.Object,
            ledgerService,
            chain ?? ConfiguredChain(),
            CreateIdentityAccountsMock(),
            _challengeService.Object);

        var httpContext = new DefaultHttpContext();
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestAuth")
            : new ClaimsIdentity();
        httpContext.User = new ClaimsPrincipal(identity);

        var session = new FakeSession();
        if (sessionWallet is not null)
        {
            session.SetString("WalletAddress", sessionWallet);
        }

        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature(session));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldReturnUnauthorized_WhenNoWalletSessionExists()
    {
        var controller = CreateController(sessionWallet: null, authenticated: false);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldReturnBadRequest_WhenRequestWalletDoesNotMatchSessionWallet()
    {
        var controller = CreateController(sessionWallet: WalletB);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("The connected wallet does not match the staking session wallet.");
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldReturnConflict_WhenTransactionHashAlreadyRecorded()
    {
        _dbContext.StakingLedgerEntries.Add(StakingLedgerEntry.Create("ethereum-sepolia", TxHash, 0, entry =>
        {
            entry.WalletAddress = WalletA;
            entry.ActionType = "stake";
            entry.Amount = 10m;
            entry.NetworkName = "Ethereum Sepolia";
            entry.PaymentTokenContract = "0x2222222222222222222222222222222222222222";
            entry.StakingPoolContract = "0x1111111111111111111111111111111111111111";
        }));
        await _dbContext.SaveChangesAsync();

        var controller = CreateController(sessionWallet: WalletA);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldReturnBadRequest_WhenOnChainVerificationFails()
    {
        _web3Service
            .Setup(service => service.VerifyStakingTransactionAsync(
                TxHash, WalletA, 10m, StakingTransactionType.Stake, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StakingVerificationResult.Failed);

        var controller = CreateController(sessionWallet: WalletA);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Staking transaction could not be verified on-chain.");
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldPersistLedgerEntry_WhenVerificationSucceeds()
    {
        _web3Service
            .Setup(service => service.VerifyStakingTransactionAsync(
                TxHash, WalletA, 10m, StakingTransactionType.Stake, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StakingVerificationResult(TransactionVerificationStatus.Verified, 10m));

        var controller = CreateController(sessionWallet: WalletA);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await _dbContext.StakingLedgerEntries.AnyAsync(e => e.TransactionHash == TxHash)).Should().BeTrue();
    }

    [Fact]
    public async Task RecordStakeAsync_ShouldReturnBadRequest_WhenStakingIsNotConfigured()
    {
        var controller = CreateController(chain: new BlockchainNetworkOptions(), sessionWallet: WalletA);
        var request = new StakingController.StakingTransactionRequest(WalletA, 10m, TxHash);

        var result = await controller.RecordStakeAsync(request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Configure a payment token and staking pool contract before staking.");
    }

    private sealed class FakeSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool IsAvailable => true;
        public string Id => "fake-session";
        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
