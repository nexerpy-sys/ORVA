# ORVA 4.x — Showcase Design Constitution & Art Direction

**Document Status:** Master Prompt 1 of 5 Deliverable (Design Authority — No Application Code Modified)  
**Authoritative Baseline Inspected:** `stage4.8 ORVA.zip` (ORVA v4.0.0, `.NET Framework 4.8`, WPF + WebView2 `1.0.2903.40`)  
**Scope:** Creative showcase screens only — `HomeView` (`Home`) and `AboutUsView` (`About Us`).  
**Authority:** Every subsequent specification (Experience Blueprint, Motion Spec, Technical Plan) and implementation batch must comply with this constitution. No implementation step may independently alter the palette, typography, composition rules, or product boundaries defined here.

---

## 1. Stage 4.8 Baseline Audit

### 1.1 Exact Files Inspected

| File Path (in `stage4.8 ORVA.zip`) | Role in Current Baseline |
|---|---|
| `AI_COLLABORATION_CONTEXT.md` | Documents Stage 4.8 / v4.0.0 frozen constraints, operational palette, and showcase summary. |
| `src/Orva.Desktop/Orva.Desktop.csproj` | Confirms `net48`, C# 7.3, `Microsoft.Web.WebView2` `1.0.2903.40`, `System.Data.SQLite.Core` `1.0.119`, embedded `apothecary_frieze_bw.jpg` / `apothecary_frieze.jpg`, and 10 bundled Noto TTF fonts. |
| `src/Orva.Desktop/App.xaml` | Defines operational palette brushes (`#F3EADA`, `#FBF7EF`, `#E7DCC6`, `#172C30`, `#1A1712`, `#FF4B1F`, `#00A05A`, `#D42A2A`), 11 inline `StreamGeometry` icons, and `UiBodyFont`/`UiHeadingFont`/`UiMonoFont` Noto pack URIs. |
| `src/Orva.Desktop/MainWindow.xaml` & `MainWindow.xaml.cs` | 1360×760 default (`MinWidth="1000"`, `MinHeight="560"`, `WindowState="Maximized"`), zero-airspace 2-column spring push drawer (`NavDrawer`, 272 px), adaptive `TopBarBorder` dark/light switching (`ApplyTopBarTheme`), startup route `ShowHome()`, `Ctrl+B` shortcut to `ShowBilling()`, and `HomeView` event wiring (`StartBillingRequested`, `AddStockRequested`, `NavigateRequested`). |
| `src/Orva.Desktop/Views/HomeView.xaml` & `HomeView.xaml.cs` | Hosts `HomeWebView` (`WebView2`), `#0A0A0A` `LoadingVeil`, and 1:1 native WPF `OfflineFallbackPanel`. Initializes WebView2 in `%LocalAppData%\Rindle Works\ORVA\WebView2Cache`, calls `ShowcaseHtmlBuilder.BuildHomeHtml(data)` via `NavigateToString`, and handles `WebMessageReceived` JSON `{ "action": "..." }`. |
| `src/Orva.Desktop/Views/AboutUsView.xaml` & `AboutUsView.xaml.cs` | Hosts `AboutWebView` (`WebView2`), `#0A0A0A` `LoadingVeil`, and 1:1 native WPF `OfflineAboutPanel` (with 5-node Bezier path). Reads `support.phones` (default `"9933130541"`), `support.email` (default `"nexerpy@gmail.com"`), and assembly version/build. |
| `src/Orva.Desktop/Services/ShowcaseHtmlBuilder.cs` | 916-line C# static class generating both Home and About Us HTML/CSS/JS strings inline via `StringBuilder`. Embeds `Orva.Assets.apothecary_frieze_bw.jpg` as a Base64 data URI. |
| `src/Orva.Desktop/Assets/apothecary_frieze_bw.jpg` | High-craft custom black-and-white ink illustration of an independent Indian pharmacist writing in a ledger behind a stocked apothecary counter (bottles, drawers, balance scale, mortar and pestle, wall calendar, street/bicycles behind). |
| `src/Orva.Desktop/Assets/Fonts/*.ttf` | 10 locally bundled SIL OFL fonts: `NotoSans`, `NotoSansBengali`, `NotoSansMono`, `NotoSerif`, `NotoSerifBengali` (Regular + Bold each). |

### 1.2 Page-by-Page Audit of Current Implementation

#### A. Home (`HomeView` + `ShowcaseHtmlBuilder.BuildHomeHtml`)
- **Current composition & section order:**
  1. `lantern-hero` (`min-height: 88vh`): `<canvas id="lanternCanvas">` behind `< PHARMACY LEDGER />` monumental headline, a centered 236×236 px 3D flipping square metric card (`morph-card`), a metadata bar (`©2026 RINDLE WORKS` / `[[ DATE · READY AT THE COUNTER ]]`), and two buttons (`Start Billing →` in `#FF4B1F`, `+ Add Stock` outlined).
  2. `split-sec`: 2-column text split (`COUNTER READY` / `Clear bills. Careful stock.` + 1 paragraph).
  3. `folders-sec`: 3-column grid of matte-black folders (`Quick Billing`, `Expiry & Stock`, `About ORVA`) wired via inline `onclick="send(...)"`.
  4. `footer-dark` + `footer-band`: Dark bar (`See you at the counter.` / `RINDLE WORKS · ORVA 4.0.0`) above a `#FAFAFA` white band containing ghosted `ORVA.` and `apothecary_frieze_bw.jpg` (`mix-blend-mode: multiply`).
- **Current colour tokens & typography:** `#0A0A0A` background, `#141414` surface, `#FAFAFA` foreground, `#8A8A8A` muted, `rgba(255,255,255,0.11)` rule, and operational Vermilion `#FF4B1F` on `Start Billing`. Fonts loaded via Google Fonts CDN: `Inter`, `JetBrains Mono`, `Noto Sans Bengali`.
- **Verified motion & interaction behaviour (what is actually wired):**
  - `lanternCanvas` **is connected**, but draws procedural dashes, plus-marks, and monospace tokens (`RX`, `BILL`, `STOCK`, `BATCH`, `GST`, `SAFE`, `ORVA`) inside a white radial gradient—and **autonomously orbits in a figure-eight after 1.5 s of idle** (`if (!ptr.active && now - ptr.lastMove > 1500)`). It does **not** extinguish to darkness on pointer leave and does **not** reveal bespoke artwork.
  - `cursor-ring` runs an unbounded `requestAnimationFrame` loop continuously even when the window is inactive or pointer is stationary.
  - `morph-card` rotates `rotateY(0 -> 180deg)` on `window.scrollY / 420`, resembling a generic 3D SaaS KPI flip-card rather than an editorial pharmacy composition.
  - `.folder` lifts only **two** flat sheets (`.folder-paper-rear`, `.folder-paper`) with CSS `:hover` transitions; there is no keyboard-focus equivalent (`tabindex` is missing on `.folder`), no touch/click reveal state, no pointer-cancel/blur reset, and no staggered 3+ sheet fan.
  - `footer-band` shifts `ghostOrva` (`-30 px`) and `friezeImg` (`+12 px`) **only on horizontal pointer hover** (`pointermove`). **Vertical scroll does NOT move the illustration horizontally**, missing the core interaction in Reference Video 75300.
  - Bengali translation is incomplete on Home: `< PHARMACY LEDGER />`, `EXP ≤30D`, `BKP`, folder titles (`Quick Billing`, `Expiry & Stock`, `About ORVA`), and `BUILT FOR THE COUNTER` remain English even when `DATA.Language === 'bn'`.
- **External dependencies & failure behaviour:** Loads Google Fonts stylesheet over HTTPS. If offline, browser silently falls back to `Segoe UI` and `Consolas` (ignoring ORVA's 10 locally bundled Noto TTF fonts). If WebView2 fails to initialize, `HomeView` shows `OfflineFallbackPanel` in native WPF, which currently uses rounded cards (`CornerRadius="10"`) and operational Vermilion (`#FF4B1F`) on `Start Billing` and `OfflineExpiryRing`.

#### B. About Us (`AboutUsView` + `ShowcaseHtmlBuilder.BuildAboutHtml`)
- **Current composition & section order:**
  1. `hero`: Top mono metadata (`ORVA — ABOUT US` / `RINDLE WORKS · V4.0.0`), `<h1>` (`Made for the work behind the counter.`), lead line (`Clear bills. Careful stock. Your records, kept close.`).
  2. `story`: 0.38fr / 1fr split (`OUR STORY` + two grounded Rindle Works paragraphs).
  3. `wave-sec`: 5-node SVG cubic Bezier curve (`waveGlow` stroke-dashoffset) above a 5-column grid of principles (`01 — LOCAL BY DESIGN` through `05 — MADE FOR REAL COUNTERS`).
  4. `meta-split`: 2-column Support & Contact / Product & Ownership key-value lists.
  5. `footer-band`: Same `#FAFAFA` monochrome frieze band (hover parallax only, no scroll-scrubbed horizontal motion).
- **Verified motion & interaction behaviour:**
  - `waveGlow` animates `stroke-dashoffset` on scroll (`prog * 5`) and on `mouseenter`/`click` of `.w-node` or `.p-item`. Once hovered/clicked, `userPinned = true` permanently disables scroll progression with no reset. Nodes lack keyboard focusability (`tabindex` / `Enter` / `Space`).
  - `AboutUsView.xaml.cs` lines 93–97 **hardcode fallback contact defaults** (`"9933130541"` and `"nexerpy@gmail.com"`) if `AppSettings` keys are empty—violating the Shared Project Context rule that missing settings values must omit the contact row or use an approved non-customer-facing empty state rather than displaying fallback/hardcoded defaults.
- **External dependencies & failure behaviour:** Same Google Fonts CDN dependency as Home; native WPF `OfflineAboutPanel` fallback works without internet.

### 1.3 Disposition Summary (Preserve / Improve / Replace / Remove)

| Element | Disposition | Rationale |
|---|---|---|
| `apothecary_frieze_bw.jpg` monochrome illustration | **Preserve & Elevate** | Authentic, bespoke Indian pharmacy ink art; ideal centerpiece for the scroll-controlled horizontal frieze sequence. |
| WebView2 host lifecycle (`_isLoading`, `Unloaded` disposal, `%LocalAppData%` cache, `OfflineFallbackPanel`, `postMessage` bridge) | **Preserve** | Solid, crash-safe WPF integration that keeps operational screens isolated. |
| About Us grounded English/Bengali story & 5 principles | **Preserve & Refine** | Honest, provisional Rindle Works copy with zero invented history; refine visual composition and keyboard access. |
| `< PHARMACY LEDGER />` angle-bracket hero & `morph-card` 3D KPI flip | **Remove** | Developer-portfolio cliché and generic dashboard-card motif; replace with monumental ORVA editorial composition. |
| Autonomous orbiting `lanternCanvas` with `RX`/`BILL`/`GST` procedural noise | **Replace** | Replace with a true darkness-first feathered lantern spotlight revealing curated monochrome apothecary artwork and extinguishing cleanly on exit. |
| Hover-only footer parallax | **Replace** | Replace with vertical-scroll-controlled horizontal frieze progression over counter-moving oversized ghost typography. |
| 2-sheet CSS-only `.folder` cards | **Replace** | Replace with physically layered 3+ sheet reversible folder stacks with staggered lift, rotation, depth shadows, and keyboard/tap access. |
| Vermilion `#FF4B1F` on Home showcase & `Inter` / `JetBrains Mono` CDN defaults | **Remove / Replace** | Violates dual-identity separation and typography directive; replace with graphite/chalk/electric blue-violet system and considered editorial font pairing. |
| Hardcoded support phone/email fallback strings in `AboutUsView.xaml.cs` | **Remove** | Missing settings must omit the row rather than displaying hardcoded fallback values. |

### 1.4 Observed Facts vs. Prior Report Mismatches
- **Baseline version mismatch (Observed Fact):** The Master Prompts reference a `Stage 4.6 ZIP` baseline and prior Stage 4.6 report. The actual uploaded baseline archive is `stage4.8 ORVA.zip` (version `4.0.0`, documented in `AI_COLLABORATION_CONTEXT.md` as Stage 4.8). No Stage 4.6 report exists in the archive; `ShowcaseHtmlBuilder.cs` still carries a stale `"Stage 4.5 Creative Peak Obsidian"` XML doc comment. Per owner direction, `stage4.8 ORVA.zip` is treated as the authoritative baseline.
- **Operational accent bleed (Observed Fact):** `AI_COLLABORATION_CONTEXT.md` (line 8) explicitly mandated Vermilion `#FF4B1F` on Home's Start Billing button, and `MainWindow.xaml` (lines 45, 97, 104) uses `{StaticResource Vermilion}` inside the top bar and nav drawer. This directly conflicts with the new Shared Project Context prohibition against `#FF4B1F` on showcase pages and is flagged in Section 6.

---

## 2. Showcase Art Direction System

### 2.1 Why This System Is Recognisably ORVA / Rindle Works (Not a Dark SaaS Template)

Generic dark SaaS pages rely on glowing purple gradient blobs, frosted glass cards, rounded Bento grids, stock 3D spheres, and `Inter` + neon cyan KPI counters. ORVA's showcase rejects all of those in favour of **editorial print-meets-cinema discipline**:
1. **Ink-on-Obsidian Craft:** The visual anchor is not abstract tech geometry—it is fine monochrome ink illustration (`apothecary_frieze_bw.jpg` and matching apothecary linework) paired with chalk-white editorial typography (`#F0F0EC`) on deep blue-black graphite (`#08090D`).
2. **Architectural Restraint:** Structure comes from 1 px dark-slate hairlines (`#2B2E38`), dramatic typographic scale contrast (12 px metadata beside 72–140 px display headlines), and generous unlit negative space.
3. **Physical Metaphors Over Digital Clutter:** Interactions behave like real objects on a dark studio table—a beam of neutral light sweeping across dark ink, physical paper sheets fanning out of a tabbed folder pocket, and a wide illustrated panorama gliding horizontally as you scroll.
4. **One Electric Accent, Used Sparingly:** Electric blue-violet (`#8B7CFF` / `#B4A9FF`) never fills large backgrounds or gradients; it appears only as crisp interactive focus, active hairline rules, active folder rim light, and active timeline progression.

### 2.2 Authoritative Design-Token Table

| Token Name | Hex / Value | Contrast vs. `#08090D` | Role & Strict Usage Rule |
|---|---|---|---|
| `--canvas-deep` | `#08090D` | — | Primary viewport canvas for Home and About Us. Deep cool obsidian-graphite. |
| `--canvas-secondary` | `#101218` | — | Alternating editorial band background and anchored footer header strip. |
| `--surface-raised` | `#181B24` | — | Physical folder backplate/pocket flap, subtle elevated interactive controls. Never a generic SaaS card wall. |
| `--surface-paper-sheet` | `#E8E8E2` / `#F0F0EC` | 16.5:1 / 17.8:1 | Physical document sheets inside folders and the stark monochrome frieze backdrop (`#F0F0EC`). |
| `--ink-primary` | `#F0F0EC` | 17.8:1 (AAA) | Monumental display headlines, primary prose headings, active labels. |
| `--ink-secondary` | `#B3B4BC` | 9.6:1 (AAA) | Editorial body paragraphs, principle descriptions, secondary statements. |
| `--ink-muted` | `#777B86` | 4.9:1 (AA) | Restrained metadata, coordinates, inactive structural indices (≥ 12 px only; use `#9094A0` where smaller or on `#181B24` to preserve ≥ 4.5:1). |
| `--rule-subtle` | `#2B2E38` | — | 1 px structural section dividers, table hairlines, folder resting edge borders. |
| `--rule-strong` | `#3F4454` | — | Hover/active neutral hairline dividers and secondary button borders. |
| `--accent-electric` | `#8B7CFF` | 6.1:1 (AA+) | Principal showcase interactive accent: primary CTA fill/border, active timeline stroke, active indicator. |
| `--accent-highlight` | `#B4A9FF` | 9.9:1 (AAA) | High-contrast text links, keyboard `:focus-visible` outlines (2 px), active folder top bevel, active node core. |
| `--accent-tint-subtle` | `rgba(139, 124, 255, 0.12)` | — | Subtle hover wash on active timeline column or navigation item. Never a full-section gradient. |
| `--lantern-core` | `rgba(246, 246, 242, 0.18)` | — | Neutral optical light pool for the cursor lantern reveal. Pure light—must never shift into amber/orange brand colour. |
| `--frieze-ink` | `#08090D` | — | Text/ink multiply contrast inside the `#F0F0EC` monochrome frieze band; ghost watermark `ORVA.` at `rgba(8, 9, 13, 0.065)`. |

### 2.3 Typography System & Considered Font Pairing

**Explicit departure from defaults:** `Inter` and `JetBrains Mono` are retired from the showcase. The showcase uses a three-role editorial pairing—all **SIL Open Font License 1.1** (commercially safe for online Google Fonts delivery or local bundling)—backed by ORVA's 10 locally bundled SIL OFL Noto TTF fonts so offline rendering remains crisp and bilingual English/Bengali parity is unbroken.

| Typography Role | Primary Online Face (SIL OFL 1.1) | Local / Offline Fallback (Bundled in `Assets/Fonts/`) | Intended Usage |
|---|---|---|---|
| **Display — Architectural** | `Space Grotesk` (500, 600, 700) | `Noto Sans` (Bold) → `Segoe UI` | Monumental Home hero headline, section titles (`h2`), giant ghosted `ORVA.` footer lettering. Distinctive geometric ink-traps give studio authority without SaaS sameness. |
| **Display — Editorial Accent** | `Instrument Serif` (400 Regular & Italic) | `Noto Serif` (Regular) → `Georgia` | Sparse editorial emphasis: About Us monumental headline or single italic pull-phrase. Never bolded (only weight 400). |
| **Body — Editorial Prose** | `Plus Jakarta Sans` (400, 500, 600) | `Noto Sans` (Regular/Bold) → `Segoe UI` | All narrative copy, principle descriptions, folder document excerpts, and navigation labels. |
| **Micro / Metadata** | `IBM Plex Mono` (400, 500) | `Noto Sans Mono` (Regular/Bold) → `Consolas` | Strictly limited to version/build numbers, principle indices (`01`–`05`), live counter numerals, and footer coordinates. Never used for decorative fake-terminal badges. |
| **Bengali Script Parity** | `Noto Sans Bengali` & `Noto Serif Bengali` | Bundled `NotoSansBengali-*.ttf` & `NotoSerifBengali-*.ttf` | Applied seamlessly via `lang="bn"` font stack so Bengali headlines use `Noto Serif Bengali` / `Noto Sans Bengali` Bold with relaxed line-height (`1.22` display, `1.75` body) and zero negative letter-spacing. |

#### Type Scale, Measure & Spacing Rules

| Role | Size (`clamp`) | Line Height (EN / BN) | Letter Spacing (EN / BN) | Max Measure |
|---|---|---|---|---|
| Giant Ghost Watermark (`ORVA.`) | `clamp(96px, 18vw, 220px)` | `0.90` | `+0.06em` | Full bleed |
| Hero Monumental `h1` | `clamp(44px, 6.2vw, 84px)` | `0.98` / `1.18` | `-0.03em` / `0` | `16ch`–`20ch` (`text-wrap: balance`) |
| Section Heading `h2` | `clamp(30px, 3.8vw, 48px)` | `1.08` / `1.22` | `-0.02em` / `0` | `24ch` |
| Subheading / Folder Title `h3` | `20px`–`24px` | `1.25` / `1.35` | `-0.01em` / `0` | `28ch` |
| Lead Statement | `18px`–`20px` | `1.55` / `1.70` | `0` | `56ch` |
| Body Copy | `15.5px`–`16.5px` | `1.68` / `1.78` | `0` | `64ch` |
| Metadata / Micro Label | `12px` floor (never `< 12px`) | `1.40` | `+0.08em` / `0` | Single line (`white-space: nowrap`) |

### 2.4 Composition Principles

- **Scale Contrast & Asymmetry:** Pair monumental left-aligned or offset headlines with compact metadata anchors and asymmetric `4/8` or `5/7` editorial splits rather than centered symmetrical SaaS stacks.
- **Deliberate Negative Space:** Target 40–50% open canvas across sections. Section vertical rhythm uses generous breathing room (`88px`–`120px` desktop, scaling proportionally to `56px`–`72px` at `1000×640`).
- **Open Hairline Architecture:** Group related items with `1px solid #2B2E38` horizontal rules and grid alignment—never repeated rounded card boxes.
- **Stillness Balancing Motion:** At most one primary animated focal point is active in any viewport height. Anchored headings, navigation bars, and body prose remain still while the lantern, active folder, or horizontal frieze moves.
- **Desktop-First Framing:** Layouts must compose cleanly at `1000×640`, `1360×760`, and `1600×900` (and 100%/125%/150% Windows DPI scaling), accounting for the 272 px push-drawer open state (`728 px` minimum content width when drawer opens at `1000 px` window width).

### 2.5 Illustration & Image Treatment

- **Existing Monochrome Frieze (`apothecary_frieze_bw.jpg`):** Preserved as ORVA's signature illustrated centerpiece in the scroll-controlled footer composition. Rendered on the chalk-white `#F0F0EC` band with `mix-blend-mode: multiply` so the black ink linework floats cleanly over the giant low-contrast `ORVA.` ghost typography without a rectangular photo edge.
- **Lantern Stage Artwork:** Rather than random procedural dashes/tokens, the dark lantern canvas reveals cohesive monochrome apothecary/ledger linework (clean vector engraving or monochrome plate) that sits nearly invisible (`opacity <= 0.05`) in darkness until illuminated by the soft neutral-white pointer beam.
- **Edge & Radius Discipline:** Sharp `0px` architectural framing for full-bleed bands and illustrations; restrained `8px`–`10px` radius only on physical folder tabs/sheets and primary interactive buttons where tactile geometry warrants it.

### 2.6 Borders, Shadows, Masks & Interaction States

- **Borders & Rules:** `1px solid #2B2E38` at rest; `1px solid #3F4454` on subtle hover; `1px solid #B4A9FF` on active/focused showcase elements.
- **Shadows:** Reserved strictly for physical depth between overlapping layers (folder sheets lifting over one another and over the backplate: `0 14px 32px rgba(0, 0, 0, 0.55)`). Never used as neon glow under cards.
- **Masks & Spotlight:** The lantern uses a smooth multi-stop radial mask (`0%` clear core → `55%` gradual feather → `100%` transparent) driven by eased pointer coordinates. On `pointerleave`, window blur, or navigation away, the light fades smoothly to `opacity: 0`.
- **Keyboard & Focus States:** Every interactive control (buttons, folders, timeline nodes, navigation links) must be keyboard-reachable (`tabindex="0"` or native `<button>`/`<a>`) with a visible `2px solid #B4A9FF` focus ring (`outline-offset: 3px`). Hover-only interactions never conceal mandatory information.

---

## 3. Prohibited Design Patterns & Do / Don't Examples

The following patterns are **explicitly prohibited** on `HomeView` and `AboutUsView`:

1. **Operational Pharmacy Palette Leakage:** No warm archival paper (`#F3EADA`, `#FBF7EF`, `#E7DCC6`), no operational deep teal (`#172C30`), and no operational vermilion (`#FF4B1F`) as a showcase colour or button accent.
2. **Generic AI / Dark SaaS Templates:** No Bento-box grids of identical rounded cards, no glassmorphism (`backdrop-filter: blur()` frosted panels), no multi-colour mesh gradients, no floating ambient orbs/blobs, no decorative particle fields, and no gratuitous 3D spinning KPI cubes (`morph-card` is retired).
3. **Developer-Portfolio Clichés:** No `< PHARMACY LEDGER />` HTML-tag headlines, no fake terminal brackets (`[[ ... ]]`), no `Inter` + `JetBrains Mono` default pairing, and no autonomous screensaver spotlight orbits.
4. **Motion Overload:** No fade-in-up animation on every heading/paragraph, no continuous `requestAnimationFrame` loops when off-screen or window-blurred, no jarring spring bounce, and no hover states that get stuck after fast pointer exits.
5. **Fabricated Content or Jargon:** No fake testimonials, customer counts, awards, founding dates, founder biographies, office addresses, hardcoded subscription prices, hardcoded fallback support phone/email strings, or internal technical jargon (`SQLite`, `WAL`, `SKU`, `FEFO` on Home—plain staff language only).

### Concrete Do / Don't Matrix

| Area | DO (Approved Constitution) | DON'T (Rejected Pattern) |
|---|---|---|
| **Home Opening** | Open on deep `#08090D` graphite with monumental `Space Grotesk` typography and a dark-first lantern beam that reveals monochrome apothecary inkwork under the cursor. | Don't open with `< PHARMACY LEDGER />`, a spinning 3D metric cube, or an autonomously orbiting white flashlight over random `RX`/`GST` text. |
| **Action Emphasis** | Use restrained electric blue-violet (`#8B7CFF`) with high-contrast text (`#08090D` or `#F0F0EC` verified ≥ 4.5:1) for the primary showcase action and active interactive highlights. | Don't paint showcase buttons or rings with operational Vermilion (`#FF4B1F`) or rainbow neon gradients. |
| **Workspace Links** | Present key ORVA areas as physical, multi-sheet layered folders that fan out with staggered depth on hover/focus and settle cleanly back to rest on exit. | Don't replace the folders with three flat rounded cards that merely brighten their border on hover. |
| **Footer Composition** | Anchor the dark footer header while vertical scroll smoothly translates `apothecary_frieze_bw.jpg` horizontally across a chalk-white band over counter-moving giant ghosted `ORVA.` type. | Don't limit the frieze to a 12 px mouse-hover nudge or replace it with a standard multi-column SaaS link footer. |
| **About Us & Contacts** | State clearly that Rindle Works is provisional (Indian trademark clearance pending), pull version/build from assembly metadata, and omit any support contact row whose setting is empty. | Don't invent a founding year/team story, and don't silently display hardcoded fallback phone numbers or emails when settings are blank. |

---

## 4. Product & Visual Boundary Contract

### 4.1 In-Scope (Creative Showcase Only)
- `src/Orva.Desktop/Views/HomeView.xaml` & `HomeView.xaml.cs` (including native WPF fallback alignment to the new graphite/chalk/electric-violet palette).
- `src/Orva.Desktop/Views/AboutUsView.xaml` & `AboutUsView.xaml.cs` (including settings-driven empty-contact omission and native WPF fallback alignment).
- `src/Orva.Desktop/Services/ShowcaseHtmlBuilder.cs` (or dedicated local showcase web assets if approved in Prompt 4).
- `src/Orva.Desktop/MainWindow.xaml` & `MainWindow.xaml.cs` **only** where `ApplyTopBarTheme(bool darkShowcase)` styles the top bar while viewing Home/About (subject to Owner Decision #1 in Section 5; zero changes to routing, startup destination, `Ctrl+B` shortcut, or drawer structure).

### 4.2 Strictly Out-of-Scope (Frozen Operational Application)
The following screens, styles, and subsystems **must not be redesigned or modified**:
- **Operational Views:** `BillingView`, `InventoryView`, `InvoicesView`, `SalesReturnsView`, `RecordsView`, `SettingsView`, `SubscriptionView`, `ModuleNoticeView`, and all modal windows (`AddStockWindow`, `EditMedicineWindow`, `ImportCatalogueWindow`, `ImportStockWindow`, `RestoreBackupWindow`, `ShopSetupWindow`, `StockAdjustmentWindow`, `OrvaConfirmDialog`).
- **Operational Visual Identity:** Warm archival paper (`#F3EADA`, `#FBF7EF`, `#E7DCC6`), deep teal (`#172C30`), ink (`#1A1712`), vermilion (`#FF4B1F`), success (`#00A05A`), danger (`#D42A2A`), and offline WPF templates in `App.xaml`, `OrvaFx.cs`, `OrvaProgressRing.cs`.
- **Business Engine & Data:** `Domain/Billing/BillingContracts.cs`, `Services/Billing/*` (GST, `InvoiceCalculator`, `FefoAllocator`, `BillingService`), `SalesReturnService`, `InvoiceCancellationService`, `StockEntryService`, `StockAdjustmentService`, `ReportingService`, `ShopBackupService`, `AutomaticBackupCoordinator`, `SubscriptionService`, SQLite migrations `001`–`003`, and `tests/Orva.Billing.Tests/*`.
- **Operational Guarantees:** Offline-first execution without internet, zero network calls in billing/inventory paths, startup flow (`ShopSetupWindow` guard → `MainWindow` → `ShowHome()`), and `Ctrl+B` instant billing entry.

---

## 5. Evidence Ledger & Unresolved Questions Before Prompt 2

### 5.1 Observed Facts vs. Assumptions vs. Recommendations
- **Observed Facts:**
  - Uploaded baseline is `stage4.8 ORVA.zip` (not Stage 4.6), running `.NET Framework 4.8` / WPF / `WebView2 1.0.2903.40`, with plain inline HTML/CSS/JS in `ShowcaseHtmlBuilder.cs` (no React or Node build pipeline).
  - 10 SIL OFL Noto TTF fonts and `apothecary_frieze_bw.jpg` are already bundled in `Orva.Desktop.csproj`.
  - Current `AboutUsView.xaml.cs` hardcodes `"9933130541"` and `"nexerpy@gmail.com"` as default fallbacks when settings are blank.
  - Current `MainWindow.xaml` embeds Vermilion (`#FF4B1F`) accents in the top bar dot, third hamburger bar, and active nav drawer border even when `ApplyTopBarTheme(true)` is active.
- **Assumptions:**
  - `stage4.8 ORVA.zip` is the accepted starting point for all five master prompts (confirmed by owner).
  - External Google Fonts loading is permitted when online for the showcase, with local bundled Noto TTF fonts serving as the immediate offline fallback.
- **Recommendations:**
  - Adopt the refined `#08090D` / `#F0F0EC` / `#8B7CFF` (`#B4A9FF`) token system and `Space Grotesk` + `Instrument Serif` + `Plus Jakarta Sans` + `IBM Plex Mono` (+ bundled Noto Bengali) pairing as locked in Section 2.
  - Keep the showcase on clean HTML/CSS/JavaScript (plus GSAP/ScrollTrigger if justified in Prompt 4) rather than converting the `.NET 4.8` project to a React build pipeline solely to copy a React component library.

### 5.2 Unresolved Source Questions for Owner Confirmation
1. **Top Bar & Drawer Chrome Accent (`MainWindow.xaml`):** While `Home` or `About Us` is active, should the WPF `TopBarBorder` dot, third hamburger line, and active nav indicator switch from Vermilion (`#FF4B1F`) to the showcase's electric blue-violet (`#8B7CFF`) / chalk white (`#F0F0EC`) so zero vermilion appears on showcase screens, or should the outer WPF window chrome remain vermilion-accented across the whole app? *(Recommended: switch `TopBarBorder` accents to `#8B7CFF` / `#F0F0EC` when `darkShowcase == true`, keeping Vermilion strictly on operational screens.)*
2. **Empty Support Contact Treatment (`AboutUsView`):** When `support.phones` or `support.email` is empty in `AppSettings`, should that contact row be completely omitted (cleanest), or display a non-customer-facing notice such as "Configured in Settings"? *(Recommended: omit the empty row cleanly, and hide the Support block's contact list with a single quiet note only if both are blank.)*
3. **Home Live Counter Telemetry:** Stage 4.8 currently passes live `UsableMedicines`, `ExpiringSoonCount`, `LowStockCount`, and `BackupFresh` from SQLite into `HomeView`. Should Prompt 2 retain a quiet, non-dashboard editorial status line (e.g., shop name + backup/stock readiness) or keep Home purely brand/narrative with navigation into Billing, Inventory, and About Us? *(Recommended: keep a restrained single-line live readiness strip—never KPI cards or progress rings—so pharmacy staff get immediate utility without turning Home into a dashboard.)*
