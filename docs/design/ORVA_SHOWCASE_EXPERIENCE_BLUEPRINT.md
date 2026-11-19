# ORVA 4.x — Home & About Us Experience Architecture (Blueprint)

**Document Status:** Master Prompt 2 of 5 Deliverable (Experience & Content Specification — No Application Code Modified)  
**Authoritative Baseline:** `stage4.8 ORVA.zip` (ORVA v4.0.0)  
**Governing Authorities:** `docs/design/ORVA_SHOWCASE_DESIGN_CONSTITUTION.md` (Prompt 1) & Master Prompt 6 (Engineering Authority & Locked Owner Decisions 3.1–3.3)  
**Scope:** Complete page architecture, section-by-section narrative, navigation relationship, responsive text wireframes, English/Bengali copy plan, and factual truth audit for `HomeView` and `AboutUsView`.

---

## 1. Architectural Relationship Between Home and About Us

Home and About Us share the exact graphite (`#08090D` / `#101218`), chalk (`#F0F0EC`), and restrained electric blue-violet (`#8B7CFF` / `#B4A9FF`) visual constitution, yet each page plays a distinct editorial role so they never feel like duplicates:

- **Home (`HomeView`) — Atmospheric Counter Threshold:** Opens with ORVA's signature darkness-first **cursor lantern stage**, anchors the user with a single quiet live readiness strip and immediate counter entry (`Start Billing`, `+ Add Stock`), unfolds a concise editorial identity statement, presents three **physically layered workspace folders**, and closes with the **scroll-controlled horizontal apothecary frieze**.
- **About Us (`AboutUsView`) — Quiet Studio Monograph:** Deliberately omits the Home lantern and folder shelf so those interactions stay rare. Opens with an `Instrument Serif` monumental statement on clean dark space, presents Rindle Works' honest two-paragraph story, guides the reader through the **five-node curved principles timeline**, displays settings-driven support and dynamic assembly metadata, and closes with the shared **scroll-controlled monochrome apothecary frieze**.

---

## 2. Home Experience (`HomeView`) — Section-by-Section Specification

### Section H1 — Opening Lantern Composition (Hero Stage)
- **Purpose & Key Message:** Establish ORVA's art-directed studio presence immediately upon launch while giving pharmacy staff zero-friction access to daily counter work (`Start Billing` and `+ Add Stock`).
- **Visual Composition & Hierarchy:**
  - Full-bleed `#08090D` canvas with zero boxed containers, zero KPI cards, and zero angle-bracket developer syntax (`< PHARMACY LEDGER />` and `morph-card` are removed).
  - Background layer: cohesive monochrome apothecary/ledger linework sitting nearly invisible (`opacity <= 0.04`) at rest, revealed only inside the pointer-driven neutral-white feathered lantern beam (`--lantern-core`).
  - Foreground left-weighted asymmetric composition (`max-width: 1120px`, generous right negative space so the lantern beam has room to breathe over dark artwork):
    1. Restrained single-line top coordinate: dynamic shop name (from `HomeShowcaseData.ShopName`) paired with current formatted date (`DateText`).
    2. Monumental `Space Grotesk` display headline (2 short lines, with one key word set in `Instrument Serif` italic for editorial contrast).
    3. Concise one-line subtitle framing ORVA for independent pharmacies.
    4. Action row: Primary **Start Billing** button (`#8B7CFF` fill, `#08090D` high-contrast label, `≥ 7:1` contrast) + Secondary **+ Add Stock** button (`1px solid #3F4454`, `#F0F0EC` label).
    5. Bottom hairline (`1px solid #2B2E38`) anchoring the **approved single-line readiness strip** (Owner Decision 3.3): displays existing local `HomeShowcaseData` only—usable medicines count, expiring ≤ 30 days count (only flagged subtly if `> 0`), and local backup status (`Saved` / `Due today` / `Not yet backed up`). Zero extra DB queries, zero KPI tiles, zero progress rings, zero technical jargon.
- **Copy Direction (EN / BN):**
  - **EN Headline:** `Built for the pace` / *`behind the counter.`*
  - **EN Subtitle:** `Fast counter billing, careful batch and expiry checks, and local records that stay under your roof.`
  - **EN CTAs:** `Start Billing →` | `+ Add Stock`
  - **EN Readiness Strip (single line):** `{ShopName}  ·  {UsableMedicines} medicines ready  ·  {ExpiringSoonCount} expiring within 30 days  ·  Backup: {BackupStatusText}`
  - **BN Headline:** `কাউন্টারের প্রতিদিনের` / *`গতির জন্য তৈরি।`*
  - **BN Subtitle:** `দ্রুত বিলিং, ব্যাচ ও মেয়াদের সতর্ক হিসাব এবং আপনার কম্পিউটারেই সুরক্ষিত দোকানের রেকর্ড।`
  - **BN CTAs:** `বিলিং শুরু করুন →` | `+ স্টক যোগ করুন`
  - **BN Readiness Strip (single line):** `{ShopName}  ·  {UsableMedicines}টি ওষুধ প্রস্তুত  ·  ৩০ দিনের মধ্যে মেয়াদ শেষ: {ExpiringSoonCount}  ·  ব্যাকআপ: {BackupStatusText}`
- **Asset Requirements:** Bespoke dark-ground monochrome apothecary/ledger vector linework SVG embedded locally (zero external image dependency for the lantern reveal).
- **Entry, Scroll & Exit Behaviour:** Headline enters once with a clean masked vertical reveal (disabled under reduced motion). Lantern activates smoothly on pointer enter, tracks with eased damping, extinguishes smoothly on pointer leave/blur, and pauses `requestAnimationFrame` work once scrolled out of viewport.
- **Height & Responsive Behaviour:** `min-height: calc(100vh - 58px)` (approx. `680–760 px` at `1360×760`, compressing gracefully to `580 px` at `1000×640`). At `728 px` narrow drawer-open width, the readiness strip wraps cleanly onto two compact lines without horizontal overflow.

### Section H2 — Editorial Identity Statement (What ORVA Is & Whom It Serves)
- **Purpose & Key Message:** Explain clearly and honestly what ORVA does for independent retail pharmacies without marketing fluff or fabricated claims.
- **Visual Composition & Hierarchy:**
  - `#101218` secondary canvas separated by `1px solid #2B2E38` top and bottom hairlines.
  - Asymmetric `4 / 8` editorial split (`grid-template-columns: 0.42fr 1fr`, `gap: 48px`):
    - Left column: calm architectural statement (`Clear bills. Careful stock.`) in `Space Grotesk` `h2` (`clamp(30px, 3.8vw, 48px)`).
    - Right column: two short, high-legibility `Plus Jakarta Sans` paragraphs (`max-width: 60ch`) plus a hairline-separated 3-item horizontal index of real everyday strengths (Local & Always Ready · Clean GST Invoices · Portable Shop Backups).
- **Copy Direction (EN / BN):**
  - **EN Left:** `Clear bills.` / `Careful stock.`
  - **EN Right:** `ORVA is built for independent retail pharmacies that need dependable counter software without clutter or cloud lock-in. Everyday billing, batch tracking, sales returns, and GST records run locally on your counter computer.` / `Open the navigation menu at any time—or press Ctrl+B—to move straight into billing.`
  - **BN Left:** `পরিষ্কার বিল।` / `সতর্ক স্টক।`
  - **BN Right:** `স্বাধীন ফার্মেসির প্রতিদিনের কাজের জন্য জটিলতাহীন ও নির্ভরযোগ্য ডেস্কটপ সফটওয়্যার হিসেবে ORVA তৈরি। বিলিং, ব্যাচ ও মেয়াদ তদারকি, সেলস রিটার্ন এবং GST রেকর্ড—সবকিছুই আপনার দোকানের কম্পিউটারেই চলে।` / `যেকোনো সময় উপরের বাম মেনু খুলুন—অথবা Ctrl+B চেপে সরাসরি বিলিংয়ে যান।`
- **Asset Requirements:** Pure typography and `1px` hairlines—intentional visual stillness between the lantern stage and the interactive folder shelf.
- **Entry, Scroll & Exit Behaviour:** Completely static once in view (no gratuitous fade-in animation on every line), providing visual rest.
- **Height & Responsive Behaviour:** Approx. `340–400 px` at `1360×760`; collapses to a single column below `820 px` (`728 px` drawer-open width).

### Section H3 — Physical Layered Folder Shelf (Workspace Entry & Practical Strengths)
- **Purpose & Key Message:** Replace generic SaaS feature cards with a tactile, studio-grade interactive shelf representing ORVA's three primary destinations (`Quick Billing`, `Expiry & Stock`, `About ORVA`).
- **Visual Composition & Hierarchy:**
  - `#08090D` deep canvas. Left-aligned section heading (`Built for the counter.` / `কাউন্টারের কাজের জন্য।`) above a 3-column shelf (`gap: 28px`) with generous top clearance (`56 px`) so fanned document sheets never clip.
  - Each folder is a physical 4-layer object:
    1. Recessed `#141720` matte backplate with chamfered upper-left index tab (`01`, `02`, `03`).
    2. **Three staggered chalk-paper document sheets** (`#DCDCD6`, `#E8E8E2`, `#F0F0EC`) tucked at distinct resting offsets/rotations, displaying grounded micro-copy (never fake metrics):
       - **Folder 01 (`start_billing`) — Quick Billing:** Sheet 1: `Keyboard & Barcode Ready`; Sheet 2: `Clean GST & Customer Receipts`; Sheet 3: `Fast Counter Checkout`.
       - **Folder 02 (`nav_inventory`) — Expiry & Stock:** Sheet 1: `Batch & Expiry Safeguards`; Sheet 2: `{ExpiringSoonCount} Expiring ≤ 30 Days` (live from `HomeShowcaseData`); Sheet 3: `Earliest-Expiry Stock Priority`.
       - **Folder 03 (`nav_about`) — About ORVA:** Sheet 1: `Rindle Works · Provisional Studio`; Sheet 2: `ORVA v{AppVersion}` (live metadata); Sheet 3: `Five Principles & Support`.
    3. Hinged `#181B24` front pocket flap carrying the folder title, one-line action cue, and directional arrow (`→`).
- **Copy Direction (EN / BN):** Full English and Bengali parity across all folder titles, tab numbers (`01`–`03` / `০১`–`০৩`), front action labels (`Start Billing` / `বিলিং শুরু করুন`, `Open Inventory` / `স্টক দেখুন`, `Read About Us` / `আমাদের কথা পড়ুন`), and all three inner sheet titles.
- **Asset Requirements:** Pure semantic HTML/CSS/SVG layered geometry (zero external images required).
- **Entry, Scroll & Exit Behaviour:** Hover or keyboard focus (`tabindex="0"`, `role="button"`) smoothly hinges the front flap forward and fans the three inner sheets with staggered timing; pointer leave, window blur, or focus blur smoothly reverses every layer back to its exact resting stack. Enter/Space/Click triggers the validated `postMessage` navigation action.
- **Height & Responsive Behaviour:** Approx. `480–520 px` at `1360×760`; 3 columns at `1360 px` and `1000 px`; gracefully switches to 1-column vertical stack with generous vertical gap (`44 px`) at `728 px` drawer-open width so fanned sheets never collide.

### Section H4 — Cinematic Scroll-Controlled Apothecary Frieze & Composed Closing
- **Purpose & Key Message:** Deliver the signature Reference Video 75300 scroll-controlled finale, anchoring ORVA's brand identity in authentic Indian pharmacy counter craft and offering clear onward navigation.
- **Visual Composition & Hierarchy:**
  - **Anchored dark footer bar (`#101218`):** Left side displays closing phrase (`See you at the counter.` / `কাউন্টারের প্রতিদিনের কাজে আপনার সাথে।`). Center/right displays restrained text links (`Start Billing`, `About Us`, `Back to top ↑`) and dynamic metadata (`RINDLE WORKS · ORVA v{AppVersion}`).
  - **Scroll-controlled frieze stage (`#F0F0EC` chalk-white band):**
    - Layer 1 (back): Giant low-contrast `ORVA.` display lettering (`rgba(8, 9, 13, 0.065)`).
    - Layer 2 (front): Wide monochrome `apothecary_frieze_bw.jpg` (`mix-blend-mode: multiply`, scaled slightly wider than viewport container so horizontal translation never exposes empty edges).
- **Asset Requirements:** Existing embedded `Orva.Assets.apothecary_frieze_bw.jpg` (already bundled locally as an embedded resource—zero network failure risk).
- **Entry, Scroll & Exit Behaviour:** Vertical scroll progress across the footer trigger smoothly drives horizontal translation of `apothecary_frieze_bw.jpg` from left-offset (beginning) through center (midpoint) to right-offset (end), while giant `ORVA.` counter-glides gently in the opposite direction. Reverse scrolling reverses the scene smoothly. Reduced motion locks both layers centered at `0%`.
- **Height & Responsive Behaviour:** Dark footer bar (`110–130 px`) + frieze stage (`260–340 px` scaled proportionally by viewport width).

---

## 3. About Us Experience (`AboutUsView`) — Section-by-Section Specification

### Section A1 — Monumental Editorial Hero
- **Purpose & Key Message:** Present Rindle Works and ORVA with quiet studio dignity—distinct from Home's lantern stage.
- **Visual Composition & Hierarchy:**
  - `#08090D` deep canvas with top hairline scroll-progress indicator (`#8B7CFF`, `1.5 px`).
  - Top metadata bar: `ORVA — ABOUT US` (left) and dynamic `RINDLE WORKS · V{AppVersion}` (right, from assembly metadata).
  - Monumental `Instrument Serif` headline (`clamp(44px, 6vw, 78px)`, weight `400`): `Made for the work behind the counter.`
  - Sub-tagline in `#B3B4BC`: `Clear bills. Careful stock. Your records, kept close.`
- **Copy Direction (EN / BN):** Preserves the approved Stage 4.8 English and Bengali hero and lead copy verbatim (`কাউন্টারের প্রতিদিনের কাজের জন্য তৈরি।` / `পরিষ্কার বিল। সতর্ক স্টক ব্যবস্থাপনা। আপনার রেকর্ড, আপনার কাছেই।`).
- **Height & Responsive Behaviour:** Approx. `300–340 px` at `1360×760`; clean full-width reflow down to `728 px`.

### Section A2 — Grounded Studio Story
- **Purpose & Key Message:** Tell the honest story of Rindle Works and ORVA in 2–3 grounded sentences with zero invented history, dates, or corporate claims.
- **Visual Composition & Hierarchy:**
  - Asymmetric `4 / 8` editorial split (`border-top: 1px solid #2B2E38`, `padding: 56px 0`):
    - Left column: `OUR STORY` / `আমাদের কথা` in `IBM Plex Mono` (`12px`).
    - Right column: Two calm paragraphs (`Plus Jakarta Sans`, `16.5px`, `max-width: 62ch`).
- **Copy Direction (EN / BN):** Preserves the exact Stage 4.8 verified story stating that **Rindle Works** is the provisional working studio name behind ORVA, built around quick billing, careful batch and expiry management, and local shop-controlled records.

### Section A3 — Five-Node Curved Principles Timeline
- **Purpose & Key Message:** Present ORVA's five enduring design principles along a continuous curved architectural wave (truthful principles, zero fabricated historical dates).
- **Visual Composition & Hierarchy:**
  - `#101218` secondary canvas (`border-top` & `border-bottom: 1px solid #2B2E38`).
  - Full-width SVG cubic Bezier curve (`viewBox="0 0 1000 96"`) with resting hairline track (`#2B2E38`), luminous `#8B7CFF` progress stroke, and 5 keyboard-focusable nodes (`tabindex="0"`, `role="button"`, `aria-pressed`).
  - Five aligned principle columns directly beneath the 5 nodes, each marked with a `1px` top rule that highlights to `#B4A9FF` and subtle `rgba(139, 124, 255, 0.10)` surface wash when active:
    1. `01 — Local by Design` (`০১ — স্থানীয় ব্যবস্থায়, ইন্টারনেট ছাড়াও`)
    2. `02 — Accurate to the Paise` (`০২ — পয়সা পর্যন্ত নির্ভুল হিসাব`)
    3. `03 — Every Batch Matters` (`০৩ — প্রতিটি ব্যাচ গুরুত্বপূর্ণ`) — *Recommended refinement: replace the internal acronym `FEFO` with plain language (`earliest-expiry-first stock selection` / `আগে মেয়াদ শেষ হওয়া ব্যাচ আগে বাছাই`) to honour the zero-jargon rule.*
    4. `04 — Your Records, Your Control` (`০৪ — আপনার রেকর্ড, আপনার নিয়ন্ত্রণে`)
    5. `05 — Made for Real Counters` (`০৫ — ফার্মেসির বাস্তব কাজের কথা ভেবে`)
- **Entry, Scroll & Exit Behaviour:** Scroll progress smoothly draws the `#8B7CFF` curve across Nodes 01–05; hovering, clicking, or focusing any node highlights it immediately; leaving the timeline section returns scroll control cleanly instead of permanently locking `userPinned = true`.
- **Responsive Behaviour:** 5 columns at `1360 px` and `1000 px`; cleanly transitions to a 2-column (or 1-column) stacked layout with numbered hairline rows at `728 px` drawer-open width.

### Section A4 — Support, Ownership, Dynamic Version Footer & Scroll Frieze
- **Purpose & Key Message:** Provide truthful support contacts (strictly from `AppSettings`), dynamic version/build metadata, and explicit provisional-brand notice, followed by the shared scroll-controlled frieze.
- **Visual Composition & Hierarchy:**
  - Two-column open hairline layout (`Support & Contact` left, `Product & Ownership` right — zero boxed cards):
    - **Support & Contact (strict Owner Decision 3.2):**
      - Reads `support.phones` and `support.email` from `AppSettings` with **empty string defaults** (hardcoded `"9933130541"` and `"nexerpy@gmail.com"` are removed).
      - If phone is configured → render Phone row. If blank → omit row completely.
      - If email is configured → render Email row. If blank → omit row completely.
      - If **both** are blank → omit both rows and render one restrained note (`Support contact details are not configured yet. Add them in Settings.` / `সহায়তার যোগাযোগের তথ্য এখনো সেটিংসে যুক্ত করা হয়নি।`).
    - **Product & Ownership:**
      - Product: `ORVA — Pharmacy Billing & Inventory`
      - Developed by: `Rindle Works`
      - Studio status: `Provisional studio name; Indian trademark clearance pending.`
      - Version & Build: dynamic `Assembly.GetExecutingAssembly().GetName().Version` (`{Major}.{Minor}.{Build}` and `{Revision}`).
      - Quick return links: `Back to Home` (`nav_home`) and `Start Billing` (`start_billing`) — requires adding `WebMessageReceived` handling to `AboutUsView.xaml.cs` (currently missing in Stage 4.8) using the exact same validated action allowlist as `HomeView`.
  - **Closing Scroll-Controlled Frieze Band:** Identical `#F0F0EC` scroll-scrubbed `apothecary_frieze_bw.jpg` + giant ghosted `ORVA.` composition as Home Section H4.

---

## 4. Navigation & Page Relationship

- **Primary App Navigation (Preserved):** The existing WPF zero-airspace 272 px spring push drawer (`MainWindow.xaml`) remains the authoritative navigation shell across all 9 routes. Startup destination (`ShowHome()`), `ShopSetupWindow` first-run guard, `ConfirmLeaveUnsavedBill()` protection, and `Ctrl+B` instant billing shortcut remain 100% untouched.
- **Showcase Chrome Alignment (Owner Decision 3.1):** In `MainWindow.ApplyTopBarTheme(bool darkShowcase)`, when `darkShowcase == true` (Home or About Us):
  - Top bar background `#08090D`, divider `#2B2E38`, brand & title `#F0F0EC`, muted eyebrow/date `#777B86`.
  - Top brand dot, third hamburger line, and active nav drawer left border switch from operational Vermilion (`#FF4B1F`) to electric blue-violet (`#8B7CFF`) / chalk (`#F0F0EC`).
  - Navigating to any operational screen (`Billing`, `Inventory`, `Invoices`, `Returns`, `Records`, `Settings`, `Subscription`) immediately restores warm paper (`#FBF7EF`), deep teal (`#172C30`), and operational Vermilion (`#FF4B1F`).
- **Direct Home ↔ About Us & Operational Links:**
  - From **Home**: Folder 03 (`About ORVA`) and Footer link (`About Us`) post `{ "action": "nav_about" }` → `MainWindow.ShowAboutUs()`. Primary CTA and Folder 01 post `{ "action": "start_billing" }` → `ShowBilling()`. `+ Add Stock` posts `{ "action": "add_stock" }` → `OpenQuickAddStockDialog()`. Folder 02 posts `{ "action": "nav_inventory" }` → `ShowInventory()`.
  - From **About Us**: Footer link (`Home`) posts `{ "action": "nav_home" }` → `ShowHome()`, and (`Start Billing`) posts `{ "action": "start_billing" }` → `ShowBilling()`.
- **Navigation States:**
  - **Resting:** `#B3B4BC` text, transparent underline.
  - **Hover:** `#F0F0EC` text, `1px` `#8B7CFF` underline (`160 ms ease-out`).
  - **Focus-visible:** `2px solid #B4A9FF` outline with `3px` offset.
  - **Pressed:** `opacity: 0.78`, `translateY(1px)` (`80 ms`).
  - **Active (in WPF drawer):** `#181B24` fill with `3px` `#8B7CFF` left border on showcase routes (`#FF4B1F` on operational routes).
- **Page Transition:** Brief `200 ms` opacity settle (`0 -> 1`) on initial WebView2 content mount behind the `#08090D` loading veil—never blocking input, never delaying billing navigation, and instant (`0 ms`) under `prefers-reduced-motion: reduce`.

---

## 5. Responsive Text Wireframes

### 5.1 Home (`HomeView`) Wireframes

#### A. Desktop Standard — `1360 × 760` (Drawer Closed, Content Width `1360 px`)
```text
+----------------------------------------------------------------------------------+
| [≡] ORVA·  | ORVA / HOME · Home                       19 NOV 2026 · READY LOCAL  |  <- WPF TopBar (#08090D, #8B7CFF accent)
+----------------------------------------------------------------------------------+
|                                                                                  |
|   {SHOP NAME}  ·  19 NOV 2026                                                    |
|                                                                                  |
|   Built for the pace                        (  subtle feathered lantern pool   ) |  <- H1: Lantern Stage (100vh)
|   behind the counter.                        (  revealing monochrome apothecary ) |     Dark #08090D ground
|                                               (  ink linework beneath cursor   ) |
|   Fast counter billing, careful batch and                                        |
|   expiry checks, and local records.                                              |
|                                                                                  |
|   [ Start Billing -> ]   [ + Add Stock ]                                         |
|                                                                                  |
|----------------------------------------------------------------------------------|
|   {ShopName} · 142 medicines ready · 3 expiring <=30d · Backup: Saved today      |  <- Single-line Readiness Strip
+==================================================================================+
|                                                                                  |
|   Clear bills.              ORVA is built for independent retail pharmacies...   |  <- H2: Asymmetric 4/8 Split (#101218)
|   Careful stock.            Everyday billing, batch tracking, returns, GST...    |     Static, calm editorial rest
|                             --------------------------------------------------   |
|                             Local & Always Ready  |  Clean GST  |  Local Backup  |
+==================================================================================+
|                                                                                  |
|   Built for the counter.                                                         |  <- H3: Physical Folder Shelf (#08090D)
|                                                                                  |
|    ___   [sheet3]            ___   [sheet3]            ___   [sheet3]            |     3-column 4-layer hinged folders
|   |01 | [sheet2]            |02 | [sheet2]            |03 | [sheet2]             |     Hover/focus fans 3 sheets upward
|   +-----------------+       +-----------------+       +-----------------+        |
|   | Quick Billing   |       | Expiry & Stock  |       | About ORVA      |        |
|   | Start Billing ->|       | Inventory     ->|       | Read More     ->|        |
|   +-----------------+       +-----------------+       +-----------------+        |
+==================================================================================+
|   See you at the counter.          Start Billing · About Us · Top ^ · v4.0.0     |  <- H4: Anchored Dark Footer (#101218)
+----------------------------------------------------------------------------------+
|                 O   R   V   A   .    (giant ghost watermark, counter-glide)      |  <- Chalk #F0F0EC Frieze Band
|    <<<<<  [=== monochrome apothecary_frieze_bw.jpg pans on vertical scroll ===]  |     Vertical scroll -> Horizontal pan
+----------------------------------------------------------------------------------+
```

#### B. Constrained Desktop — `1000 × 640` (Drawer Closed, Content Width `1000 px`)
```text
+---------------------------------------------------------------+
| [≡] ORVA· | HOME                           19 NOV 2026 · LOCAL|
+---------------------------------------------------------------+
|  {SHOP NAME} · 19 NOV 2026                                    |
|  Built for the pace             ( lantern beam reveals ink )  |  <- H1 compressed to ~560px
|  behind the counter.                                          |
|  Fast counter billing, careful batch & expiry checks...       |
|  [ Start Billing -> ]  [ + Add Stock ]                        |
|---------------------------------------------------------------|
|  {ShopName} · 142 ready · 3 expiring <=30d · Backup: Saved    |
+===============================================================+
|  Clear bills.       ORVA is built for independent retail...   |  <- H2 compact 4/8 split
|  Careful stock.     Local Ready | Clean GST | Local Backup    |
+===============================================================+
|  Built for the counter.                                       |  <- H3 stays 3-col (tighter 18px gap)
|  [01 Quick Billing]  [02 Expiry & Stock]  [03 About ORVA]     |
+===============================================================+
|  See you at the counter.        Billing · About · Top ^       |  <- H4 anchored bar + scaled frieze
|  [=== monochrome apothecary frieze panning on scroll ===]     |
+---------------------------------------------------------------+
```

#### C. Narrow WebView Content Width — `728 × 640` (`1000 × 640` Window with `272 px` Push Drawer Open)
```text
+---------------+-----------------------------------------------+
| NAV DRAWER    | [≡] ORVA· | HOME                    19 NOV 26 |
| (272 px)      +-----------------------------------------------+
| [Home] *      | {SHOP NAME} · 19 NOV 2026                     |
|  Billing      | Built for the pace                            |  <- H1 full-width single column
|  Inventory    | behind the counter.                           |     Lantern stays active behind type
|  Invoices     | [ Start Billing -> ]  [ + Add Stock ]         |
|  Returns      |-----------------------------------------------|
|  Records      | {ShopName} · 142 ready                        |  <- Readiness strip wraps to 2 clean lines
|  Settings     | 3 expiring <=30d · Backup: Saved              |
|  ---------    +===============================================+
|  Sub / About  | Clear bills. Careful stock.                   |  <- H2 stacks to 1 column
|  [EN | বাংলা] | ORVA is built for independent pharmacies...   |
|               +===============================================+
|               | Built for the counter.                        |  <- H3 stacks to 1 column (44px gap)
|               | [ 01 · Quick Billing  (3-sheet folder)    -> ]|
|               | [ 02 · Expiry & Stock (3-sheet folder)    -> ]|
|               | [ 03 · About ORVA     (3-sheet folder)    -> ]|
|               +===============================================+
|               | See you at the counter.  Billing · About · ^  |  <- H4 stacked footer + scaled frieze
|               | [=== monochrome frieze panning on scroll ===] |
+---------------+-----------------------------------------------+
```

### 5.2 About Us (`AboutUsView`) Wireframes

#### A. Desktop Standard — `1360 × 760` (`1360 px`)
```text
+----------------------------------------------------------------------------------+
| [≡] ORVA·  | ORVA / RINDLE WORKS · About Us           19 NOV 2026 · READY LOCAL  |
+----------------------------------------------------------------------------------+
|  ORVA — ABOUT US                                            RINDLE WORKS · V4.0.0|
|                                                                                  |
|  Made for the work behind the counter.                                           |  <- A1: Instrument Serif Hero
|  Clear bills. Careful stock. Your records, kept close.                           |
+----------------------------------------------------------------------------------+
|  OUR STORY            Rindle Works is the working studio name behind ORVA...     |  <- A2: 4/8 Grounded Story
|                       Our aim is simple: make essential shop operations easier...|
+==================================================================================+
|  THE PRINCIPLES BEHIND ORVA                                                      |  <- A3: 5-Node Curved Wave (#101218)
|   (01)~~~~~~~~~~(02)~~~~~~~~~~(03*)~~~~~~~~~~(04)~~~~~~~~~~(05)                  |     Luminous #8B7CFF progress stroke
|   --------      --------      --------      --------      --------               |
|   01 Local      02 Paise      03 Every      04 Your       05 Real                |     5 aligned principle columns
|   by Design     Accurate      Batch         Records       Counters               |
+==================================================================================+
|  SUPPORT & CONTACT                        PRODUCT & OWNERSHIP                    |  <- A4: 2-Column Hairline Metadata
|  Phone      (only if set in AppSettings)  Product       Pharmacy Billing & Inv.  |     Zero hardcoded fallback contacts
|  Email      (only if set in AppSettings)  Developed by  Rindle Works (Provisional)|
|  (or quiet Settings note if both empty)   Version       4.0.0   Build   {Rev}    |
|                                           [ Home ]   [ Start Billing -> ]        |
+----------------------------------------------------------------------------------+
|                 O   R   V   A   .    (giant ghost watermark, counter-glide)      |  <- Shared Scroll-Scrubbed Frieze
|    <<<<<  [=== monochrome apothecary_frieze_bw.jpg pans on vertical scroll ===]  |
+----------------------------------------------------------------------------------+
```

#### B. Constrained Desktop (`1000 × 640`) & Narrow Drawer-Open Width (`728 × 640`)
```text
At 1000 x 640:
  - A1 Hero & A2 Story retain 4/8 split; A3 Principles stays 5 columns with 14px gutters;
    A4 Support & Ownership stays 2 columns; Frieze scales proportionally.

At 728 x 640 (Drawer Open):
  - A1 Hero full-width; A2 Story stacks to 1 column;
  - A3 SVG wave compresses cleanly above a 2-column (or 1-column) numbered principle stack;
  - A4 Support & Ownership stacks to 1 column; Frieze scales cleanly with zero horizontal overflow.
```

---

## 6. English & Bengali Content Plan and Translation Discrepancies

### 6.1 Stage 4.8 Discrepancies Identified
- **Missing Bengali translations on Home (`ShowcaseHtmlBuilder.cs`):** In Stage 4.8, when `DATA.Language === 'bn'`, the hero headline (`< PHARMACY LEDGER />`), folder headers (`Quick Billing`, `Expiry & Stock`, `About ORVA`), folder tabs (`01 · COUNTER`, `02 · EXPIRY WATCH`, `03 · RINDLE WORKS`), `Built for the counter.`, and `EXP ≤30D` / `BKP` remain untranslated in English. Prompt 5 implementation must achieve **100% bilingual parity** across all visible text, `aria-label` attributes, tooltips, empty states, and native WPF fallbacks.
- **Verified WPF Resource Keys (`Strings.en.xaml` / `Strings.bn.xaml`):** Existing keys (`PageHomeTitle`: `Home` / `হোম`, `PageAboutTitle`: `About Us` / `আমাদের সম্পর্কে`, `QuickStartBilling`: `Start Billing` / `বিলিং শুরু করুন`, `QuickAddStock`: `+ Add Stock` / `+ স্টক যোগ করুন`) are already aligned and preserved.
- **Bengali Typography Rule:** When `DATA.Language === 'bn'`, set `<html lang="bn">`, apply `Noto Serif Bengali` / `Noto Sans Bengali` (bundled locally), increase heading line-height to `1.22` and body line-height to `1.78`, and disable negative `letter-spacing` so Bengali conjuncts (যুক্তাক্ষর) never clip or overlap.

---

## 7. Content & Truth Audit

Every user-visible factual statement planned for Home and About Us is audited below:

| Planned Statement / Data Field | Screen | Source / Verification Status |
|---|---|---|
| Product name: `ORVA — Pharmacy Billing & Inventory` | Home & About | **Confirmed by source/context** (`Orva.Desktop.csproj`, `AI_COLLABORATION_CONTEXT.md`). |
| Studio identity: `Rindle Works` (provisional; Indian trademark clearance pending) | Home & About | **Confirmed by source/context** (`AI_COLLABORATION_CONTEXT.md`, `AboutUsView.xaml.cs`). |
| Shop Name (`HomeShowcaseData.ShopName`) | Home | **Confirmed by source/context** (read from `App.Services.ShopRepository.GetProfile()`). |
| Usable medicines, expiring ≤ 30 days, backup status (`HomeShowcaseData`) | Home Readiness Strip & Folder 02 | **Confirmed by source/context** (existing `HomeView.GatherShowcaseData()`—zero extra DB queries). |
| Offline-first local billing, inventory, returns, GST invoices, `.shopbackup` | Home & About | **Confirmed by source/context** (`AI_COLLABORATION_CONTEXT.md`, `Services/*`). |
| Integer-paise precision for invoice and GST calculations | About Principle 02 | **Confirmed by source/context** (`InvoiceCalculator.cs`, `AboutUsView.xaml.cs`). |
| Keyboard-friendly billing, barcode scanning, English–Bengali support | Home & About Principle 05 | **Confirmed by source/context** (`BillingView`, `Strings.en/bn.xaml`, `MainWindow` `Ctrl+B`). |
| Application version & build number (`AppVersion`, `BuildNumber`) | Home & About | **Confirmed by source/context** (dynamic `Assembly.GetExecutingAssembly().GetName().Version`). |
| Support Phone (`support.phones`) & Support Email (`support.email`) | About Section A4 | **Requires settings value** (read from `AppSettings`; omit row if empty). |
| Hardcoded fallback phone `"9933130541"` & email `"nexerpy@gmail.com"` | About (Stage 4.8) | **Removed** (violates Owner Decision 3.2 & Shared Project Context). |
| Technical acronym `FEFO` in About Principle 03 | About Section A3 | **Requires owner approval** *(Recommended: replace with plain phrase `earliest-expiry-first stock selection` / `আগে মেয়াদ শেষ হওয়া ব্যাচ আগে বাছাই` to match zero-jargon guidance; keep `FEFO` only if owner prefers retaining exact Stage 4.8 About copy).* |
| `< PHARMACY LEDGER />`, `EXP ≤30D`, `BKP: OK`, `©2026` hardcoded copyright | Home (Stage 4.8) | **Removed** (replaced with staff-friendly bilingual copy and dynamic date/metadata). |
| Subscription prices, customer counts, testimonials, founding year, office address | Home & About | **Removed / Prohibited** (cannot be substantiated; strictly banned). |

---

## 8. Owner Checkpoints Before Prompt 3

All major architectural decisions were locked in Master Prompt 6 (Sections 3.1–3.3). Only **one minor editorial preference** warrants confirmation before Prompt 3 (Motion Spec):
1. **About Us Principle 03 wording (`FEFO`):** Confirm our recommendation to replace the acronym `FEFO` in About Us Principle 03 with plain-language `earliest-expiry-first stock selection` (`আগে মেয়াদ শেষ হওয়া ব্যাচ আগে বাছাই`), or let us know if you want to retain the exact Stage 4.8 About Us phrasing verbatim. *(Default if unchanged: plain-language wording for consistency with the zero-jargon rule.)*
