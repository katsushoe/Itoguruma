# Itoguruma configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## Display language

The installer stores `Itoguruma.Language` in `config/appsettings.json`. Supported values are `en` and `ja`. The Viewer UI, application logs, and console messages use this setting. Run the installer with `-Language en` or `-Language ja` for unattended installation; when omitted, the installer prompts for a language.

Message diagnostics are written to the server log without message bodies, payloads, or authentication data. `[Messaging][Send]` records the message ID, sender, requested Project IDs, resolved inbox Agent IDs, thread, and type. `[Messaging][Lease]` records the inbox Agent ID, selected message IDs, filters, and lease expiry, including zero-message results. `[Messaging][Ack]` records the inbox Agent ID, message ID, and acknowledgement result. Use these events together to distinguish routing, leasing, and acknowledgement failures.

## Resolution and locations

Environment variables override `src/Itoguruma.Server/appsettings.json`. Installer-generated runtime files are stored below `C:\Itoguruma` unless another installation directory is selected.

## Settings

| Environment variable / key | Required | Type | Default | Constraints |
| :--- | :---: | :--- | :--- | :--- |
| `ITOGURUMA_AUTH_TOKEN` | Yes | String | None | Secret bearer token; use a long random value. |
| `ITOGURUMA_URL` | No | Absolute HTTP URL | `http://127.0.0.1:47631` | Must remain loopback for the supported deployment. |
| `ITOGURUMA_DB` | No | File path | User Local Application Data database | Parent directory must be writable. |
| `ITOGURUMA_CONFIG_DIR` | No | Directory path | Installation `config` directory | Must be writable for generated client configuration. |
| `ITOGURUMA_LOG_DIR` | No | Directory path | Installation `logs` directory | Must be writable by the server process. |
| `ITOGURUMA_CR_ROOT` / `Itoguruma:CrRoot` | For CR delivery | Directory path | None | Must identify the shared CR root; files must be below `inbox/<target_project>/`. |
| `ITOGURUMA_SINGLE_INSTANCE_WAIT_SECONDS` / `Itoguruma:SingleInstanceWaitSeconds` | No | Integer | `5` | Must be 0-60; waits for a stopping server to release its single-instance locks before restart. |

## Example

```powershell
$env:ITOGURUMA_AUTH_TOKEN = "<random-secret>"
$env:ITOGURUMA_URL = "http://127.0.0.1:47631"
$env:ITOGURUMA_DB = "C:\Itoguruma\data\messages.db"
```
