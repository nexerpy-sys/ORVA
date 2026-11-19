# ORVA — Pharmacy Billing & Inventory

**Publisher:** Rindle Works *(provisional brand pending Indian trademark clearance)*  
**Version:** 4.0.0 (Stage 4.8 Release-Ready Source)  
**Platform:** Windows 10 / 11 Desktop (WPF, `.NET Framework 4.8`, C# 7.3)  
**Storage:** Local unencrypted SQLite (`System.Data.SQLite.Core` 1.0.119, `WAL` + `synchronous=FULL`)  
**Languages:** English & বাংলা (304 / 304 matched resource keys)

## Implemented Modules (Stages 1 through 4.8)

- **Stage 1–2 — Core Pharmacy Ledger:** Local SQLite schema (`001`–`003` with performance indexes), integer-paise (`long`) & thousandths (`long`) arithmetic, FEFO batch allocation, expiry/low-stock alerts, `StockData.json` v2 snapshots, and portable `.shopbackup` backup & restore.
- **Stage 3–3.7 — Billing, Returns, Records & Hardening:** Keyboard-first billing (`F2`, `F8`, `F9`, `Ctrl+B`, `Ctrl+Enter`, `Ctrl+P`, `Ctrl+N`), GSTIN Mod-36 validation, Sales Returns & Invoice Cancellation with exact header-to-line round-off reconciliation, CSV (formula-neutralized), typed XLSX, ASCII-safe PDF exports, native 58mm/80mm/A4/A5 printing with pre-measure hardware margins, subscription status + offline UPI QR, and `PerMonitorV2` DPI awareness.
- **Stage 4.0–4.8 — Obsidian Showcase, Push-Column Hamburger Shell & Offline WPF Polish:**
  - **Home & About Us:** Obsidian Black (`#0A0A0A`) + Stark White (`#FAFAFA`) showcase with a single Vermilion (`#FF4B1F`) accent on **Start Billing**, hosted in `Microsoft.Web.WebView2` (`%LocalAppData%\Rindle Works\ORVA\WebView2Cache`) with complete 1:1 native WPF fallbacks (including the 5-node Bezier principles timeline, concentric `OrvaProgressRing`, and bundled monochrome apothecary frieze).
  - **Zero-Airspace Navigation:** 2-column spring push-drawer (`OrvaSpring.cs`) with bespoke 24×24 vector icons on all 9 workspaces (`Home`, `Billing`, `Inventory`, `Invoices`, `Sales Returns`, `Records`, `Settings`, `Subscription`, `About Us`) and an adaptive Obsidian/Paper top bar.
  - **100% Offline Operational Views:** Custom offline WPF templates for `TextBox`, `ComboBox`, `CheckBox`, `TabControl`/`TabItem`, `DataGridRow`/`DataGridCell`, slim `ScrollBar`s, `OrvaFx.Spotlight`, and `OrvaFx.ValuePulse` on Billing Grand Total.
  - **Installer & CI:** `installer/ORVA.iss` verifies `.NET Framework 4.8` and Microsoft Edge WebView2 Runtime (auto-installing `MicrosoftEdgeWebview2Setup.exe` if bundled). `.github/workflows/build-windows.yml` compiles `ORVA.sln` (`net48`) and executes the xUnit suite on `windows-latest`.

## Building & Testing on Windows 10/11

```powershell
msbuild ORVA.sln /t:Restore /p:Configuration=Release
msbuild ORVA.sln /p:Configuration=Release /m
dotnet test tests/Orva.Billing.Tests/Orva.Billing.Tests.csproj -c Release --no-build
```

> **Note:** Source validation in Linux covers C# 7.3 lexical/delimiter hygiene, XML/XAML well-formedness, 304/304 EN/BN parity, live SQLite 3.40.1 `001->002->003` migrations, and asset bundling. Windows `msbuild`, xUnit execution, WebView2 rendering, and physical thermal/A4 printing must be verified on Windows 10/11 (or via `.github/workflows/build-windows.yml`).
