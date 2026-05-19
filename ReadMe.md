# WARNING: Do NOT use in production
This plugin is still under development.

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

Run in PowerShell / Command Prompt:

```bash
dotnet msbuild "D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions\Nevoweb.ETransactions.csproj" /t:PackagePlugin /p:Configuration=Release
```

Generated package:

- `D:\NEVOWEB\Project\nopCommerce\src\Plugins\Nevoweb.ETransactions\bin\Packages\Nevoweb.ETransactions.Release.zip`

## Install in nopCommerce

1. Open **Admin → Configuration → Local plugins**
2. Click **Upload plugin or theme**
3. Upload `Nevoweb.ETransactions.Release.zip`
4. Install **ETransactions / Up2Pay**
5. Configure values in **Admin → Configuration → Payment methods**

## Required gateway settings

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

## Troubleshooting

- **Upload error about archive structure**
  - The zip must contain a single root plugin directory: `Payments.ETransactions`.
- **Payment remains pending**
  - Verify notify callback is reachable from gateway side.
  - Check PBX identifiers and HMAC key.
- **Callback rejected**
  - If IP validation is enabled, ensure gateway IPs are included in allowed list.