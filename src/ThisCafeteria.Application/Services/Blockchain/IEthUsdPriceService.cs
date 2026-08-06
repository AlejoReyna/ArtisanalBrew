namespace ThisCafeteria.Application.Services.Blockchain;

public interface IEthUsdPriceService
{
    Task<decimal?> GetEthUsdPriceAsync(CancellationToken cancellationToken = default);
}
