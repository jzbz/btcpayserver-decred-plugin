using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Decred.Configuration;
using BTCPayServer.Plugins.Decred.RPC;
using BTCPayServer.Plugins.Decred.RPC.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Decred.Services;

public class DecredRpcProvider
{
    readonly ImmutableDictionary<string, JsonRpcClient> _walletClients;
    readonly ILogger<DecredRpcProvider> _logger;
    readonly ConcurrentDictionary<string, DecredLikeSummary> _summaries = new();

    public class DecredDaemonStateChange
    {
        public string CryptoCode { get; set; }
    }

    public DecredRpcProvider(
        DecredLikeConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DecredRpcProvider> logger)
    {
        _logger = logger;
        var walletClients = ImmutableDictionary.CreateBuilder<string, JsonRpcClient>();

        foreach (var (cryptoCode, item) in configuration.DecredLikeConfigurationItems)
        {
            var walletHttpClient = httpClientFactory.CreateClient($"{cryptoCode}client");
            walletClients.Add(cryptoCode, new JsonRpcClient(item.WalletRpcUri, walletHttpClient));
        }

        _walletClients = walletClients.ToImmutable();
    }

    public bool HasSummary(string cryptoCode) => _summaries.ContainsKey(cryptoCode);

    public DecredLikeSummary GetSummary(string cryptoCode)
    {
        _summaries.TryGetValue(cryptoCode, out var summary);
        return summary;
    }

    public IEnumerable<string> GetCryptoCodes() => _walletClients.Keys;

    public JsonRpcClient GetWalletClient(string cryptoCode) =>
        _walletClients.TryGetValue(cryptoCode, out var client) ? client : null;

    public bool IsAvailable(string cryptoCode) =>
        _summaries.TryGetValue(cryptoCode, out var s) && s.Synced && s.WalletAvailable;

    public async Task UpdateSummary(string cryptoCode, CancellationToken cancellationToken = default)
    {
        if (!_summaries.TryGetValue(cryptoCode, out var summary))
        {
            summary = new DecredLikeSummary();
            _summaries[cryptoCode] = summary;
        }

        var previousAvailable = summary.WalletAvailable;

        try
        {
            var walletClient = GetWalletClient(cryptoCode);
            if (walletClient == null)
            {
                summary.WalletAvailable = false;
                return;
            }

            var walletInfo = await walletClient.SendCommandAsync<WalletGetInfoResponse>(
                "getinfo", cancellationToken: cancellationToken);
            summary.CurrentHeight = walletInfo.Blocks;
            summary.WalletHeight = walletInfo.Blocks;
            summary.WalletAvailable = true;
            summary.UpdatedAt = DateTimeOffset.UtcNow;

            // dcrwallet in SPV mode syncs headers then blocks.
            // Consider synced if we have blocks and the height is recent.
            // A more robust check could compare against peer-reported heights,
            // but for now trust that dcrwallet reports accurately.
            summary.Synced = walletInfo.Blocks > 0;
        }
        catch (Exception ex)
        {
            summary.WalletAvailable = false;
            summary.Synced = false;
            _logger.LogWarning(ex, "Failed to get wallet info for {CryptoCode}", cryptoCode);
        }

        if (previousAvailable != summary.WalletAvailable)
        {
            _logger.LogInformation("{CryptoCode} wallet availability changed to {Available}",
                cryptoCode, summary.WalletAvailable);
        }
    }
}

public class DecredLikeSummary
{
    public bool Synced { get; set; }
    public long CurrentHeight { get; set; }
    public long WalletHeight { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool WalletAvailable { get; set; }
}
