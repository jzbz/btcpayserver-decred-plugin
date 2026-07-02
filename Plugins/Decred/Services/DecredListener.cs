using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Decred.Payments;
using BTCPayServer.Plugins.Decred.RPC;
using BTCPayServer.Plugins.Decred.RPC.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Decred.Services;

public class DecredListener : EventHostedServiceBase
{
    readonly InvoiceRepository _invoiceRepository;
    readonly DecredRpcProvider _rpcProvider;
    readonly PaymentMethodHandlerDictionary _handlers;
    readonly PaymentService _paymentService;
    readonly ILogger<DecredListener> _logger;

    public DecredListener(
        EventAggregator eventAggregator,
        InvoiceRepository invoiceRepository,
        DecredRpcProvider rpcProvider,
        PaymentMethodHandlerDictionary handlers,
        PaymentService paymentService,
        ILogger<DecredListener> logger) : base(eventAggregator, logger)
    {
        _invoiceRepository = invoiceRepository;
        _rpcProvider = rpcProvider;
        _handlers = handlers;
        _paymentService = paymentService;
        _logger = logger;
    }

    PaymentMethodId Pmi => PaymentTypes.CHAIN.GetPaymentMethodId("DCR");

    protected override void SubscribeToEvents()
    {
        Subscribe<DecredEvent>();
        Subscribe<DecredRpcProvider.DecredDaemonStateChange>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case DecredRpcProvider.DecredDaemonStateChange stateChange:
                if (_rpcProvider.IsAvailable(stateChange.CryptoCode))
                    await UpdateAnyPendingPayments(stateChange.CryptoCode, cancellationToken);
                break;

            case DecredEvent { BlockHash: not null } blockEvent:
                await UpdateAnyPendingPayments(blockEvent.CryptoCode, cancellationToken);
                break;

            case DecredEvent { TransactionHash: not null } txEvent:
                // "poll" is a synthetic event from the summary updater
                if (txEvent.TransactionHash == "poll")
                    await UpdateAnyPendingPayments(txEvent.CryptoCode, cancellationToken);
                else
                    await OnTransactionUpdated(txEvent.CryptoCode, txEvent.TransactionHash, cancellationToken);
                break;
        }
    }

    async Task UpdateAnyPendingPayments(string cryptoCode, CancellationToken cancellationToken)
    {
        var invoices = await _invoiceRepository.GetMonitoredInvoices(Pmi, cancellationToken);

        foreach (var invoice in invoices)
        {
            try
            {
                var prompt = invoice.GetPaymentPrompt(Pmi);
                if (prompt?.Destination == null) continue;

                await CheckPaymentsForAddress(cryptoCode, invoice, prompt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating payments for invoice {InvoiceId}", invoice.Id);
            }
        }
    }

    async Task OnTransactionUpdated(string cryptoCode, string txHash, CancellationToken cancellationToken)
    {
        var walletClient = _rpcProvider.GetWalletClient(cryptoCode);
        if (walletClient == null) return;

        try
        {
            var tx = await walletClient.SendCommandAsync<GetTransactionResponse>(
                "gettransaction", [txHash], cancellationToken);

            if (tx.Details == null) return;

            var invoices = await _invoiceRepository.GetMonitoredInvoices(Pmi, cancellationToken);

            // A transaction can pay the same address through multiple outputs;
            // sum them so the payment is not undercounted.
            var receivedByAddress = tx.Details
                .Where(d => d.Category == "receive" && d.Address != null)
                .GroupBy(d => d.Address)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));

            foreach (var invoice in invoices)
            {
                var prompt = invoice.GetPaymentPrompt(Pmi);
                if (prompt?.Destination == null) continue;
                if (!receivedByAddress.TryGetValue(prompt.Destination, out var amount)) continue;

                await HandlePaymentData(cryptoCode, invoice, tx.TxId,
                    amount, tx.Confirmations, tx.Time, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing transaction {TxHash} for {CryptoCode}", txHash, cryptoCode);
        }
    }

    async Task CheckPaymentsForAddress(string cryptoCode, InvoiceEntity invoice,
        PaymentPrompt prompt, CancellationToken cancellationToken)
    {
        var walletClient = _rpcProvider.GetWalletClient(cryptoCode);
        if (walletClient == null) return;

        try
        {
            var txs = await walletClient.SendCommandAsync<ListTransactionsEntry[]>(
                "listaddresstransactions", [new[] { prompt.Destination }], cancellationToken);

            if (txs == null) return;

            // One entry per output: sum outputs of the same transaction paying
            // this address so the payment is not undercounted.
            foreach (var group in txs
                .Where(t => t.Category == "receive" && t.Address == prompt.Destination)
                .GroupBy(t => t.TxId))
            {
                var first = group.First();
                await HandlePaymentData(cryptoCode, invoice, group.Key,
                    group.Sum(t => t.Amount), first.Confirmations, first.Time, cancellationToken);
            }
        }
        catch (JsonRpcException)
        {
            await CheckPaymentsViaListTransactions(cryptoCode, invoice, prompt, cancellationToken);
        }
    }

    async Task CheckPaymentsViaListTransactions(string cryptoCode, InvoiceEntity invoice,
        PaymentPrompt prompt, CancellationToken cancellationToken)
    {
        var walletClient = _rpcProvider.GetWalletClient(cryptoCode);
        if (walletClient == null) return;

        var txs = await walletClient.SendCommandAsync<ListTransactionsEntry[]>(
            "listtransactions", ["*", (object)1000, (object)0], cancellationToken);

        if (txs == null) return;

        foreach (var group in txs
            .Where(t => t.Category == "receive" && t.Address == prompt.Destination)
            .GroupBy(t => t.TxId))
        {
            var first = group.First();
            await HandlePaymentData(cryptoCode, invoice, group.Key,
                group.Sum(t => t.Amount), first.Confirmations, first.Time, cancellationToken);
        }
    }

    async Task HandlePaymentData(string cryptoCode, InvoiceEntity invoice,
        string txId, decimal amount, long confirmations, long time, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(Pmi, out var handler))
            return;

        var prompt = invoice.GetPaymentPrompt(Pmi);
        if (prompt == null) return;

        var existingPayments = invoice.GetPayments(false);
        var existing = existingPayments.FirstOrDefault(p =>
            p.PaymentMethodId == Pmi &&
            p.Details is JObject details &&
            (string)details["transactionId"] == txId);

        var paymentData = new DecredLikePaymentData
        {
            TransactionId = txId,
            ConfirmationCount = confirmations,
            Address = prompt.Destination
        };

        var status = GetPaymentStatus(confirmations, prompt, invoice);

        if (existing != null)
        {
            var existingData = existing.Details?.ToObject<DecredLikePaymentData>();
            if (existingData != null && existingData.ConfirmationCount != confirmations)
            {
                existing.Status = status;
                existing.Details = JToken.FromObject(paymentData);
                await _paymentService.UpdatePayments([existing]);
                EventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            }
            return;
        }

        var newPayment = new PaymentData
        {
            Id = $"{txId}#{prompt.Destination}",
            Created = time > 0 ? DateTimeOffset.FromUnixTimeSeconds(time) : DateTimeOffset.UtcNow,
            Status = status,
            Amount = amount,
            Currency = cryptoCode
        };
        newPayment.Set(invoice, handler, paymentData);

        var payment = await _paymentService.AddPayment(newPayment);
        if (payment != null)
        {
            // Notify BTCPayServer that a payment was received so it updates the invoice status
            var updatedInvoice = await _invoiceRepository.GetInvoice(invoice.Id);
            EventAggregator.Publish(new InvoiceEvent(updatedInvoice, InvoiceEvent.ReceivedPayment)
                { Payment = payment });
            EventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
        }

        _logger.LogInformation(
            "Payment detected for invoice {InvoiceId}: {Amount} DCR, tx {TxId}, {Confirmations} confirmations",
            invoice.Id, amount, txId, confirmations);
    }

    static PaymentStatus GetPaymentStatus(long confirmations, PaymentPrompt prompt, InvoiceEntity invoice)
    {
        var promptDetails = prompt.Details?.ToObject<DecredPaymentPromptDetails>();
        var requiredConfirmations = promptDetails?.InvoiceSettledConfirmationThreshold
            ?? ConfirmationRequired(invoice.SpeedPolicy);

        return confirmations >= requiredConfirmations
            ? PaymentStatus.Settled
            : PaymentStatus.Processing;
    }

    // Mirrors the mapping the core Bitcoin listener uses for its speed policy.
    static int ConfirmationRequired(SpeedPolicy speedPolicy) => speedPolicy switch
    {
        SpeedPolicy.HighSpeed => 0,
        SpeedPolicy.MediumSpeed => 1,
        SpeedPolicy.LowMediumSpeed => 2,
        SpeedPolicy.LowSpeed => 6,
        _ => 6
    };
}
