# ORVA 4.x — Showcase Technical Architecture & Implementation Plan

**Document Status:** Master Prompt 4 of 5 Deliverable (Technical Plan — No Application Code Modified)  
**Authoritative Baseline:** `stage4.8 ORVA.zip` (ORVA v4.0.0, `.NET Framework 4.8`, C# 7.3, WPF, `Microsoft.Web.WebView2 1.0.2903.40`, `System.Data.SQLite.Core 1.0.119`)  
**Governing Authorities:**  
- `docs/design/ORVA_SHOWCASE_DESIGN_CONSTITUTION.md` (Prompt 1)  
- `docs/design/ORVA_SHOWCASE_EXPERIENCE_BLUEPRINT.md` (Prompt 2)  
- `docs/design/ORVA_SHOWCASE_MOTION_SPEC.md` (Prompt 3)  
- Master Prompt 6 (Engineering Execution, Code Quality & Verification)

---

## 1. Existing Stage 4.8 Architecture Map

### 1.1 Current Component & File Inventory

| Area | Inspected File(s) | Current Implementation |
|---|---|---|
| **Project & Stack** | `src/Orva.Desktop/Orva.Desktop.csproj` | SDK-style `Microsoft.NET.Sdk.WindowsDesktop`, `net48`, `LangVersion 7.3`, `Nullable=disable`, `AnyCPU`, `Prefer32Bit=false`. Packages: `System.Data.SQLite.Core 1.0.119`, `Microsoft.Web.WebView2 1.0.2903.40`. |
| **Window Chrome & Routing** | `src/Orva.Desktop/MainWindow.xaml`, `MainWindow.xaml.cs` | 1360×760 default (`MinWidth=1000`, `MinHeight=560`, `WindowState=Maximized`). Zero-airspace 2-column push drawer (`NavDrawer`, 272 px via `OrvaSpring`). `ShowHome()` on startup, `Ctrl+B` → `ShowBilling()`, `ConfirmLeaveUnsavedBill()` guard, `ApplyTopBarTheme(bool darkShowcase)` toggles top bar colors. Currently uses `{StaticResource Vermilion}` on top-bar dot, 3rd hamburger bar, and active nav border even when `darkShowcase == true`. |
| **Home Host & Fallback** | `src/Orva.Desktop/Views/HomeView.xaml`, `HomeView.xaml.cs` | Hosts `HomeWebView`, `#0A0A0A` `LoadingVeil`, and native WPF `OfflineFallbackPanel` (currently uses rounded KPI cards, `OrvaProgressRing`, and `#FF4B1F`). Initializes WebView2 in `%LocalAppData%\Rindle Works\ORVA\WebView2Cache`, gathers `HomeShowcaseData` on `Task.Run`, calls `NavigateToString(ShowcaseHtmlBuilder.BuildHomeHtml(data))`, handles `WebMessageReceived` JSON `{ "action": "..." }`. |
| **About Us Host & Fallback** | `src/Orva.Desktop/Views/AboutUsView.xaml`, `AboutUsView.xaml.cs` | Hosts `AboutWebView`, `#0A0A0A` `LoadingVeil`, and native WPF `OfflineAboutPanel` (with 5-node Bezier path and rounded cards). Reads `support.phones` (currently defaults to `"9933130541"`), `support.email` (currently defaults to `"nexerpy@gmail.com"`), and `Assembly.GetExecutingAssembly().GetName().Version`. **Missing `WebMessageReceived` handler** for `nav_home` / `start_billing` links. |
| **HTML / CSS / JS Generator** | `src/Orva.Desktop/Services/ShowcaseHtmlBuilder.cs` | Generates inline HTML/CSS/JS via `StringBuilder` (`BuildHomeHtml`, `BuildAboutHtml`), embedding `apothecary_frieze_bw.jpg` as Base64 data URI. Currently loads `Inter` / `JetBrains Mono` from Google Fonts CDN with no local Noto `@font-face` mapping. |
| **Bundled Assets & Motion** | `Assets/apothecary_frieze_bw.jpg`, `Assets/Fonts/*.ttf`, `Services/UiMotion.cs` | 10 SIL OFL Noto TTF fonts (`NotoSans`, `NotoSansBengali`, `NotoSansMono`, `NotoSerif`, `NotoSerifBengali` Regular & Bold) compiled as WPF `<Resource>`. `UiMotion.cs` tracks `SystemParameters.ClientAreaAnimation` for Windows reduced-motion preference. |
| **Installer & CI** | `installer/ORVA.iss`, `.github/workflows/build-windows.yml` | `ORVA.iss` checks `.NET 4.8` (`Release >= 528040`) and `IsWebView2Installed()` via EdgeUpdate registry keys, optionally runs bundled `MicrosoftEdgeWebview2Setup.exe`, and leaves native WPF fallback ready if absent. CI builds `ORVA.sln` in `Release` and runs `dotnet test tests/Orva.Billing.Tests/Orva.Billing.Tests.csproj`. |

### 1.2 Exact Scope of Files to Modify in Prompt 5

| Target File | Reason for Change | Protected Status |
|---|---|---|
| `src/Orva.Desktop/Orva.Desktop.csproj` | Add `<EmbeddedResource>` entries for the bundled Noto TTF fonts (so `WebResourceRequested` or local data URI can serve them cleanly to WebView2 offline without duplicating files on disk). | In scope (additive resource metadata only) |
| `src/Orva.Desktop/MainWindow.xaml` & `MainWindow.xaml.cs` | Update `ApplyTopBarTheme(bool darkShowcase)` and `HighlightActiveNav(string route)` so showcase routes (`home`, `about`) use `#8B7CFF` / `#F0F0EC` instead of `#FF4B1F` (Owner Decision 3.1), restoring `#FF4B1F` on operational routes. | Minimal chrome-only edit; routing, drawer, `Ctrl+B` untouched |
| `src/Orva.Desktop/Views/HomeView.xaml` & `HomeView.xaml.cs` | Align `LoadingVeil` (`#08090D`) and `OfflineFallbackPanel` to Constitution (remove KPI cards, progress rings, and `#FF4B1F`); add `ReducedMotion` flag from `UiMotion.Enabled`; enforce strict WebView2 navigation lock, `WebResourceRequested` virtual-origin handler, and validated action allowlist. | In scope (showcase view only) |
| `src/Orva.Desktop/Views/AboutUsView.xaml` & `AboutUsView.xaml.cs` | Remove hardcoded fallback phone/email defaults (Owner Decision 3.2); align `LoadingVeil` (`#08090D`) and `OfflineAboutPanel` to Constitution; add `ReducedMotion` flag, `WebResourceRequested` virtual-origin handler, and validated `WebMessageReceived` bridge (`nav_home`, `start_billing`). | In scope (showcase view only) |
| `src/Orva.Desktop/Services/ShowcaseHtmlBuilder.cs` | Implement full Prompt 1–3 Home & About Us composition, CSS tokens, bilingual EN/BN parity, local Noto `@font-face` fallbacks, bespoke lantern SVG, 5-layer physical folders, scroll-scrubbed frieze, and 5-node interactive Bezier timeline. | In scope (primary showcase builder) |
| `AI_COLLABORATION_CONTEXT.md`, `docs/design/ORVA_4X_IMPLEMENTATION_REPORT.md` | Keep documentation synchronized with Stage 5.x implementation state per Master Prompt 6 §13. | Documentation only |

**Strictly Untouched:** All operational views (`BillingView`, `InventoryView`, `InvoicesView`, `SalesReturnsView`, `RecordsView`, `SettingsView`, `SubscriptionView`, `ModuleNoticeView`), all modal dialogs, `App.xaml` operational styles, `Domain/*`, `Infrastructure/*`, `Services/Billing/*`, `Services/SalesReturnService.cs`, `Services/ShopBackupService.cs`, `Services/SubscriptionService.cs`, and `tests/Orva.Billing.Tests/*`.

---

## 2. Animation & Tooling Stack Selection

### 2.1 Architectural Decision: Keep Existing WPF + WebView2 + Vanilla HTML/CSS/JS
Consistent with Master Prompt 6 §7.1:
- **Do NOT introduce React, Node.js, Vite, or a separate JS build pipeline.** `ShowcaseHtmlBuilder.cs` already compiles cleanly inside `Orva.Desktop` (`net48`) with zero external build tooling.
- **Do NOT load external animation CDNs (GSAP, Anime.js, Motion, React Spring) at runtime** when the exact approved interactions execute with higher determinism and zero network dependency in self-contained ES2020 + CSS3:
  1. **Scroll-Scrubbed Frieze (`H4` / `A4`) & Bezier Wave (`A3`):** `IntersectionObserver` + passive `scroll` listener + frame-rate-independent `requestAnimationFrame` linear lerp (`--scrub-lag`) mutating `transform: translate3d(...)` and `stroke-dashoffset`. Reproduces GSAP ScrollTrigger `scrub: 1` behavior identically with zero external script download or CDN failure mode.
  2. **Lantern Reveal (`H1`):** CSS `mask-image: radial-gradient(...)` driven by `--lx`, `--ly`, `--lr`, `--la` custom properties updated inside a self-terminating `rAF` exponential lerp loop (`1 - Math.exp(-7.2 * dt)`).
  3. **Layered Folders (`H3`):** Pure CSS 3D `perspective(1000px)` + `transform: translate3d(...) rotate(...) scale(...)` + asymmetric `transition-delay` governed by a single `.is-open` / `:focus-visible` state, matching the physical Vengeance UI `Folder Preview` mechanics without importing React or Framer Motion.

### 2.2 Dependency & Licence Classification

| Dependency | Version / Source | Licence | Classification | Failure / Offline Fallback |
|---|---|---|---|---|
| `Microsoft.Web.WebView2` | `1.0.2903.40` (NuGet) | Microsoft WebView2 SDK License | **2. Bundled / Host** | Falls back cleanly to native WPF `OfflineFallbackPanel` / `OfflineAboutPanel`. |
| Bundled Noto TTFs (10 files) | `Assets/Fonts/*.ttf` | SIL Open Font License 1.1 | **2. Bundled locally** | Always available offline via local virtual host `https://orva.showcase/fonts/*.ttf`. |
| `apothecary_frieze_bw.jpg` | `Assets/apothecary_frieze_bw.jpg` | Project Asset | **2. Bundled locally** | Embedded resource served locally; `alt` + clean `#F0F0EC` band if missing. |
| Google Fonts (`Space Grotesk`, `Instrument Serif`, `Plus Jakarta Sans`, `IBM Plex Mono`) | `fonts.googleapis.com` (HTTPS) | SIL Open Font License 1.1 | **3. Optional enhancement** | `font-display: swap` + 2.5 s browser non-blocking load; immediate fallback to bundled local Noto TTFs (`Noto Serif`, `Noto Sans`, `Noto Sans Mono`). |
| Operationally Critical Showcase Dependencies | — | — | **4. EMPTY** | **Zero** showcase dependency can block startup, billing, or offline pharmacy work. |

---

## 3. WebView2 Hosting, Security & Graceful Failure Isolation

### 3.1 Virtual Host Mapping via `WebResourceRequested` (Zero Disk Clutter)
Instead of relying on `about:blank` (`NavigateToString`)—which restricts local font loading—or copying loose HTML/font files into `bin\Release`:
1. Both `HomeView` and `AboutUsView` register `CoreWebView2.AddWebResourceRequestedFilter("https://orva.showcase/*", CoreWebView2WebResourceContext.All)`.
2. Requests to `https://orva.showcase/home.html` or `https://orva.showcase/about.html` return the UTF-8 HTML stream generated by `ShowcaseHtmlBuilder`.
3. Requests to `https://orva.showcase/fonts/<name>.ttf` and `https://orva.showcase/assets/apothecary_frieze_bw.jpg` stream directly from `Assembly.GetExecutingAssembly().GetManifestResourceStream(...)` with proper `Content-Type` (`font/ttf`, `image/jpeg`) and `Cache-Control: max-age=86400`.
4. **Strict Navigation & Origin Lock:**
   - `NavigationStarting` cancels any navigation (`e.Cancel = true`) whose URI does not start with `https://orva.showcase/`.
   - `NewWindowRequested` sets `e.Handled = true` so no popup or external browser window can be spawned.
   - `Settings.AreDefaultContextMenusEnabled = false`, `AreDevToolsEnabled = false`, `IsStatusBarEnabled = false`, `AreHostObjectsAllowed = false`.
5. **Strict `postMessage` Allowlist:**
   - `HomeView` accepts only: `"start_billing"`, `"add_stock"`, `"nav_inventory"`, `"nav_about"`.
   - `AboutUsView` accepts only: `"nav_home"`, `"start_billing"`.
   - Any unknown action string or malformed JSON is ignored safely without throwing or touching files/SQLite.

### 3.2 Failure-Mode & Fallback Matrix

| Failure Mode | Detection / Mechanism | User-Facing & Operational Behaviour |
|---|---|---|
| **Internet unavailable / offline counter PC** | Google Fonts stylesheet fails or times out | Showcase renders immediately using locally bundled Noto TTFs; operational billing/SQLite never touches network. |
| **WebView2 Runtime absent / corrupted** | `EnsureCoreWebView2Async` throws or `CoreWebView2 == null` | `HomeView` / `AboutUsView` catches exception, logs via `App.LogFatal`, collapses `LoadingVeil`, and shows Constitution-styled native WPF `OfflineFallbackPanel` / `OfflineAboutPanel`. |
| **Showcase JS runtime error** | `window.onerror` / `unhandledrejection` listener inside HTML | Logs silently; all content remains static-visible (`opacity: 1`) with functional CTA buttons (`Start Billing`, `+ Add Stock`). |
| **Missing `apothecary_frieze_bw.jpg`** | `GetManifestResourceStream` returns `null` | Frieze `<img>` omitted cleanly; giant `ORVA.` ghost typography remains centered on `#F0F0EC` band without broken image icon. |

---

## 4. Settings, Contact & Metadata Contract

- **Data Escaping & Injection:** All dynamic C# values (`HomeShowcaseData` and `AboutShowcaseData`) are serialized solely through `JavaScriptSerializer` into a JSON `<script id="orva-data" type="application/json">` block (with `</` escaped as `<\/`) and parsed safely via `JSON.parse`—never raw string concatenation into HTML tags or JS expressions.
- **Support Contact Resolution (`AboutUsView.GatherAboutData()`):**
  - `SupportPhone = (App.Services.AppSettings.Get(SubscriptionService.SupportPhonesKey, string.Empty) ?? string.Empty).Trim();`
  - `SupportEmail = (App.Services.AppSettings.Get("support.email", string.Empty) ?? string.Empty).Trim();`
  - **Zero hardcoded fallback strings.** Empty values omit their row in both WebView2 HTML and native WPF `OfflineAboutPanel`.
- **Version & Build Metadata:** Read dynamically from `Assembly.GetExecutingAssembly().GetName().Version` (`AppVersion` = `Major.Minor.Build`, `BuildNumber` = `Revision`).
- **Reduced-Motion Integration:** `HomeShowcaseData.ReducedMotion` and `AboutShowcaseData.ReducedMotion` pass `!UiMotion.Enabled` (Windows `SystemParameters.ClientAreaAnimation`) alongside CSS `@media (prefers-reduced-motion: reduce)` so Windows Accessibility settings are honored inside WebView2 even if Chromium's flag lags.

---

## 5. Performance, Lifecycle & Responsive Compatibility

- **Non-Blocking Startup:** `HomeView` gathers `HomeShowcaseData` on `Task.Run` while `#08090D` `LoadingVeil` is visible, then initializes WebView2 asynchronously; `Ctrl+B` and the WPF nav drawer remain immediately responsive even while WebView2 initializes.
- **Lifecycle Cleanup:** `HomeView_Unloaded` and `AboutUsView_Unloaded` unsubscribe `WebMessageReceived`, `WebResourceRequested`, `NavigationStarting`, `NavigationCompleted`, and `NewWindowRequested`, then call `Dispose()` on the `WebView2` control to release native Edge processes and memory.
- **Window Resize, Minimize & Inactive Suspension:** Inside WebView2, `ResizeObserver` (debounced `120 ms`) updates cached geometry; `visibilitychange` and `window.blur` halt `rAF` loops, extinguish the lantern, and reset open folders.
- **Responsive Breakpoints:**
  - `>= 1200px` (`1360×760`, `1600×900`): Full 3-column folder shelf, wide 4/8 splits, `260px` lantern radius.
  - `900px – 1199px` (`1000×640` drawer closed): Compact vertical padding, 3-column folders (`18px` gap), `220px` lantern radius.
  - `< 900px` (`728×640` at `1000×640` with `272px` drawer open): Single-column folder stack (`44px` vertical clearance), stacked `4/8` splits, 2-column About Us principles, `±4%` frieze pan amplitude.

---

## 6. Operational Regression Protection Matrix

| Protected Subsystem | Verification / Regression Check |
|---|---|
| **Startup & Shop Setup** | First-run `ShopSetupWindow` guard → `MainWindow` → `ShowHome()` unchanged (`App.xaml.cs`). |
| **Billing Entry & `Ctrl+B`** | `Ctrl+B` shortcut, `Start Billing` CTA, and `ConfirmLeaveUnsavedBill()` work identically with zero network dependency. |
| **Billing / GST / FEFO / Returns** | `dotnet test tests/Orva.Billing.Tests/Orva.Billing.Tests.csproj` passes 100% with zero edits to `Services/Billing/*` or `Domain/*`. |
| **SQLite Schema & Backups** | `001`–`003` migrations, `ShopBackupService`, and `AutomaticBackupCoordinator` untouched. |
| **Operational Theme Restoration** | Navigating from `Home`/`About` → `Billing`/`Inventory`/`Settings` restores `#FBF7EF` paper, `#172C30` teal, and `#FF4B1F` vermilion on `TopBarBorder` and `NavDrawer`. |
| **Offline Operation** | Disconnecting network still boots ORVA, opens Home/About with local Noto TTFs + local frieze, and completes billing/printing normally. |

---

## 7. Ordered Implementation Batches (Authorised in Prompt 5)

| Batch | Scope & Exact Target Files | Prerequisites | Acceptance Gate |
|---|---|---|---|
| **Batch 0 · Baseline & Safety** | Inspect `stage4.8 ORVA.zip` checksum/count; verify untouched operational files; confirm baseline CI/test structure. | None | Baseline inventory recorded before first edit. |
| **Batch 1 · Showcase Shell, Chrome & Static Composition** | `Orva.Desktop.csproj` (embed Noto TTFs), `MainWindow.xaml/.cs` (chrome `#8B7CFF` on dark showcase), `HomeView` / `AboutUsView` `WebResourceRequested` virtual host + navigation lock + static `ShowcaseHtmlBuilder` HTML/CSS. | Batch 0 | Home & About render cleanly in `#08090D` / `#F0F0EC` / `#8B7CFF` offline & online; zero Vermilion on showcase chrome; operational screens unchanged. |
| **Batch 2 · Cinematic Scroll Frieze (`H4` / `A4`)** | `ShowcaseHtmlBuilder.cs` JS/CSS scroll-scrubbed horizontal `apothecary_frieze_bw.jpg` + counter-gliding `ORVA.` watermark. | Batch 1 | Vertical scroll smoothly pans frieze `-6% -> +6%`; reverse scroll reverses; zero edge clipping; static under reduced motion. |
| **Batch 3 · Darkness-First Lantern (`H1`)** | `ShowcaseHtmlBuilder.cs` monochrome apothecary SVG + feathered CSS `mask-image` + damped `rAF` + idle stop + keyboard/touch/reduced-motion fallbacks. | Batch 1 | Dark at rest; smooth follow; smooth extinction on exit/blur; zero idle orbit; `rAF` stops when settled. |
| **Batch 4 · 5-Layer Physical Folders (`H3`)** | `ShowcaseHtmlBuilder.cs` 3 semantic `<button>` folders with 3 staggered chalk sheets + hinged flap + inverse-stagger exit + touch/keyboard handling. | Batch 1 | Staggered fan open; exact resting return on 20 rapid sweeps, blur, and cancel; keyboard Tab/Enter/Space works. |
| **Batch 5 · About Us, Settings Contacts & EN/BN Parity** | `AboutUsView.xaml/.cs` (remove hardcoded phone/email defaults, add `WebMessageReceived`), `HomeView.xaml/.cs` + `ShowcaseHtmlBuilder.cs` (100% EN/BN parity, 5-node wave exit release). | Batch 1 | Missing settings omit contact rows; dynamic version/build shown; 100% EN/BN parity; Rindle Works provisional notice intact. |
| **Batch 6 · Native Fallbacks, Resilience & Polish** | `HomeView.xaml/.cs`, `AboutUsView.xaml/.cs` (`OfflineFallbackPanel` & `OfflineAboutPanel` aligned to `#08090D`/`#8B7CFF`, zero KPI cards/rings/Vermilion; `UiMotion.Enabled` bridge). | Batches 2–5 | Missing WebView2 fallback matches Constitution; `prefers-reduced-motion` + `UiMotion` respected; zero console errors. |
| **Batch 7 · Regression, Packaging & Handoff** | Full build/test check (reporting environment limits honestly), diff audit, `AI_COLLABORATION_CONTEXT.md` sync, `ORVA_4X_IMPLEMENTATION_REPORT.md`, verified `stage5.5.0_ORVA.zip`. | Batch 6 | Zero unintended operational edits; complete verified archive with SHA-256 and factual report. |

---

## 8. Architectural Blocker Check

**Zero architectural blockers found.** `stage4.8 ORVA.zip` already includes `Microsoft.Web.WebView2 1.0.2903.40`, `System.Web.Script.Serialization`, embedded `apothecary_frieze_bw.jpg`, 10 SIL OFL Noto TTF fonts, `UiMotion.Enabled`, and clean `WebView2` disposal lifecycles. All planned features in Prompts 1–4 are cleanly implementable within `.NET Framework 4.8` and C# 7.3 without upgrading frameworks or adding external JS dependencies.
