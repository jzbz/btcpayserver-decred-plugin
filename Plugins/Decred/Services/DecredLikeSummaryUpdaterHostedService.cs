using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Decred.RPC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Decred.Services;

public class DecredLikeSummaryUpdaterHostedService : IHostedService, IDisposable
{
    readonly DecredRpcProvider _rpcProvider;
    readonly ILogger<DecredLikeSummaryUpdaterHostedService> _logger;
    readonly EventAggregator _eventAggregator;
    CancellationTokenSource _cts;
    Task _updateTask;

    public DecredLikeSummaryUpdaterHostedService(
        DecredRpcProvider rpcProvider,
        EventAggregator eventAggregator,
        ILogger<DecredLikeSummaryUpdaterHostedService> logger)
    {
        _rpcProvider = rpcProvider;
        _eventAggregator = eventAggregator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _updateTask = UpdateLoop(_cts.Token);
        return Task.CompletedTask;
    }

    async Task UpdateLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cryptoCodes = _rpcProvider.GetCryptoCodes().ToList();
                foreach (var cryptoCode in cryptoCodes)
                {
                    var previouslyAvailable = _rpcProvider.IsAvailable(cryptoCode);
                    await _rpcProvider.UpdateSummary(cryptoCode, cancellationToken);
                    var nowAvailable = _rpcProvider.IsAvailable(cryptoCode);

                    if (previouslyAvailable != nowAvailable)
                    {
                        _eventAggregator.Publish(new DecredRpcProvider.DecredDaemonStateChange
                        {
                            CryptoCode = cryptoCode
                        });
                    }

                    // Trigger a payment re-check on every poll cycle so the listener
                    // catches payments even without external notifications.
                    if (nowAvailable)
                    {
                        _eventAggregator.Publish(new DecredEvent
                        {
                            CryptoCode = cryptoCode,
                            TransactionHash = "poll"
                        });
                    }
                }

                // Poll every 15 seconds when available, 10 seconds when not
                var allAvailable = cryptoCodes.All(c => _rpcProvider.IsAvailable(c));
                await Task.Delay(allAvailable ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(10),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in summary updater loop");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_updateTask != null)
        {
            try { await _updateTask; }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
