using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Decred.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Decred.Payments;

public class DecredLikePaymentMethodHandler : IPaymentMethodHandler
{
    readonly DecredLikeSpecificBtcPayNetwork _network;
    readonly DecredRpcProvider _rpcProvider;

    public DecredLikePaymentMethodHandler(
        DecredLikeSpecificBtcPayNetwork network,
        DecredRpcProvider rpcProvider)
    {
        _network = network;
        _rpcProvider = rpcProvider;
    }

    public PaymentMethodId PaymentMethodId =>
        PaymentTypes.CHAIN.GetPaymentMethodId(_network.CryptoCode);

    public JsonSerializer Serializer { get; } = new();

    record Prepare(Task<string> ReservedAddress);

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = _network.CryptoCode;
        context.Prompt.Divisibility = _network.Divisibility;

        var walletClient = _rpcProvider.GetWalletClient(_network.CryptoCode);

        if (walletClient == null)
        {
            context.Prompt.Inactive = true;
            return Task.CompletedTask;
        }

        var config = ParsePaymentMethodConfig(context.PaymentMethodConfig) as DecredPaymentPromptDetails
            ?? new DecredPaymentPromptDetails();
        var account = string.IsNullOrWhiteSpace(config.AccountName) ? "default" : config.AccountName;

        context.State = new Prepare(
            ReservedAddress: walletClient.SendCommandAsync<string>(
                "getnewaddress", [account, "ignore"]));

        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (!_rpcProvider.IsAvailable(_network.CryptoCode))
            throw new PaymentMethodUnavailableException("Node not available");

        if (context.State is not Prepare prepare)
            throw new PaymentMethodUnavailableException("Prompt not prepared");

        var address = await prepare.ReservedAddress;

        // Store prompt details (account name + settlement threshold from config)
        var storeConfig = ParsePaymentMethodConfig(context.PaymentMethodConfig) as DecredPaymentPromptDetails
            ?? new DecredPaymentPromptDetails();

        var promptDetails = new DecredPaymentPromptDetails
        {
            AccountName = storeConfig.AccountName,
            InvoiceSettledConfirmationThreshold = storeConfig.InvoiceSettledConfirmationThreshold
        };

        context.Prompt.Destination = address;
        context.Prompt.PaymentMethodFee = 0;
        context.Prompt.Details = JObject.FromObject(promptDetails, Serializer);

        context.TrackedDestinations.Add(address);
    }

    public object ParsePaymentPromptDetails(JToken details)
    {
        return details?.ToObject<DecredPaymentPromptDetails>(Serializer);
    }

    public object ParsePaymentMethodConfig(JToken config)
    {
        return config?.ToObject<DecredPaymentPromptDetails>(Serializer)
            ?? new DecredPaymentPromptDetails();
    }

    public object ParsePaymentDetails(JToken details)
    {
        return details?.ToObject<DecredLikePaymentData>(Serializer);
    }
}
