# Itoguruma configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## Resolution and locations

Environment variables override `src/Itoguruma.Server/appsettings.json`. Installer-generated runtime files are stored below `%LOCALAPPDATA%\Programs\Itoguruma` unless another installation directory is selected.

## Settings

| Environment variable / key | Required | Type | Default | Constraints |
| :--- | :---: | :--- | :--- | :--- |
| `ITOGURUMA_AUTH_TOKEN` | Yes | String | None | Secret bearer token; use a long random value. |
| `ITOGURUMA_URL` | No | Absolute HTTP URL | `http://127.0.0.1:47631` | Must remain loopback for the supported deployment. |
| `ITOGURUMA_DB` | No | File path | User Local Application Data database | Parent directory must be writable. |
| `ITOGURUMA_CONFIG_DIR` | No | Directory path | Installation `config` directory | Must be writable for generated client configuration. |
| `ITOGURUMA_LOG_DIR` | No | Directory path | Installation `logs` directory | Must be writable by the server process. |
| `ITOGURUMA_CR_ROOT` / `Itoguruma:CrRoot` | For CR delivery | Directory path | None | Must identify the shared CR root; files must be below `inbox/<target_project>/`. |

## Example

```powershell
$env:ITOGURUMA_AUTH_TOKEN = "<random-secret>"
$env:ITOGURUMA_URL = "http://127.0.0.1:47631"
$env:ITOGURUMA_DB = "$env:LOCALAPPDATA\Programs\Itoguruma\data\messages.db"
```
