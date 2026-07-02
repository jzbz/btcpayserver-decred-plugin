using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace BTCPayServer.Plugins.Decred.Configuration;

public class DecredLikeConfiguration
{
    public Dictionary<string, DecredLikeConfigurationItem> DecredLikeConfigurationItems { get; set; } = [];
}

public class DecredLikeConfigurationItem
{
    public Uri WalletRpcUri { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }

    // Optional pinned TLS certificate (dcrwallet's rpc.cert). When set, only
    // this exact certificate is accepted for the RPC connection.
    public X509Certificate2 RpcCertificate { get; set; }
}
