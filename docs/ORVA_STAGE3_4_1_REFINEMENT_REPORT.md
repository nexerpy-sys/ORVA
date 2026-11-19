# ORVA — Stage 3.4.1 Refinement Report

**Release Archive:** `ORVA_Stage3_4_1_Refined.zip`

## Summary of Targeted Stage 3.4.1 Fixes

1. **High Priority — 1000px Width Overflow Fixes:**
   - **`InventoryView.xaml`:** Replaced rigid side-by-side 9-column toolbar and competing left/right `DockPanel` filter/action bar with responsive `WrapPanel`s so controls wrap cleanly at 1000×640 without clipping or colliding.
   - **`InvoicesView.xaml`:** Replaced 885px of fixed-width columns with proportional star widths (`1.2*`, `1.05*`, `1.35*`, `0.85*`, `0.75*`, `0.9*`, `1.0*`, `1.1*`) with sensible `MinWidth`s and `HorizontalScrollBarVisibility="Auto"`.
2. **Medium Priority — Wired Missing Startup Fade & Page Transitions (`MainWindow.xaml.cs`):**
   - Added missing `MainWindow_Loaded` handler calling `UiMotion.FadeWindow(this)` (preventing a `CS0103` compile error and activating the gated startup fade).
   - Updated `SetPage(...)` to call `OrvaNavigation.SetContent(PageHost, page)` instead of direct `PageHost.Content = page;` assignment.
   - Fixed `RefreshSubscriptionNotice()` to check `PageHost.Content` and updated subscription-locked `ModuleNoticeView` to display `"Subscription Renewal Required"`.
3. **Project Management — Removed Unapproved Hardcoded Subscription Prices (`SubscriptionView.xaml.cs`):**
   - Removed hardcoded `499 / 1399 / 4999 / 9999` amounts. Plan amounts now read from optional `AppSettings` keys (`subscription.price_monthly`, `subscription.price_quarterly`, `subscription.price_annual`, `subscription.price_onetime`), defaulting to blank until commercial pricing is approved.
4. **Editorial Visual Consistency & Bilingual Parity:**
   - Added matching archival Cobalt+Ink mastheads with `RegistrationMark` to `SalesReturnsView.xaml` (`04`) and `SettingsView.xaml` (`06`).
   - Localized all operational preferences labels in `SettingsView.xaml` (287/287 matched EN/BN resource keys).
   - Synchronized `README.md` and `AI_COLLABORATION_CONTEXT.md` through Stage 3.4.1, explicitly documenting the bundled `Noto Sans Mono` (offline) vs `IBM Plex Mono` choice and pending Windows-only verification.

## Verification Status

- **Linux Sandbox (0 Errors, 0 Warnings):** 23 XML/XAML/csproj files, 287/287 EN/BN keys, 84 C# 7.3 files, 0 system font overrides, 0 hardcoded subscription prices, 76/76 SQLite 3.40.1 SQL queries, and 10 bundled Noto TTF fonts + `orva.ico` verified inside the ZIP.
- **Windows 10/11 Required:** `ORVA\installer\BUILD_WINDOWS_RELEASE.ps1` (`msbuild`, `dotnet test`, Inno Setup), visual screenshot checks at 1360×760 and 1000×640 in English and বাংলা, and physical 58mm/80mm/A4/A5 printing & UPI QR scan checks.
