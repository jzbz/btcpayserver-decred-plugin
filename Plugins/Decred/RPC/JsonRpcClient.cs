using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Decred.RPC;

public class JsonRpcClient
{
    readonly HttpClient _httpClient;
    readonly Uri _rpcUri;
    int _nextId;

    public JsonRpcClient(Uri rpcUri, HttpClient httpClient)
    {
        _rpcUri = rpcUri;
        _httpClient = httpClient;
    }

    public async Task<T> SendCommandAsync<T>(string method, object[] parameters = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new JObject
        {
            ["jsonrpc"] = "1.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters != null ? JArray.FromObject(parameters) : new JArray()
        };

        var content = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_rpcUri, content, cancellationToken);
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        JObject responseJson;
        try
        {
            responseJson = JObject.Parse(responseString);
        }
        catch (JsonReaderException)
        {
            // dcrwallet returns JSON-RPC errors as JSON, but transport-level
            // failures (bad credentials, wrong endpoint) come back as plain text.
            var body = responseString.Length > 200 ? responseString[..200] : responseString;
            throw new JsonRpcException((int)response.StatusCode,
                $"Non-JSON response from wallet (HTTP {(int)response.StatusCode}): {body}");
        }

        if (responseJson["error"] != null && responseJson["error"].Type != JTokenType.Null)
        {
            var errorMessage = responseJson["error"]["message"]?.ToString() ?? "Unknown RPC error";
            var errorCode = responseJson["error"]["code"]?.Value<int>() ?? -1;
            throw new JsonRpcException(errorCode, errorMessage);
        }

        return responseJson["result"].ToObject<T>();
    }

    public Task SendCommandAsync(string method, object[] parameters = null,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync<JToken>(method, parameters, cancellationToken);
    }
}

public class JsonRpcException : Exception
{
    public int Code { get; }

    public JsonRpcException(int code, string message) : base(message)
    {
        Code = code;
    }
}
