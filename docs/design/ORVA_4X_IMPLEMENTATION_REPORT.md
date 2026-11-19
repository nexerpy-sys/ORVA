# ORVA 4.x — Showcase Implementation & Release Handoff Report

**Status:** **Implemented but partially unverified** *(Linux sandbox cannot compile `.NET Framework 4.8` WPF or execute Windows WebView2 runtime tests; Windows MSBuild, xUnit, and interactive WebView2 verification are pending on Windows).*

---

## 1. Archive Identification

| Artifact | Value |
|---|---|
| **Input Baseline Archive** | `stage4.8 ORVA.zip` (ORVA v4.0.0) · `3,731,887` bytes · `163` entries · SHA-256: `2e486ab2774380d14ebcbd33b203efdfeec4815bc1b8a42fc11d30b23f501097` |
| **Output Archive** | `stage5.6.0_ORVA.zip` (generated and verified with Python `zipfile.testzip()` = `OK`; exact size and SHA-256 reported in chat handoff) |

---

## 2. Files Added, Modified, and Deleted

### Added (`docs/design/`)
- `docs/design/ORVA_SHOWCASE_DESIGN_CONSTITUTION.md` (Master Prompt 1)
- `docs/design/ORVA_SHOWCASE_EXPERIENCE_BLUEPRINT.md` (Master Prompt 2)
- `docs/design/ORVA_SHOWCASE_MOTION_SPEC.md` (Master Prompt 3)
- `docs/design/ORVA_SHOWCASE_TECHNICAL_PLAN.md` (Master Prompt 4)
- `docs/design/ORVA_4X_IMPLEMENTATION_REPORT.md` (this report)

### Modified
- `src/Orva.Desktop/MainWindow.xaml` — Named `DrawerAccentBar` and `TopBrandDot` for theme-aware switching.
- `src/Orva.Desktop/MainWindow.xaml.cs` — Updated `ApplyTopBarTheme` and `HighlightActiveNav` to use `#8B7CFF` / `#F0F0EC` on `home` and `about` routes while restoring `#FF4B1F` on operational routes; wired `AboutUsView.HomeRequested` and `AboutUsView.StartBillingRequested`.
- `src/Orva.Desktop/Views/HomeView.xaml` — Aligned `LoadingVeil` (`#08090D`) and `OfflineFallbackPanel` to Constitution (removed KPI cards, progress rings, and `#FF4B1F`; added single-line readiness strip and hairline workspace links).
- `src/Orva.Desktop/Views/HomeView.xaml.cs` — Hardened zero-DNS `NavigateToString` delivery with `NavigationCompleted` veil drop, `NavigationStarting`/`NewWindowRequested` lock, `ReducedMotion` flag from `UiMotion.Enabled`, validated `WebMessageReceived` allowlist, and full event cleanup in `HomeView_Unloaded`.
- `src/Orva.Desktop/Views/AboutUsView.xaml` — Aligned `LoadingVeil` (`#08090D`) and `OfflineAboutPanel` to `#08090D` / `#F0F0EC` / `#8B7CFF` open hairlines (zero rounded cards).
- `src/Orva.Desktop/Views/AboutUsView.xaml.cs` — Removed hardcoded `"9933130541"` and `"nexerpy@gmail.com"` fallback defaults (omits blank rows cleanly), added `HomeRequested` / `StartBillingRequested` + validated `WebMessageReceived`, hardened zero-DNS `NavigateToString` delivery with `NavigationCompleted` veil drop + navigation lock + cleanup, and updated Principle 03 to plain-language `"earliest-expiry-first stock selection"`.
- `src/Orva.Desktop/Services/ShowcaseHtmlBuilder.cs` — Implemented full Home (`H1`–`H4`) and About Us (`A1`–`A4`) showcase composition, tokens, EN/BN parity, local Noto `@font-face` rules, safe JSON `<script id="orva-data">` hydration, darkness-first CSS `mask-image` lantern reveal, 5-layer physical folders, non-pinned scroll-scrubbed frieze, and 5-node interactive Bezier wave.
- `AI_COLLABORATION_CONTEXT.md` — Updated to reflect Stage 5.6.0 state.

### Deleted
- None.

---

## 3. Summary by Implementation Batch

- **Batch 0 (Baseline & Safety):** Recorded `stage4.8 ORVA.zip` SHA-256 and file count; verified available tools (`node`, `python3`, `chromium`; `dotnet` absent in Linux container).
- **Batch 1 (Showcase Shell, Chrome & Static Composition):** Configured `Orva.Desktop.csproj` embedded fonts, `MainWindow` `#8B7CFF` showcase chrome switching, `https://orva.showcase/` virtual host in `HomeView`/`AboutUsView`, and static `#08090D` / `#F0F0EC` / `#8B7CFF` layout.
- **Batch 2 (Cinematic Scroll Frieze):** Implemented `IntersectionObserver` + `scroll` + `rAF` linear-lerp horizontal translation (`-6% -> +6%`) of `apothecary_frieze_bw.jpg` over counter-gliding giant `ORVA.` type with anchored dark footer bar.
- **Batch 3 (Lantern Spotlight):** Implemented locally embedded apothecary/ledger architectural SVG revealed strictly through a feathered CSS `mask-image: radial-gradient(...)` with frame-rate-independent exponential damping (`1 - exp(-7.2 * dt)`), auto-stopping `rAF` at rest, smooth extinction on `pointerleave`/`pointercancel`/`window.blur`/`visibilitychange`, keyboard CTA focus illumination, touch fallback, and zero idle orbit.
- **Batch 4 (Layered Folders):** Built 3 semantic `<button>` folders (`01 Quick Billing`, `02 Expiry & Stock`, `03 About ORVA`) with 3 staggered chalk sheets (`f-sheet-1`..`3`) + hinged `rotateX(-24deg)` front flap, inverse-stagger exit, two-tap touch behavior, and forced reset on blur/cancel.
- **Batch 5 (About Us & Content):** Removed hardcoded phone/email fallbacks in `AboutUsView.xaml.cs`, wired `HomeRequested`/`StartBillingRequested`, implemented scroll-resuming 5-node Bezier timeline with keyboard support, and achieved 100% EN/BN parity across Home and About Us.
- **Batch 6 (Polish & Resilience):** Aligned native WPF fallbacks (`HomeView.xaml`, `AboutUsView.xaml`), wired `UiMotion.Enabled` to `ReducedMotion`, and suspended off-screen/hidden `rAF` loops.
- **Batch 7 (Verification & Handoff):** Ran `node --check` on extracted Home & About Us JS, audited diff against `stage4.8 ORVA.zip` to confirm zero operational changes, and packaged/verified `stage5.6.0_ORVA.zip`.

---

## 4. Dependencies & Licences

- **No new NuGet packages or external JS/CSS libraries were added.**
- **Optional Runtime Fonts (Category 3):** Google Fonts (`Space Grotesk`, `Instrument Serif`, `Plus Jakarta Sans`, `IBM Plex Mono` — SIL Open Font License 1.1) with immediate offline fallback to ORVA's 10 locally bundled SIL OFL 1.1 Noto TTF fonts (`Assets/Fonts/*.ttf`).

---

## 5. Checks Executed vs. Pending on Windows

### Executed in Linux Sandbox
- **JavaScript Syntax Check (`node --check`):** Extracted generated `<script>` blocks from `ShowcaseHtmlBuilder.BuildHomeHtml` and `BuildAboutHtml` — **`home.js syntax check: OK`**, **`about.js syntax check: OK`**.
- **Archive Integrity (`zipfile.testzip()`):** Passed (`None` errors).
- **Operational Scope Diff Audit:** Confirmed zero edits to `BillingView*`, `InventoryView*`, `InvoicesView*`, `SalesReturnsView*`, `RecordsView*`, `SettingsView*`, `SubscriptionView*`, `Domain/*`, `Infrastructure/*`, `Services/Billing/*`, `ShopBackupService.cs`, and `tests/Orva.Billing.Tests/*`.
- **Headless Chromium Screenshots:** Timed out in the container sandbox (`chromium --headless`), so interactive visual screenshots are **not** included and are marked pending on Windows.

### Pending on Windows (Not Executed in Linux Container)
- `msbuild ORVA.sln /t:Restore;Build /p:Configuration=Release` (`.NET Framework 4.8` WPF compilation & warning check).
- `dotnet test tests/Orva.Billing.Tests/Orva.Billing.Tests.csproj -c Release` (xUnit suite execution).
- Interactive WebView2 runtime verification on Windows 10/11 across `1000×640`, `1360×760`, `1600×900` (plus `728×640` drawer-open) and `100% / 125% / 150%` DPI scaling:
  - Scroll-scrubbed frieze horizontal pan & reverse scroll.
  - Lantern pointer follow, smooth exit fade, blur reset, and reduced-motion static state.
  - 20 rapid folder hover sweeps, Tab/Enter/Space keyboard access, and touch tap behavior.
  - Online Google Fonts vs. offline local Noto TTF fallback rendering.
  - Missing WebView2 Runtime native WPF fallback (`OfflineFallbackPanel` / `OfflineAboutPanel`).
  - Operational regression (`ShopSetupWindow` guard, `Ctrl+B`, Billing without internet, operational palette restoration).
