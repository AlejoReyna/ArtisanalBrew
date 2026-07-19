using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/chains")]
public sealed class ChainsController(IChainRegistry registry, ISelectedChainAccessor selectedChainAccessor) : ControllerBase
{
    [HttpGet]
    public IActionResult GetChains()
    {
        var selected = selectedChainAccessor.SelectedChainKey;
        return Ok(new
        {
            defaultChainKey = registry.DefaultChainKey,
            selectedChainKey = selected,
            chains = registry.All.Where(chain => chain.Enabled).Select(chain => new
            {
                key = chain.Key,
                name = chain.DisplayName,
                shortName = chain.ShortName,
                iconAsset = chain.IconAsset,
                family = chain.FamilyName,
                chainId = chain.EvmChainId,
                chainIdHex = chain.EvmChainIdHex,
                cluster = chain.SolanaCluster,
                nativeCurrency = new { name = chain.NativeCurrencyName, symbol = chain.NativeCurrencySymbol, decimals = chain.NativeCurrencyDecimals },
                publicRpcUrl = chain.PublicRpcUrl,
                explorerAddressTemplate = chain.ExplorerAddressTemplate,
                explorerTransactionTemplate = chain.ExplorerTransactionTemplate,
                minimumConfirmations = chain.MinimumConfirmations,
                solanaCommitment = chain.SolanaCommitment,
                capabilities = chain.Capabilities,
                deployments = new
                {
                    cafe = chain.Deployment.Cafe,
                    coffee = chain.Deployment.Coffee,
                    stCafe = chain.Deployment.StCafe,
                    liquidVault = chain.Deployment.LiquidVault,
                    legacyPool = chain.Deployment.LegacyPool,
                    faucet = chain.Deployment.Faucet,
                    program = chain.Deployment.Program,
                    vaultPda = chain.Deployment.VaultPda,
                    authorityPda = chain.Deployment.AuthorityPda,
                    cafeCustody = chain.Deployment.CafeCustody,
                    coffeeCustody = chain.Deployment.CoffeeCustody,
                    admin = chain.Deployment.Admin,
                    tokenProgram = chain.Deployment.TokenProgram,
                    token2022Program = chain.Deployment.Token2022Program,
                    cafeDecimals = chain.Deployment.CafeDecimals,
                    stCafeDecimals = chain.Deployment.StCafeDecimals,
                    coffeeDecimals = chain.Deployment.CoffeeDecimals,
                    startBlockOrSlot = chain.Deployment.StartBlockOrSlot
                }
            })
        });
    }

    [HttpPost("select")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select([FromBody] SelectChainRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChainKey) || !await selectedChainAccessor.SelectAsync(request.ChainKey.Trim(), cancellationToken))
        {
            return BadRequest("The requested chain is unknown or disabled.");
        }

        var chain = selectedChainAccessor.SelectedChain;
        return Ok(new { selectedChainKey = chain.Key, family = chain.FamilyName });
    }

    public sealed record SelectChainRequest(string ChainKey);
}
