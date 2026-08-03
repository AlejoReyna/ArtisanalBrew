using System;
using System.Linq;
using ThisCafeteria.Application.Configuration;

var options = BlockchainOptions.CreateDefaults();
var bsc = options.Chains.Single(c => c.Key == "bsc-testnet");
Console.WriteLine($"bsc-testnet enabled: {bsc.Enabled}");
var solana = options.Chains.Single(c => c.Key == "solana-testnet");
Console.WriteLine($"solana-testnet enabled: {solana.Enabled}");
