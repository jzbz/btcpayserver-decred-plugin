using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Decred.RPC.Models;

public class WalletGetInfoResponse
{
    [JsonProperty("version")]
    public long Version { get; set; }

    [JsonProperty("blocks")]
    public long Blocks { get; set; }

    [JsonProperty("balance")]
    public decimal Balance { get; set; }

    [JsonProperty("txfee")]
    public decimal TxFee { get; set; }
}

public class SyncStatusResponse
{
    [JsonProperty("synced")]
    public bool Synced { get; set; }

    [JsonProperty("initialblockdownload")]
    public bool InitialBlockDownload { get; set; }

    [JsonProperty("headersfetchprogress")]
    public float HeadersFetchProgress { get; set; }
}
