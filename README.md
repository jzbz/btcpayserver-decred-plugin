# BTCPay Server Decred Plugin

A [BTCPay Server](https://github.com/btcpayserver/btcpayserver) plugin by the [Decred project](https://github.com/decred) that enables receiving payments via [Decred](https://decred.org/).

The plugin talks directly to `dcrwallet` (in SPV mode) via JSON-RPC. No dcrd node, NBitcoin, or NBXplorer required.

## Deployment

If you don't have BTCPay Server yet, follow the [official deployment guide](https://docs.btcpayserver.org/Docker/) to set it up with Docker.

If you already run BTCPay Server via [btcpayserver-docker](https://github.com/btcpayserver/btcpayserver-docker):

1. SSH into your BTCPay Server host.

2. Create the wallet. This only needs to be done once:

   ```bash
   cd "$BTCPAY_BASE_DIRECTORY/btcpayserver-docker"
   docker run -it --rm -v generated_dcr_wallet:/root/.dcrwallet \
     "${BTCPAY_DCR_IMAGE:-ghcr.io/decred/decred:2.1.5}" dcrwallet --create
   ```

   Set a passphrase and **save the seed phrase** - this is the wallet backup. To restore an existing wallet, answer "yes" when asked if you have an existing seed.

3. Enable Decred and start the wallet. The wallet passphrase must be written to the deployment's `.env` file (not just exported) so `dcrwallet` can unlock on every start. `btcpay-setup.sh` persists only its own known variables, but it re-reads `$BTCPAY_BASE_DIRECTORY/.env` on every (re)start - so a plain `export` of a custom variable does not survive, and the wallet container ends up with an empty passphrase:

   ```bash
   echo 'BTCPAY_DCR_WALLET_PASSPHRASE=your-wallet-passphrase' >> "$BTCPAY_BASE_DIRECTORY/.env"
   export BTCPAYGEN_CRYPTO2=dcr
   . btcpay-setup.sh -i
   ```

   Use the exact passphrase you set in step 2, and avoid `$`, backticks, or quotes in it (they break shell/compose interpolation).

   This starts a `dcrwallet` container in SPV mode alongside your existing stack. It will sync headers and blocks from the Decred P2P network.

   By default this pulls the official `ghcr.io/decred/decred` image. To use a different build (e.g. your own fork's image), `export BTCPAY_DCR_IMAGE=ghcr.io/<owner>/decred:2.1.5` before running setup - it is also honored by the wallet-creation command above.

4. Install the plugin from your BTCPay Server admin panel under **Server Settings > Plugins**. Search for "Decred" and click Install.

5. Go to **Store Settings > Decred** to verify the connection status. Once `dcrwallet` is synced, the store is ready to accept DCR.

6. Create an invoice and select Decred as the payment method. To withdraw funds, use the Send button on the Decred settings page.

### Manual deployment

If you are not using btcpayserver-docker, you need:

- BTCPay Server >= 2.1.0
- A running `dcrwallet` instance (SPV mode recommended: `dcrwallet --spv`)

Set these environment variables on your BTCPay Server instance:

| Variable | Description | Example |
|---|---|---|
| `BTCPAY_DCR_WALLET_URI` | dcrwallet RPC endpoint | `http://dcrwallet:9110` |
| `BTCPAY_DCR_RPC_USERNAME` | RPC username | `btcpay` |
| `BTCPAY_DCR_RPC_PASSWORD` | RPC password | `btcpay` |

Then install the plugin DLL manually by placing it in BTCPay Server's `Plugins` directory and restarting.

## Using dcrctl

The Docker container includes `dcrctl` for direct wallet management. Run commands via `docker exec`:

```bash
# Shorthand for running dcrctl against dcrwallet
alias dcrctl-wallet="docker exec btcpayserver_dcrwallet dcrctl --wallet --rpcuser=btcpay --rpcpass=btcpay"
```

Common commands:

```bash
# Check wallet balance
dcrctl-wallet getbalance

# Get a new receiving address
dcrctl-wallet getnewaddress

# List recent transactions
dcrctl-wallet listtransactions

# Rescan the blockchain (e.g. after importing keys or if transactions are missing)
dcrctl-wallet rescanwallet
```

## Docker image

The Docker image containing `dcrwallet` and `dcrctl` is published to GHCR. The `docker-fragment/` directory contains the compose fragments for btcpayserver-docker.

## Development

### Building

```bash
git clone --recursive https://github.com/user/btcpayserver-decred-plugin.git
cd btcpayserver-decred-plugin
dotnet build Plugins/Decred/BTCPayServer.Plugins.Decred.csproj
```

If you get `NETSDK1226: Prune Package data not found` errors, apply this workaround before building:

```bash
echo '<Project><PropertyGroup><AllowMissingPrunePackageData>true</AllowMissingPrunePackageData></PropertyGroup></Project>' > submodules/btcpayserver/Directory.Build.props
```

### Testing with the dcrdex simnet harness

```bash
# Start the harness (requires dcrd, dcrwallet, dcrctl in PATH)
cd /path/to/dcrdex/dex/testing/dcr && bash harness.sh

# In another terminal, run the integration tests
cd btcpayserver-decred-plugin
dotnet run --project Tests/HarnessTest.csproj
```

For full end-to-end testing (running BTCPayServer with the plugin, creating invoices, and verifying payments), see [docs/e2e-testing.md](docs/e2e-testing.md).

## License

[MIT](LICENSE.md)
