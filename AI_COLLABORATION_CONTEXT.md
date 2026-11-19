# ORVA — AI Collaboration Context (Stage 5.6.0 Current State)

## Product & Non-Negotiables
- **Product:** ORVA — Pharmacy Billing & Inventory by **Rindle Works** (provisional studio name pending Indian trademark clearance).
- **Stack:** C# 7.3, `.NET Framework 4.8` (`net48`, `AnyCPU`, `Prefer32Bit=false`, `Nullable=disable`), WPF, `System.Data.SQLite.Core` 1.0.119, `Microsoft.Web.WebView2` 1.0.2903.40 (used only for `Home` and `About Us`), xUnit 2.6.6, Inno Setup 6 (`installer/ORVA.iss`).
- **Frozen Operational Constraints:**
  - Offline operational views keep the single warm-paper / deep-teal / vermilion light palette (`#F3EADA`, `#FBF7EF`, `#E7DCC6`, `#172C30`, `#1A1712`, `#FF4B1F`, `#1E3FA6`, `#00A05A`, `#D42A2A`).
  - Zero encryption, zero logins/roles, zero discounts, zero cloud lock-in, zero daily profit dashboards, zero unapproved hardcoded subscription prices, frozen `Domain/Billing/BillingContracts.cs`, 10 bundled SIL OFL Noto TTF fonts, bilingual English / বাংলা parity.

## Current Implementation Status (Stage 5.6.0 / v4.0.0)
1. **Database & Core Services (Untouched from Stage 4.8):** Schema `001_InitialSchema.sql` -> `002_BillingTaxConfig.sql` -> `003_PerformanceIndexes.sql` (`DatabaseMigrator.CurrentSchemaVersion = 3`, synced with `ShopBackupService.MaxSupportedSchemaVersion`). All billing/return/cancellation timestamps use `BillingInstant.Capture`, and `SalesReturnService` enforces `Preview == Header == SUM(Lines)`.
2. **Design & Architecture Specifications (`docs/design/`):**
   - `ORVA_SHOWCASE_DESIGN_CONSTITUTION.md` (Master Prompt 1)
   - `ORVA_SHOWCASE_EXPERIENCE_BLUEPRINT.md` (Master Prompt 2)
   - `ORVA_SHOWCASE_MOTION_SPEC.md` (Master Prompt 3)
   - `ORVA_SHOWCASE_TECHNICAL_PLAN.md` (Master Prompt 4)
   - `ORVA_4X_IMPLEMENTATION_REPORT.md` (Master Prompt 5 & 6 handoff report)
3. **Creative Showcase (`MainWindow`, `HomeView`, `AboutUsView`, `ShowcaseHtmlBuilder`):**
   - **Dual Visual Identity:** `Home` and `About Us` use the `#08090D` / `#101218` / `#F0F0EC` / `#8B7CFF` (`#B4A9FF`) creative showcase palette; `MainWindow.ApplyTopBarTheme` and `HighlightActiveNav` switch top bar and drawer accents to `#8B7CFF` on showcase routes and restore `#FF4B1F` on operational routes.
   - **Hardened WebView2 Delivery:** `HomeView` and `AboutUsView` render self-contained HTML via zero-DNS `NavigateToString` (embedding `apothecary_frieze_bw.jpg` as Base64 data URI), drop the `#08090D` loading veil inside `NavigationCompleted`, block external top-level navigation (`NavigationStarting`/`NewWindowRequested`), and validate `WebMessageReceived` actions against explicit allowlists.
   - **Signature Interactions:** Darkness-first CSS radial-masked lantern reveal (zero idle orbit, self-terminating `rAF`, smooth extinction on exit/blur), 5-layer reversible physical workspace folders with staggered 3-sheet lift, non-pinned continuous scroll-scrubbed horizontal frieze (`apothecary_frieze_bw.jpg` over counter-gliding `ORVA.`), and About Us 5-node interactive Bezier principles wave.
   - **Settings & Bilingual Parity:** `AboutUsView` reads support phone/email exclusively from `AppSettings` (omitting empty rows with zero hardcoded fallbacks), reads version/build dynamically from assembly metadata, preserves Rindle Works' provisional status, and provides full English/Bengali parity and `UiMotion.Enabled` reduced-motion support.
4. **Offline WPF Visual System, Startup, Installer & CI:** Preserved from Stage 4.8 (`App.xaml`, `OrvaFx.cs`, `OrvaProgressRing.cs`, `ShopSetupWindow` guard, `installer/ORVA.iss`, `.github/workflows/build-windows.yml`).
