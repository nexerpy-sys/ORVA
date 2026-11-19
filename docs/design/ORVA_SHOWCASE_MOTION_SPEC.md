# ORVA 4.x — Showcase Interaction & Animation Specification

**Document Status:** Master Prompt 3 of 5 Deliverable (Motion Engineering Specification — No Application Code Modified)  
**Authoritative Baseline:** `stage4.8 ORVA.zip` (ORVA v4.0.0)  
**Governing Authorities:**  
- `docs/design/ORVA_SHOWCASE_DESIGN_CONSTITUTION.md` (Prompt 1)  
- `docs/design/ORVA_SHOWCASE_EXPERIENCE_BLUEPRINT.md` (Prompt 2)  
- Master Prompt 6 (Engineering Execution, Code Quality & Verification)  
- Reference Videos `75300`, `75302`, `75303` (binding behavioural references)

**Scope:** Measurable initial, active, dwell, exit, keyboard, touch, reduced-motion, and performance specifications for every animation and interaction on `HomeView` and `AboutUsView`.

---

## 1. Motion Token System (Scroll-Scrubbed vs. Time-Based)

ORVA's showcase strictly separates **scroll-scrubbed** motion (position directly governed by scroll progress, zero independent bounce) from **time-based** motion (triggered by pointer, keyboard, or load events with calibrated deceleration). Arbitrary elastic bounce and continuous idle loops are prohibited.

| Token | Value | Motion Class | Assigned Interactions |
|---|---|---|---|
| `--dur-micro` | `140ms` | Time-based | Link underline, arrow nudge, focus ring appearance, pressed state (`80ms`). |
| `--dur-folder-in` | `380ms` | Time-based | Folder front flap hinge (`rotateX`) and 3-sheet staggered lift/fan on entry. |
| `--dur-folder-out` | `320ms` | Time-based | Inverse-staggered folder sheet retraction and flap close on exit (slightly snappier than entry so fast sweeps feel crisp). |
| `--dur-page-enter` | `200ms` | Time-based | Initial WebView2 mount opacity settle after `#08090D` loading veil lifts. |
| `--dur-reveal` | `640ms` | Time-based | One-time masked vertical reveal of Hero display lines on initial load. |
| `--dur-lantern-in` | `260ms` | Time-based | Lantern mask opacity ramp from `0` to `1` on pointer enter. |
| `--dur-lantern-out` | `420ms` | Time-based | Lantern mask smooth extinction (`1 -> 0`) + `14%` radius contraction on exit/blur. |
| `--lantern-lambda` | `7.2 s⁻¹` | Frame-rate-independent lerp | Exponential pointer follow (`1 - Math.exp(-7.2 * dt)` ≈ `0.11` at 60 Hz, identical feel on 60/120/144 Hz monitors). |
| `--scrub-lag` | `1.0 s` (GSAP) or `8.5 s⁻¹` (native lerp) | Scroll-scrubbed | Vertical-to-horizontal frieze pan and About Us Bezier wave progress (`ease: "none"` on progress mapping). |
| `--ease-editorial` | `cubic-bezier(0.22, 1, 0.36, 1)` | Deceleration curve | Headline mask reveal, page enter, link states, lantern fade. Zero overshoot. |
| `--ease-folder-open` | `cubic-bezier(0.20, 0.90, 0.25, 1.04)` | Controlled physical lift | Subtle 4% tactile paper lift on folder open—never a cartoon bounce. |
| `--ease-folder-close` | `cubic-bezier(0.32, 0, 0.20, 1)` | Clean settle | Zero-overshoot return to exact resting geometry. |

---

## 2. Signature Effect 1 — Cinematic Scroll-Controlled Apothecary Frieze (`H4` & `A4`)

### 2.1 Visual Architecture & Layering
The closing composition turns the footer into a composed horizontal panorama controlled by vertical scroll (Reference Video 75300):
- **Anchored Header Band (`#101218`, `z-index: 4`):** Sits immediately above the frieze band with closing copy (`See you at the counter.`), navigation links, and dynamic version metadata. Its text and links **do not move horizontally** while the frieze glides beneath.
- **Frieze Stage (`#F0F0EC` chalk-white ground, `overflow: hidden`, `position: relative`):**
  - **Layer A (`z-index: 1`, background):** Giant low-contrast display word `ORVA.` in `Space Grotesk` Bold (`clamp(96px, 18vw, 220px)`, `color: rgba(8, 9, 13, 0.065)`, `user-select: none`, `pointer-events: none`).
  - **Layer B (`z-index: 2`, foreground):** Embedded monochrome `apothecary_frieze_bw.jpg` (`mix-blend-mode: multiply`, `width: 116%`, `max-width: none`, centered via `-8%` base horizontal offset so a `±6%` travel range never exposes empty side edges).

### 2.2 Pinning Strategy Decision
- **Strategy: Non-pinning continuous scrubbed traversal.**
- **Rationale:** Inside a Windows desktop WebView2 host that must operate cleanly at `1000×640`, `1360×760`, `1600×900`, and `728×640` (drawer open), scroll-pinning a footer section creates fragile synthetic scroll spacers, risks scroll-trapping on mouse wheels with coarse notch steps, and breaks cleanly navigating back to top. A non-pinned scrubbed traverse (`start: "top 95%"`, `end: "bottom 100%"` of viewport) binds `p ∈ [0, 1]` directly to the real scroll position as the footer enters and settles at the bottom of the page—continuous, reversible, and immune to resize glitches.

### 2.3 Exact Scroll Progress States (`p ∈ [0.0, 1.0]`)

| State | Scroll Progress `p` | `apothecary_frieze_bw.jpg` Transform | Giant Ghost `ORVA.` Transform | Anchored Dark Header |
|---|---|---|---|---|
| **Pre-entry / Initial** | `p = 0.0` (frieze top enters bottom 95% of viewport) | `translate3d(-6.0%, 0, 0)` | `translate3d(calc(-50% + 3.5%), 0, 0)` | Stationary (`0px`), full opacity |
| **Midpoint** | `p = 0.5` (frieze centered in lower viewport) | `translate3d(0.0%, 0, 0)` | `translate3d(-50%, 0, 0)` | Stationary (`0px`), full opacity |
| **Final / Resting End** | `p = 1.0` (page scrolled to bottom) | `translate3d(+6.0%, 0, 0)` | `translate3d(calc(-50% - 3.5%), 0, 0)` | Stationary (`0px`), full opacity |
| **Reverse Scroll** | `p: 1.0 -> 0.0` | Smoothly reverses `+6.0% -> -6.0%` | Smoothly reverses `-3.5% -> +3.5%` | Stationary |
| **Reduced Motion** | Any `p` | Locked at `translate3d(0, 0, 0)` (`width: 100%`) | Locked at `translate3d(-50%, 0, 0)` | Stationary |

- **Small Window / DPI Scaling (`1000×640`, `728×640`, `150%` DPI):** At viewport widths `< 900px`, reduce travel amplitude proportionally to `±4.0%` (frieze `width: 110%`) and `±2.0%` (ghost word) so the horizontal motion remains balanced without cropping the pharmacist figure out of frame.
- **Takeover / Transition:** Because the frieze is the terminal band on both Home and About Us, reaching `p = 1.0` lands naturally on the composed final frame with zero jump, while scrolling back upward immediately reverses the scene into the preceding section (`H3` Folder Shelf on Home; `A4` Support/Ownership metadata on About Us).

---

## 3. Signature Effect 2 — Darkness-First Lantern / Cursor Spotlight (`H1` Hero Only)

### 3.1 True Light-Reveal Architecture (Not a Decorative Cursor Glow)
To prevent the lantern from degenerating into a cheap glowing circle painted over visible text:
- **Layer 0 (`z-index: 0`):** Deep `#08090D` graphite hero stage.
- **Layer 1 (`z-index: 1`, Ambient Dark Artwork):** Full-bleed monochrome apothecary/ledger architectural linework SVG rendered in `#F0F0EC` strokes at `opacity: 0.03`—virtually invisible in darkness, establishing authentic physical presence without noise.
- **Layer 2 (`z-index: 2`, Masked Illuminated Artwork):** Identical monochrome apothecary/ledger SVG at crisp `opacity: 0.58` paired with a subtle neutral-white optical wash (`radial-gradient` core `rgba(246, 246, 242, 0.12)`). This entire layer is revealed strictly through a CSS `mask-image` / `-webkit-mask-image` positioned at smoothed pointer coordinates `(--lx, --ly)` with dynamic radius `--lr` and opacity `--la`:
  ```css
  mask-image: radial-gradient(
    circle var(--lr, 250px) at var(--lx, 50%) var(--ly, 50%),
    rgba(0, 0, 0, 1) 0%,
    rgba(0, 0, 0, 0.72) 32%,
    rgba(0, 0, 0, 0.22) 64%,
    rgba(0, 0, 0, 0) 100%
  );
  ```
- **Layer 3 (`z-index: 3`, Essential Foreground UI):** Shop header, monumental headline, subtitle, `Start Billing`, `+ Add Stock`, and the single-line readiness strip sit cleanly above the artwork layers at full contrast at all times. Hover never conceals essential text or controls.
- **Zero Autonomous Orbit:** Stage 4.8's idle figure-eight screensaver loop is **permanently removed**. When no pointer is inside `H1`, `--la` settles to `0` and the background remains authentically dark.

### 3.2 Lantern State Machine

| State | Trigger | Coordinates `(--lx, --ly)` | Radius `--lr` | Reveal Opacity `--la` | Loop State |
|---|---|---|---|---|---|
| **Resting (Dark)** | Initial load / pointer outside `H1` | Frozen at last position | `0.86 × R_base` | `0.0` | **Stopped** (`0%` CPU) |
| **Pointer Enter** | `pointerenter` on `H1` (fine pointer) | Snaps initial `x, y` to entry point (zero jump from stale corner), then tracks | Expands to `R_base` (`clamp(190px, 24vw, 300px)`) | Eases `0 -> 1` over `260ms` | Active `rAF` |
| **Active Follow** | `pointermove` inside `H1` | Frame-rate-independent lerp: `x += (tx - x) * (1 - exp(-7.2 * dt))` | `R_base` | `1.0` | Active `rAF`; **auto-stops** once settled `< 0.25px` |
| **Pointer Exit / Cancel / Blur** | `pointerleave`, `pointercancel`, `window.blur`, `visibilitychange` | Continues gentle coast on last velocity | Contracts `R_base -> 0.86 × R_base` | Eases `1 -> 0` over `420ms` | **Stops** when `--la < 0.004` |
| **Keyboard Focus Fallback** | `:focus-visible` on Hero CTAs (`Start Billing`, `+ Add Stock`) | Anchors gently at `(68%, 46%)` behind right negative space | `R_base` | Eases to `0.55` over `300ms`; returns to `0` on blur | One-shot transition |
| **Touch / Coarse Pointer** | `@media (hover: none), (pointer: coarse)` or `pointerType === 'touch'` | Updates on `pointerdown` / `pointermove` drag; fades gently `1200ms` after `pointerup` | `200px` | `0.85` during touch, settles to calm `0.14` ambient reveal | Stops after fade |
| **Reduced Motion** | `prefers-reduced-motion: reduce` | Static center-right `(72%, 48%)`, no tracking | Full stage (mask disabled) | Layer 1 static `opacity: 0.08`; Layer 2 hidden | **Never runs** |

---

## 4. Signature Effect 3 — Physical 4-Layer Folder Hover Interaction (`H3` Shelf)

### 4.1 Physical Stack Geometry (All 3 Home Folders)
Each folder is a semantic `<button type="button" class="orva-folder">` (natively keyboard-focusable, Enter/Space activatable) with `perspective: 1000px` and 5 physical DOM layers:

| Layer | Element & Surface | Resting State (Exact Zero-Drift Geometry) | Active State (`.is-open` / `:hover` / `:focus-visible`) | Entry Delay / Exit Delay |
|---|---|---|---|---|
| **L0 · Backplate & Tab** | `#141720`, `1px solid #2B2E38`, chamfered tab `01`/`02`/`03` | `transform: none` | Border softens to `#3F4454` | `0ms` / `0ms` |
| **L1 · Rear Sheet (Sheet 3)** | `#DCDCD6` chalk paper, `z-index: 1` | `translate3d(0, 6px, 0) rotate(-1.8deg) scale(0.93)` · shadow `0 2px 6px rgba(0,0,0,0.25)` | `translate3d(-8px, -64px, 0) rotate(-6.2deg) scale(0.97)` · shadow `0 16px 30px rgba(0,0,0,0.52)` | `85ms` in / `0ms` out |
| **L2 · Middle Sheet (Sheet 2)** | `#E8E8E2` chalk paper, `z-index: 2` | `translate3d(0, 4px, 0) rotate(0.6deg) scale(0.96)` · shadow `0 3px 8px rgba(0,0,0,0.30)` | `translate3d(2px, -48px, 0) rotate(1.4deg) scale(0.99)` · shadow `0 18px 34px rgba(0,0,0,0.56)` | `42ms` in / `30ms` out |
| **L3 · Front Sheet (Sheet 1)** | `#F0F0EC` crisp chalk paper, `z-index: 3` | `translate3d(0, 2px, 0) rotate(-0.4deg) scale(0.985)` · shadow `0 4px 12px rgba(0,0,0,0.35)` | `translate3d(9px, -30px, 0) rotate(4.2deg) scale(1.0)` · shadow `0 20px 38px rgba(0,0,0,0.62)` | `0ms` in / `60ms` out |
| **L4 · Hinged Front Flap** | `#181B24`, `transform-origin: bottom center`, `z-index: 4` | `rotateX(0deg)` · `border-top: 1px solid #353947` | `rotateX(-24deg)` · `border-top: 1px solid #B4A9FF` · `box-shadow: 0 -8px 22px rgba(139,124,255,0.14)` | `0ms` in / `40ms` out |

### 4.2 Reversibility, Rapid-Sweep & Input Rules
- **Single Authoritative State:** Transitions are driven strictly by CSS custom properties and a single `.is-open` / `:focus-visible` state—never competing imperative JS tweens—so rapid repeated mouse sweeps across Folders 01 → 02 → 03 cleanly interrupt in mid-flight and return to exact resting transforms without drift or stuck states.
- **Inverse Stagger Symmetry:** On entry, the front flap hinges open and sheets fan front-to-back (`0ms -> 42ms -> 85ms`). On exit, sheets retract back-to-front (`0ms -> 30ms -> 60ms`) while the flap closes (`40ms`)—matching physical paper settling back into a folder pocket.
- **Touch / Click Behaviour:** On fine pointers (mouse), hovering opens the stack and clicking immediately fires the validated `postMessage` navigation action. On coarse pointers (touch), first tap opens the stack (closing any sibling folder); second tap on the open folder navigates; tapping outside closes all folders.
- **Forced Reset Hooks:** `pointercancel`, `window.blur`, `document.visibilitychange (hidden)`, and navigation click immediately strip `.is-open` from all folders.
- **Reduced Motion:** `transform` animations on L1–L4 are disabled (`transition: none`); active/focus state uses an instant `#B4A9FF` top bevel highlight and subtle `rgba(139, 124, 255, 0.10)` flap tint while keeping all folder titles and action cues 100% legible.

---

## 5. Page Transitions, About Us Wave & Micro-Interactions

- **Home ↔ About Us Transition:** `MainWindow` swaps `HomeView` and `AboutUsView` inside `PageHost`. Each view's `#08090D` `LoadingVeil` drops immediately on WebView2 `NavigationCompleted` / first paint, followed by a restrained `200ms` `--ease-editorial` opacity settle (`0 -> 1`) on `<main>`. Navigation is never blocked or delayed.
- **About Us 5-Node Curved Bezier Timeline (`A3`):**
  - Resting hairline path `#2B2E38` (`stroke-width: 1.2`); active progress path `#8B7CFF` (`stroke-width: 1.8`, subtle `drop-shadow(0 0 6px rgba(139,124,255,0.45))`).
  - Vertical scroll through `A3` scrubs `stroke-dashoffset` smoothly across Nodes `01`–`05` (`0.04, 0.28, 0.52, 0.76, 1.00`).
  - Hovering, clicking, or keyboard-focusing (`tabindex="0"`, `aria-pressed`) any node or principle column temporarily overrides the active node; leaving `A3` (`pointerleave` + `focusout` outside container) releases override cleanly back to scroll progress (fixing Stage 4.8's permanent `userPinned = true` lock).
- **Navigation, CTA & Micro-Feedback:**
  - Primary Home CTAs (`Start Billing`, `+ Add Stock`): subtle magnetic pointer pull capped strictly at `±4px` (`fine` pointer only, disabled under reduced motion), snapping cleanly to `(0, 0)` on `pointerleave` / `blur`.
  - Folder arrow (`→`): translates `+3px` (`140ms --ease-editorial`) and shifts border/fill to `#8B7CFF` / `#08090D` when folder is active.
  - **Custom Cursor Ring Retired:** Stage 4.8's unbounded `cursor-ring` `rAF` loop is **removed**. Native Windows pointer precision plus the Hero lantern beam provides superior craft with zero continuous idle CPU cost.
- **Loading & Asset-Failure States:**
  - If Google Fonts CDN is unreachable or times out, local bundled Noto TTF fallbacks (`Noto Sans`, `Noto Serif`, `Noto Sans Mono`, `Noto Sans Bengali`, `Noto Serif Bengali`) render immediately via `font-display: swap` with zero invisible text flash.
  - If external animation CDN (if any is used in Prompt 4) fails to load, every section remains 100% visible and functional via native CSS/JS fallback—never stuck at `opacity: 0`.

---

## 6. Performance Engineering & Lifecycle Suspension

1. **Zero Idle Loops:** No `requestAnimationFrame` runs unless a target delta is actively converging (`lantern` moving/fading or `frieze` scrubbing). Once settled below threshold (`< 0.25px` or `< 0.004` alpha), `cancelAnimationFrame` stops the loop immediately.
2. **Off-Screen Suspension (`IntersectionObserver`):** Both `H1` (Lantern) and `H4`/`A4` (Frieze) register `IntersectionObserver` watchers (`threshold: 0`). When a section leaves the viewport, its animation listener/loop suspends immediately.
3. **Window-Inactive Suspension:** `document.addEventListener('visibilitychange', ...)` and `window.addEventListener('blur', ...)` immediately halt active `rAF` loops, extinguish the lantern cleanly, and reset `.is-open` folder states.
4. **Pointer Event Coalescing:** `pointermove` handlers ONLY record `{ tx, ty }` and schedule a single `rAF` if one is not already queued—never performing DOM writes or `getBoundingClientRect()` inside the raw `pointermove` event.
5. **Layout Thrashing Prevention:** Bounding rects (`heroRect`, `friezeRect`, `viewportHeight`) are cached on initial load and updated exclusively via `ResizeObserver` (debounced `120ms`). Animation frames mutate only GPU-composited properties (`transform`, `opacity`, and CSS mask custom properties).
6. **Clean Disposal:** On page unload, all `ResizeObserver`, `IntersectionObserver`, `window`, and `document` listeners are explicitly disconnected/removed alongside `HomeView_Unloaded` / `AboutUsView_Unloaded` WebView2 disposal.

---

## 7. Explicit Acceptance Criteria & Failure Definitions

### 7.1 Acceptance Criteria Matrix

| Signature Effect | Mandatory Pass Criteria | Immediate Failure Definition |
|---|---|---|
| **Scroll-Controlled Frieze (`H4` / `A4`)** | Vertical scroll smoothly pans `apothecary_frieze_bw.jpg` horizontally (`-6% -> +6%`) while `ORVA.` counter-glides (`+3.5% -> -3.5%`) and the dark header stays stationary; reverse scroll reverses smoothly; zero edge clipping at `1000×640`, `1360×760`, `1600×900`, `728×640`; static centered under reduced motion. | Illustration moves only on hover; vertical scroll triggers generic fade-in; white side gap appears at edges; scroll traps user or jumps on resize. |
| **Lantern Spotlight (`H1`)** | Dark canvas at rest; pointer controls feathered radial mask revealing hidden monochrome apothecary linework; bright center dissolves smoothly into `#08090D`; pointer exit/blur extinguishes smoothly to `0`; `rAF` halts when settled; keyboard focus illuminates softly; reduced motion shows calm static fallback. | Autonomous screensaver orbit resumes on idle; glowing dot sits on top without revealing artwork; light snaps off abruptly on exit; `rAF` runs continuously at idle; essential text is hidden in darkness. |
| **Layered Folder Stack (`H3`)** | 3 distinct paper sheets separate with staggered vertical lift, rotation, and depth shadows while front flap hinges `-24deg` with `#B4A9FF` bevel; inverse-stagger exit restores exact resting geometry; 20 rapid hover sweeps leave zero stuck layers; keyboard Tab/Enter/Space and touch tap work cleanly; reduced motion stays crisp. | Flat card border-only hover; fewer than 3 layered sheets; any sheet stays displaced after fast pointer exit or window blur; folder is unreachable via keyboard. |

### 7.2 Reference Fidelity & Practical Interpretation Notes
- **Video 75300 Crowd Frieze → ORVA Apothecary Frieze:** Video 75300 shows a monochrome crowd illustration panning horizontally over giant faint `"ASWIN."` lettering. ORVA faithfully adapts this exact layered composition using ORVA's bundled monochrome `apothecary_frieze_bw.jpg` over giant faint `"ORVA."` lettering.
- **Video 75300 / GSAP Vault Lantern → Masked Monochrome Linework:** Because the reference videos show a cursor-controlled light pool revealing dark-surface artwork/typography without supplying source artwork files, ORVA implements a crisp, locally embedded monochrome apothecary/ledger architectural SVG revealed strictly via a feathered CSS radial mask so it works 100% reliably offline with zero external image failure.
- **Video 75300 / Framer / Vengeance UI Folder Stack → Plain HTML/CSS/JS Layered Stack:** Consistent with Master Prompt 6 §7.1, the multi-sheet staggered 3D folder mechanics from Video 75300 and Vengeance UI `Folder Preview` are faithfully recreated using pure semantic HTML/CSS/JS inside WebView2 without introducing React or a build pipeline.
