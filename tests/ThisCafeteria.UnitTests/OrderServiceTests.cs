using FluentAssertions;
using FluentValidation;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Validation;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.UnitTests;

public sealed class OrderServiceTests
{
    private static readonly Guid UserProfileId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string PaymentHash = "0x2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public async Task CreateOrder_RejectsUnverifiedPayment()
    {
        var harness = CreateHarness(catalogPrice: 20m, verification: StakingVerificationResult.Failed);

        var act = () => harness.Service.CreateOrderAsync(ValidRequest(clientPrice: 20m), UserProfileId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be verified*");
        harness.Orders.Verify(repository => repository.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_RequotesCatalogPriceSoCookieTamperingFailsVerification()
    {
        var capturedAmount = 0m;
        var harness = CreateHarness(
            catalogPrice: 20m,
            verification: new StakingVerificationResult(TransactionVerificationStatus.Verified, 1m),
            onVerify: amount => capturedAmount = amount);

        await harness.Service.CreateOrderAsync(ValidRequest(clientPrice: 1m), UserProfileId);

        var expected = new OrderPricingService().ToNativePaymentAmount(
            new OrderPricingService().Calculate([new CartItemDto(ProductId, "Coffee", 1, 20m)]).Total,
            "ETH");
        capturedAmount.Should().Be(expected);
        capturedAmount.Should().BeGreaterThan(new OrderPricingService().ToNativePaymentAmount(1m + 4m + 0.16m, "ETH"));
    }

    [Fact]
    public async Task CreateOrder_BindsTheAuthenticatedProfileNotTheRequestProfile()
    {
        Order? saved = null;
        var harness = CreateHarness(
            catalogPrice: 20m,
            verification: new StakingVerificationResult(TransactionVerificationStatus.Verified, 1m));
        harness.Orders
            .Setup(repository => repository.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((order, _) => saved = order)
            .Returns(Task.CompletedTask);

        var forgedProfile = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        await harness.Service.CreateOrderAsync(ValidRequest(clientPrice: 20m) with { UserProfileId = forgedProfile }, UserProfileId);

        saved.Should().NotBeNull();
        saved!.UserProfileId.Should().Be(UserProfileId);
        saved.UserProfileId.Should().NotBe(forgedProfile);
    }

    [Fact]
    public async Task CreateOrder_RejectsAReusedPaymentHash()
    {
        var harness = CreateHarness(
            catalogPrice: 20m,
            verification: new StakingVerificationResult(TransactionVerificationStatus.Verified, 1m));
        harness.Orders
            .Setup(repository => repository.ExistsByPaymentHashAsync(PaymentHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => harness.Service.CreateOrderAsync(ValidRequest(clientPrice: 20m), UserProfileId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public async Task CreateOrder_DecrementsCatalogStockWhenPaymentVerifies()
    {
        var product = CatalogProduct(20m, stock: 3);
        var harness = CreateHarness(
            catalogPrice: 20m,
            verification: new StakingVerificationResult(TransactionVerificationStatus.Verified, 1m),
            product: product);

        await harness.Service.CreateOrderAsync(ValidRequest(clientPrice: 20m), UserProfileId);

        product.StockQuantity.Should().Be(2);
        harness.Products.Verify(
            repository => repository.UpdateAsync(product, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static CreateOrderRequest ValidRequest(decimal clientPrice) => new(
        UserProfileId,
        [new CartItemDto(ProductId, "Coffee", 1, clientPrice)],
        Wallet,
        PaymentHash,
        11155111,
        "Ethereum Sepolia",
        0.01m,
        "https://sepolia.etherscan.io/tx/" + PaymentHash,
        DateTime.UtcNow);

    private static Product CatalogProduct(decimal price, int stock = 5) => new()
    {
        Id = ProductId,
        Name = "Coffee",
        Slug = "coffee",
        Price = price,
        StockQuantity = stock,
        IsActive = true
    };

    private static Harness CreateHarness(
        decimal catalogPrice,
        StakingVerificationResult verification,
        Action<decimal>? onVerify = null,
        Product? product = null)
    {
        var catalog = product ?? CatalogProduct(catalogPrice);
        var orders = new Mock<IOrderRepository>();
        orders.Setup(repository => repository.ExistsByPaymentHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        orders.Setup(repository => repository.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetProductByIdAsync(ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        products.Setup(repository => repository.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coupons = new Mock<ICouponRepository>();
        var gateway = new Mock<IMarketplacePaymentGateway>();
        gateway.Setup(service => service.VerifyNativePaymentAsync(
                "ethereum-sepolia",
                PaymentHash,
                Wallet,
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, decimal, CancellationToken>((_, _, _, amount, _) => onVerify?.Invoke(amount))
            .ReturnsAsync(verification);

        var transparency = new Mock<ITransparencyService>();
        transparency.Setup(service => service.CreatePendingRecordsForOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new OrderService(
            orders.Object,
            products.Object,
            coupons.Object,
            new OrderPricingService(),
            gateway.Object,
            new ChainRegistry(new BlockchainOptions
            {
                DefaultChainKey = "ethereum-sepolia",
                Chains =
                [
                    new ChainDefinition
                    {
                        Key = "ethereum-sepolia",
                        DisplayName = "Ethereum Sepolia",
                        Family = ChainFamily.Evm,
                        Enabled = true,
                        EvmChainId = 11155111,
                        EvmChainIdHex = "0xaa36a7",
                        NativeCurrencySymbol = "ETH",
                        PublicRpcUrl = "https://ethereum-sepolia-rpc.publicnode.com",
                        ExplorerTransactionTemplate = "https://sepolia.etherscan.io/tx/{0}",
                        Capabilities = new ChainCapabilities { MarketplacePayment = true, WalletLogin = true },
                        Deployment = new ChainDeployment { LegacyPool = "0x9d5305a9621aafb5b5f8ba7a9977e3d96ea7eceb" }
                    }
                ]
            }),
            transparency.Object,
            new CreateOrderRequestValidator());

        return new Harness(service, orders, products);
    }

    private sealed record Harness(
        OrderService Service,
        Mock<IOrderRepository> Orders,
        Mock<IProductRepository> Products);
}
