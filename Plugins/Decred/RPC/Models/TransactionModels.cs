using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Decred.RPC.Models;

public class GetTransactionResponse
{
    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("fee")]
    public decimal Fee { get; set; }

    [JsonProperty("confirmations")]
    public long Confirmations { get; set; }

    [JsonProperty("blockhash")]
    public string BlockHash { get; set; }

    [JsonProperty("blocktime")]
    public long BlockTime { get; set; }

    [JsonProperty("txid")]
    public string TxId { get; set; }

    [JsonProperty("time")]
    public long Time { get; set; }

    [JsonProperty("timereceived")]
    public long TimeReceived { get; set; }

    [JsonProperty("details")]
    public List<TransactionDetail> Details { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }
}

public class TransactionDetail
{
    [JsonProperty("account")]
    public string Account { get; set; }

    [JsonProperty("address")]
    public string Address { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("vout")]
    public int Vout { get; set; }
}

public class ListTransactionsEntry
{
    [JsonProperty("account")]
    public string Account { get; set; }

    [JsonProperty("address")]
    public string Address { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("blockhash")]
    public string BlockHash { get; set; }

    [JsonProperty("blocktime")]
    public long BlockTime { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("confirmations")]
    public long Confirmations { get; set; }

    [JsonProperty("time")]
    public long Time { get; set; }

    [JsonProperty("timereceived")]
    public long TimeReceived { get; set; }

    [JsonProperty("txid")]
    public string TxId { get; set; }

    [JsonProperty("txtype")]
    public string TxType { get; set; }

    [JsonProperty("vout")]
    public int Vout { get; set; }
}

public class GetBalanceResponse
{
    [JsonProperty("balances")]
    public List<AccountBalance> Balances { get; set; }

    [JsonProperty("totalspendable")]
    public decimal TotalSpendable { get; set; }

    [JsonProperty("cumulativetotal")]
    public decimal CumulativeTotal { get; set; }

    [JsonProperty("totalunconfirmed")]
    public decimal TotalUnconfirmed { get; set; }
}

public class AccountBalance
{
    [JsonProperty("accountname")]
    public string AccountName { get; set; }

    [JsonProperty("spendable")]
    public decimal Spendable { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }

    [JsonProperty("unconfirmed")]
    public decimal Unconfirmed { get; set; }
}

public class ValidateAddressResponse
{
    [JsonProperty("isvalid")]
    public bool IsValid { get; set; }

    [JsonProperty("address")]
    public string Address { get; set; }

    [JsonProperty("ismine")]
    public bool IsMine { get; set; }
}
