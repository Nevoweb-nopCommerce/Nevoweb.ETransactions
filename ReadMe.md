# Nevoweb.ETransactions

nopCommerce payment plugin for **ETransactions / Up2Pay** (PBX gateway).

## Features

- Redirect payment flow (customer is sent to Up2Pay payment page)
- Handles return and server-to-server notification callbacks
- Supports payment statuses:
  - `00000` → paid
  - `99999` → authorized
  - others → failed/canceled
- Optional callback source IP filtering
- Optional preproduction endpoint mode
- Supports additional handling fee (fixed or percentage)
- Multi-store setting overrides (standard nopCommerce behavior)

## Requirements

- nopCommerce 4.90.x
- .NET 9 SDK (for build/package)

## Plugin system name

`Payments.ETransactions`

## Build installation package

Run in PowerShell / Command Prompt **from inside the project directory** (so `global.json` is discovered and .NET 9 SDK is used):

```powershell
cd "D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions"
dotnet msbuild "Nevoweb.ETransactions.csproj" /t:PackagePlugin /p:Configuration=Release
```

Generated package:

- `D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions\bin\Packages\Nevoweb.ETransactions.Release.zip`

## Install in nopCommerce

1. Open **Admin → Configuration → Local plugins**
2. Click **Upload plugin or theme**
3. Upload `Nevoweb.ETransactions.Release.zip`
4. Install **ETransactions / Up2Pay**
5. Configure values in **Admin → Configuration → Payment methods**

## Required gateway settingsiisreset /stop
iisreset /start

Set these values from your bank/Up2Pay merchant account:

- `PBX_SITE`
- `PBX_RANG`
- `PBX_IDENTIFIANT`
- `HMAC key`
- Endpoint URLs (main/backup/preprod as needed)

## Callback endpoints

The plugin uses these routes:

- Return: `/Plugins/ETransactions/Return`
- Notify (IPN): `/Plugins/ETransactions/Notify`

Make sure your environment allows gateway callbacks to the notify URL.

## Pre-production test cards

These card numbers are valid on the **pre-production platform** for shared test accounts and for merchant accounts whose acquirer is a French bank. You may also use your own personal card on the test platform — it will not be charged.

> ℹ️ Source: *Verifone Solutions Paybox VAD / e-Commerce – Paramètres de test v8.0*
> ⚠️ Expiry date and CVV are **not validated** on the test platform — any future date and matching CVV format will be accepted.

### French Bank (CB)

| Description | Card number | Expiry | CVV |
|---|---|---|---|
| Standard Paybox test card | `1111 2222 3333 4444` | 12/22 | 123 |
| 3D-Secure enrolled card | `4012 0010 3714 1112` | 12/22 | 123 |
| Non 3D-Secure card | `4012 0010 3844 3335` | 12/22 | 123 |

### Atos Worldline (Belgium)

| Description | Card number | Expiry | CVV |
|---|---|---|---|
| Belgian Visa | `4236 8615 8842 3130` | 12/22 | 123 |
| Belgian Mastercard | `5476 8520 5684 3079` | 12/22 | 123 |
| Belgian Maestro | `6703 1111 2222 3334` | 12/22 | N/A |

### EMS

| Description | Card number | Expiry | CVV |
|---|---|---|---|
| Visa | `4012 0010 3714 1112` | 12/22 | 123 |
| Mastercard | `5135 1800 0000 001` | 12/22 | 123 |
| Maestro | `6703 1111 2222 3334` | 12/22 | N/A |

### Bancontact/Mistercash

Testing is not currently possible on the pre-production platform.

### American Express

| Description | Card number | Expiry | CVV |
|---|---|---|---|
| American Express | `3749 0740 3001 005` | 12/22 | 1234 |

### Sofinco

| Description | Card number | Expiry | Personal code |
|---|---|---|---|
| Sofinco | `5049 7703 0000 0098 545` | 12/22 | 0825 |

### JCB

| Description | Card number | Expiry | CVV |
|---|---|---|---|
| JCB | `3569 9900 1200 0112` | 12/22 | 123 |

**Pre-production checklist:**

- Set `Preproduction = true` in plugin configuration.
- Use your **Paybox pre-production credentials** for `PBX_SITE`, `PBX_RANG`, and `PBX_IDENTIFIANT` (not your production values).
- Ensure `PreprodUrl` points to `https://preprod-tpeweb.paybox.com/`.

## Building the Plugin

Always run `dotnet` build commands from **inside the repository directory** so that `global.json` is discovered and the correct .NET 9 SDK is used.

**Build and package only** (normal PowerShell, no elevation needed):

```powershell
cd "D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions"
dotnet msbuild "Nevoweb.ETransactions.csproj" /t:PackagePlugin /p:Configuration=Release
```

**Build, package, and deploy to the live website** (must run PowerShell **as Administrator** — `iisreset` requires elevation):

```powershell
cd "D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions"
dotnet msbuild "Nevoweb.ETransactions.csproj" /t:PackagePlugin /p:Configuration=Release /p:Deploy=true
```

> **Why does it compile Nop.Core, Nop.Services, etc.?**  
> The plugin has `<ProjectReference>` entries to those libraries. MSBuild always builds dependencies before the target project. After the first full build, subsequent builds are fast because unchanged projects are skipped.

> **Why this matters (SDK version):** Running `dotnet` from an unrelated directory (e.g. `C:\Users\…`) causes the CLI to miss the `global.json` at the repo root and fall back to the highest installed SDK — currently .NET 10 — which does not have the .NET 9 targeting packs. The error will look like:
>
> ```
> error NETSDK1127: The targeting pack Microsoft.NETCore.App is not installed.
> ```
>
> Changing into the repo directory first resolves this automatically via `rollForward: latestFeature` in `global.json`, picking `9.0.312`.

## IPN Security — RSA Signature Verification

The gateway signs each server-to-server IPN callback with an RSA-SHA1 signature sent in the `sign` parameter. The plugin verifies this signature **before** any order state change is made.

### How it works

- The `PBX_RETOUR` field always includes `sign:K` so the gateway returns the signature.
- On each IPN call the plugin reconstructs the signed message (all parameters except `sign`, in received order) and verifies it against the gateway's public key.
- If the signature is missing or invalid the callback is rejected with `KO` and logged.

### Public key files

The RSA public keys must be placed in:

```
Plugins/Payments.ETransactions/etc/pubkey.pem           ← RSA-1024, pre-production
Plugins/Payments.ETransactions/etc/pubkey_RSA_2048.pem  ← RSA-2048, production
```

**Source:** Download the official PHP module from the gateway portal — the `etc/` folder inside the archive contains both PEM files:

> 🔗 [Download PHP module — CA Mon Commerce / Up2Pay E-Transactions](https://www.ca-moncommerce.com/espace-client-mon-commerce/up2pay-e-transactions/telecharger-mes-modules/php/)

The files shipped with this plugin are a copy of those keys. If the gateway ever rotates its keys, replace the PEM files and redeploy — **no recompile is needed**.

### Disabling RSA validation (troubleshooting only)

A toggle is available in **Admin → Configuration → Payment methods → ETransactions → RSA Validation for IPN**.

> ⚠️ **Never disable RSA validation in production.** An attacker could send a forged IPN and trigger order state changes without a real payment.



- **Upload error about archive structure**
  - The zip must contain a single root plugin directory: `Payments.ETransactions`.
- **Payment remains pending**
  - Verify notify callback is reachable from gateway side.
  - Check PBX identifiers and HMAC key.
- **Callback rejected**
  - If IP validation is enabled, ensure gateway IPs are included in allowed list.