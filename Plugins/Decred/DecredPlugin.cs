using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Decred.Configuration;
using BTCPayServer.Plugins.Decred.Payments;
using BTCPayServer.Plugins.Decred.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Decred;

public class DecredPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.Decred";
    public override string Name => "Decred";
    public override string Description => "Enables receiving payments via Decred.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies =>
    [
        new() { Identifier = "BTCPayServer", Condition = ">=2.1.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        var network = new DecredLikeSpecificBtcPayNetwork
        {
            CryptoCode = "DCR",
            DisplayName = "Decred",
            Divisibility = 8,
            CryptoImagePath = "decred.svg",
            UriScheme = "decred",
            DefaultRateRules =
            [
                "DCR_X = DCR_USD * USD_X",
                "DCR_USD = kraken(DCR_USD)"
            ]
        };

        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("DCR");
        var blockExplorerLink = "https://dcrdata.decred.org/tx/{0}";

        services.AddBTCPayNetwork(network)
            .AddTransactionLinkProvider(pmi, new DefaultTransactionLinkProvider(blockExplorerLink));

        services.AddCurrencyData(new CurrencyData
        {
            Code = "DCR",
            Name = "Decred",
            Divisibility = 8,
            Symbol = null,
            Crypto = true
        });

        ConfigureDecredConfiguration(services);

        services.AddSingleton<DecredRpcProvider>();
        services.AddHostedService<DecredLikeSummaryUpdaterHostedService>();
        services.AddHostedService<DecredListener>();

        services.AddSingleton<IPaymentMethodHandler>(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider,
                typeof(DecredLikePaymentMethodHandler), network));

        services.AddSingleton<ICheckoutModelExtension>(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider,
                typeof(DecredCheckoutModelExtension), network));

        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider,
                typeof(DecredPaymentLinkExtension), network));

        services.AddUIExtension("store-wallets-nav", "Decred/StoreWalletsNavDecredExtension");
        services.AddUIExtension("store-invoices-payments", "Decred/ViewDecredLikePaymentData");

        services.AddSingleton<ISyncSummaryProvider, DecredSyncSummaryProvider>();
    }

    static void ConfigureDecredConfiguration(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var config = new DecredLikeConfiguration();
            var cryptoCode = "DCR";

            var prefix = $"BTCPAY_{cryptoCode}_";
            var walletUri = Environment.GetEnvironmentVariable(prefix + "WALLET_URI");
            var username = Environment.GetEnvironmentVariable(prefix + "RPC_USERNAME");
            var password = Environment.GetEnvironmentVariable(prefix + "RPC_PASSWORD");

            if (walletUri == null)
                return config;

            config.DecredLikeConfigurationItems[cryptoCode] = new DecredLikeConfigurationItem
            {
                WalletRpcUri = new Uri(walletUri),
                Username = username,
                Password = password
            };

            return config;
        });

        services.AddHttpClient("DCRclient")
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var config = provider.GetRequiredService<DecredLikeConfiguration>();
                var handler = new HttpClientHandler();
                // dcrd/dcrwallet use self-signed TLS certs
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            })
            .ConfigureHttpClient((provider, client) =>
            {
                var config = provider.GetRequiredService<DecredLikeConfiguration>();
                if (config.DecredLikeConfigurationItems.TryGetValue("DCR", out var item) &&
                    !string.IsNullOrEmpty(item.Username))
                {
                    var credentials = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{item.Username}:{item.Password}"));
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", credentials);
                }
            });
    }
}
