using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Orva.Services
{
    public sealed class HomeShowcaseData
    {
        public string ShopName { get; set; }
        public string DateText { get; set; }
        public string Language { get; set; }
        public int TotalMedicines { get; set; }
        public int UsableMedicines { get; set; }
        public int ExpiringSoonCount { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public string LastBackupText { get; set; }
        public bool BackupFresh { get; set; }
        public string SubscriptionHeadline { get; set; }
        public bool BillingAllowed { get; set; }
        public string AppVersion { get; set; }
        public bool ReducedMotion { get; set; }
    }

    public sealed class AboutShowcaseData
    {
        public string Language { get; set; }
        public string SupportPhone { get; set; }
        public string SupportEmail { get; set; }
        public string AppVersion { get; set; }
        public string BuildNumber { get; set; }
        public bool ReducedMotion { get; set; }
    }

    /// <summary>
    /// ORVA Stage 5.6.0 Art-Directed Creative Showcase Builder for WebView2 (Home &amp; About Us).
    /// Faithfully implements the 5 owner-selected visual references with authentic ORVA wording
    /// and 100% English / Bengali parity:
    ///   1. Kaito Note — Obsidian compass-grid hero + luminous cobalt/electric-blue hemisphere + mixed serif-italic display (Home Act 1)
    ///   2. GSAP Vault X-Ray Lantern — Acid-yellow moving light pool revealing inverted black alternate copy beneath dark surface (Home Act 2)
    ///   3. Framer Physical Folder Stack — Colour-coded pastel tabbed folders with 3 fanned angled preview sheets (Home Act 3)
    ///   4. Longshot / Video 75300 Cinematic Scroll — Vertical-scroll-scrubbed horizontal monochrome apothecary frieze over giant ORVA. (Home &amp; About Footer)
    ///   5. Elementum — Architectural concrete-mist arch hero + full-bleed display lettering + acid-lime micro-anchors (About Us Act 1)
    /// </summary>
    public static class ShowcaseHtmlBuilder
    {
        private static string _cachedFriezeDataUri;

        private static bool TryReadEmbeddedResource(string logicalName, out byte[] bytes)
        {
            bytes = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(logicalName))
                {
                    if (stream == null) return false;
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        bytes = ms.ToArray();
                        return bytes.Length > 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetFriezeDataUri()
        {
            if (_cachedFriezeDataUri != null) return _cachedFriezeDataUri;

            byte[] bytes;
            if (TryReadEmbeddedResource("Orva.Assets.apothecary_frieze_bw.jpg", out bytes)
                || TryReadEmbeddedResource("Orva.Assets.apothecary_frieze.jpg", out bytes))
            {
                _cachedFriezeDataUri = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
                return _cachedFriezeDataUri;
            }

            _cachedFriezeDataUri = string.Empty;
            return _cachedFriezeDataUri;
        }

        private static string SerializeSafeJson(object data)
        {
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(data ?? new object());
            return json.Replace("</", "<\\/");
        }

        public static string BuildHomeHtml(HomeShowcaseData data)
        {
            if (data == null) data = new HomeShowcaseData();
            string payloadJson = SerializeSafeJson(data);
            string friezeSrc = GetFriezeDataUri();
            string langAttr = string.Equals(data.Language, "bn", StringComparison.OrdinalIgnoreCase) ? "bn" : "en";

            var sb = new StringBuilder(36864);
            sb.Append(@"<!DOCTYPE html>
<html lang=""");
            sb.Append(langAttr);
            sb.Append(@""">
<head>
<meta charset=""UTF-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>ORVA — Home</title>
<link rel=""preconnect"" href=""https://fonts.googleapis.com"" />
<link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin />
<link href=""https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Instrument+Serif:ital@0;1&family=Noto+Sans+Bengali:wght@400;600;700&family=Noto+Serif+Bengali:wght@400;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700&family=Space+Grotesk:wght@500;600;700&display=swap"" rel=""stylesheet"" />
<style>
  :root {
    --canvas-black: #050608;
    --canvas-deep: #0B0C10;
    --canvas-charcoal: #111216;
    --ink-chalk: #F4F4F0;
    --ink-secondary: #B4B6BE;
    --ink-muted: #7C808C;
    --hair: rgba(244, 244, 240, 0.13);
    --hair-strong: rgba(244, 244, 240, 0.28);

    /* Ref 1: Kaito Note Cobalt / Electric Blue */
    --kaito-cyan: #38E4FF;
    --kaito-cobalt: #1E5BFF;
    --kaito-indigo: #0C1A5E;

    /* Ref 2: GSAP Vault Acid-Yellow X-Ray Lantern */
    --lantern-acid: #E8FF00;

    /* Ref 3: Framer Physical Pastel Folders */
    --folder-stage: #EFEFEB;
    --folder-pink: #FF8DA1;
    --folder-blue: #7CCBFF;
    --folder-lavender: #C79BFF;

    --font-display: 'Space Grotesk', 'Noto Sans', 'Noto Sans Bengali', 'Nirmala UI', 'Segoe UI', sans-serif;
    --font-serif: 'Instrument Serif', 'Noto Serif', 'Noto Serif Bengali', 'Nirmala UI', Georgia, serif;
    --font-body: 'Plus Jakarta Sans', 'Noto Sans', 'Noto Sans Bengali', 'Nirmala UI', 'Segoe UI', sans-serif;
    --font-mono: 'IBM Plex Mono', 'Noto Sans Mono', 'Noto Sans Bengali', Consolas, monospace;

    --ease-out: cubic-bezier(0.22, 1, 0.36, 1);
    --ease-folder-open: cubic-bezier(0.20, 0.92, 0.24, 1.05);
    --ease-folder-close: cubic-bezier(0.32, 0, 0.20, 1);
  }

  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body {
    background: var(--canvas-black);
    color: var(--ink-chalk);
    font-family: var(--font-body);
    font-size: 16px;
    line-height: 1.65;
    -webkit-font-smoothing: antialiased;
    overflow-x: hidden;
  }
  html[lang=""bn""] body {
    font-family: 'Noto Sans Bengali', 'Nirmala UI', 'Plus Jakarta Sans', sans-serif;
    line-height: 1.76;
  }
  html[lang=""bn""] .kaito-title,
  html[lang=""bn""] .xray-big,
  html[lang=""bn""] .sec-h2 {
    font-family: 'Noto Serif Bengali', 'Noto Sans Bengali', 'Nirmala UI', sans-serif;
    letter-spacing: 0 !important;
    line-height: 1.22 !important;
  }

  @keyframes orvaEnter { from { opacity: 0; } to { opacity: 1; } }
  main { opacity: 1; animation: orvaEnter 220ms var(--ease-out) both; }

  .scroll-bar {
    position: fixed; top: 0; left: 0; height: 2px; width: 0%;
    background: linear-gradient(90deg, var(--kaito-cobalt), var(--kaito-cyan));
    z-index: 95; pointer-events: none;
  }

  .container {
    width: 100%;
    max-width: 1220px;
    margin: 0 auto;
    padding: 0 clamp(22px, 4.2vw, 52px);
  }
  .mono {
    font-family: var(--font-mono);
    font-size: 11.5px;
    letter-spacing: 0.09em;
    text-transform: uppercase;
    color: var(--ink-muted);
    white-space: nowrap;
  }
  html[lang=""bn""] .mono { letter-spacing: 0; text-transform: none; }

  /* =========================================================================
     ACT 1 — KAITO NOTE HERO (Compass Geometry + Luminous Blue Hemisphere)
     ========================================================================= */
  .kaito-hero {
    position: relative;
    min-height: calc(100vh - 58px);
    background: var(--canvas-black);
    border-bottom: 1px solid var(--hair);
    overflow: hidden;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    padding: clamp(24px, 3.8vh, 40px) 0 clamp(18px, 2.6vh, 26px);
  }
  .kaito-geo {
    position: absolute;
    inset: 0;
    pointer-events: none;
    z-index: 1;
  }
  .kaito-geo svg {
    width: 100%;
    height: 100%;
    display: block;
  }
  .kaito-orb-wrap {
    position: absolute;
    left: 50%;
    top: 48%;
    width: clamp(320px, 42vw, 540px);
    height: clamp(320px, 42vw, 540px);
    transform: translate(-50%, -50%);
    pointer-events: none;
    z-index: 2;
  }
  /* Crisp half-moon cobalt/cyan hemisphere clipped to right half of vertical axis */
  .kaito-hemisphere {
    position: absolute;
    inset: 0;
    border-radius: 50%;
    clip-path: inset(0 0 0 50%);
    background:
      radial-gradient(circle at 64% 38%, rgba(160, 245, 255, 0.95) 0%, rgba(56, 228, 255, 0.85) 22%, rgba(30, 91, 255, 0.88) 54%, rgba(12, 26, 94, 0.72) 82%, rgba(5, 6, 8, 0) 100%);
    filter: saturate(1.18);
    box-shadow: 0 0 90px rgba(30, 91, 255, 0.32);
    will-change: transform;
    transition: transform 280ms var(--ease-out);
  }
  .kaito-hemisphere::after {
    content: '';
    position: absolute;
    inset: 0;
    border-radius: 50%;
    background: linear-gradient(155deg, rgba(255,255,255,0.45) 0%, rgba(30,91,255,0.0) 48%, rgba(56,228,255,0.28) 100%);
    mix-blend-mode: screen;
  }
  .kaito-top-row {
    position: relative;
    z-index: 4;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
  }
  .kaito-mid-grid {
    position: relative;
    z-index: 4;
    flex: 1;
    display: grid;
    grid-template-columns: 1.25fr 0.75fr;
    align-items: end;
    gap: 32px;
    padding: 36px 0 32px;
  }
  .kaito-title {
    font-family: var(--font-body);
    font-weight: 400;
    font-size: clamp(34px, 4.7vw, 66px);
    line-height: 1.07;
    letter-spacing: -0.025em;
    color: var(--ink-chalk);
    max-width: 17ch;
  }
  .kaito-title em {
    font-family: var(--font-serif);
    font-style: italic;
    font-weight: 400;
    font-size: 1.08em;
    color: #FFFFFF;
  }
  html[lang=""bn""] .kaito-title em { font-style: normal; }

  .kaito-right-note {
    align-self: center;
    justify-self: end;
    max-width: 32ch;
    border-left: 1px solid var(--hair-strong);
    padding-left: 18px;
  }
  .kaito-right-note p {
    font-size: 13.5px;
    line-height: 1.62;
    color: var(--ink-secondary);
  }
  .cta-row {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
    margin-top: 28px;
  }
  .btn-cobalt, .btn-ghost {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-height: 46px;
    padding: 0 26px;
    border-radius: 6px;
    font-family: var(--font-body);
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: transform 140ms var(--ease-out), background 160ms ease, box-shadow 180ms ease, border-color 160ms ease;
  }
  .btn-cobalt {
    background: var(--kaito-cobalt);
    color: #FFFFFF;
    border: 1px solid var(--kaito-cyan);
    box-shadow: 0 0 28px rgba(30, 91, 255, 0.35);
  }
  .btn-cobalt:hover {
    background: #2C6BFF;
    box-shadow: 0 0 38px rgba(56, 228, 255, 0.45);
    transform: translateY(-1px);
  }
  .btn-ghost {
    background: rgba(17, 18, 22, 0.72);
    color: var(--ink-chalk);
    border: 1px solid var(--hair-strong);
  }
  .btn-ghost:hover {
    background: rgba(244, 244, 240, 0.1);
    border-color: var(--ink-chalk);
  }
  .btn-cobalt:focus-visible, .btn-ghost:focus-visible,
  .pastel-folder:focus-visible, .footer-link:focus-visible {
    outline: 2px solid var(--kaito-cyan);
    outline-offset: 3px;
  }

  /* Approved Single-Line Readiness Strip */
  .readiness-strip {
    position: relative;
    z-index: 4;
    border-top: 1px solid var(--hair);
    padding-top: 14px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 14px;
    flex-wrap: wrap;
  }
  .readiness-text {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--ink-secondary);
  }
  .readiness-text .hl { color: var(--kaito-cyan); }

  /* =========================================================================
     ACT 2 — GSAP VAULT X-RAY LANTERN INTERACTION (Acid-Yellow Moving Pool)
     ========================================================================= */
  .xray-sec {
    position: relative;
    background: var(--canvas-charcoal);
    border-bottom: 1px solid var(--hair);
    padding: clamp(72px, 10vw, 128px) 0;
    overflow: hidden;
    --lx: 55%;
    --ly: 52%;
    --rx: 250px;
    --ry: 135px;
    --la: 0;
    cursor: crosshair;
  }
  .xray-layout {
    display: grid;
    grid-template-columns: 0.34fr 1fr;
    gap: 36px;
    align-items: center;
  }
  .xray-rail {
    border-right: 1px solid var(--hair);
    padding-right: 28px;
  }
  .xray-rail p {
    margin-top: 14px;
    font-size: 14px;
    color: var(--ink-secondary);
    line-height: 1.65;
  }
  .xray-hint {
    margin-top: 18px;
    display: inline-block;
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--lantern-acid);
    letter-spacing: 0.08em;
  }
  .xray-stage {
    position: relative;
    padding: 36px 28px;
    border-top: 1px solid var(--hair);
    border-bottom: 1px solid var(--hair);
    user-select: none;
  }
  .xray-kicker {
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--ink-muted);
    margin-bottom: 14px;
    letter-spacing: 0.08em;
  }
  .xray-stack {
    position: relative;
  }
  .xray-big {
    font-family: var(--font-display);
    font-weight: 600;
    font-size: clamp(40px, 5.8vw, 76px);
    line-height: 1.02;
    letter-spacing: -0.03em;
  }
  .xray-surface {
    color: var(--ink-chalk);
    position: relative;
    z-index: 1;
  }
  /* Hidden X-Ray alternate statement revealed strictly inside the elliptical acid-yellow lantern beam */
  .xray-reveal {
    position: absolute;
    inset: -24px -20px;
    padding: 24px 20px;
    z-index: 2;
    pointer-events: none;
    color: #08090D;
    background: radial-gradient(
      ellipse var(--rx) var(--ry) at var(--lx) var(--ly),
      var(--lantern-acid) 0%,
      rgba(232, 255, 0, 0.94) 56%,
      rgba(232, 255, 0, 0.25) 84%,
      rgba(232, 255, 0, 0) 100%
    );
    -webkit-clip-path: ellipse(calc(var(--rx) * var(--la)) calc(var(--ry) * var(--la)) at var(--lx) var(--ly));
    clip-path: ellipse(calc(var(--rx) * var(--la)) calc(var(--ry) * var(--la)) at var(--lx) var(--ly));
    opacity: var(--la);
    transition: opacity 280ms var(--ease-out);
    will-change: clip-path, opacity;
  }
  .xray-sec.xray-on .xray-reveal {
    transition: opacity 160ms var(--ease-out);
  }
  .xray-sub-bar {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 20px;
    margin-top: 28px;
    padding-top: 18px;
    border-top: 1px solid var(--hair);
  }
  .xray-sub-bar strong {
    display: block;
    font-size: 13.5px;
    color: var(--ink-chalk);
    margin-bottom: 3px;
  }
  .xray-sub-bar span {
    font-size: 12.5px;
    color: var(--ink-muted);
  }

  /* =========================================================================
     ACT 3 — FRAMER PHYSICAL PASTEL FOLDER STACK (3 Colour-Coded Tabbed Folders)
     ========================================================================= */
  .folders-sec {
    background: var(--canvas-deep);
    padding: clamp(68px, 8.5vw, 108px) 0;
    border-bottom: 1px solid var(--hair);
  }
  .folders-head {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    flex-wrap: wrap;
    gap: 14px;
    margin-bottom: 36px;
  }
  .sec-h2 {
    font-family: var(--font-display);
    font-weight: 600;
    font-size: clamp(30px, 3.8vw, 48px);
    line-height: 1.06;
    letter-spacing: -0.02em;
    color: var(--ink-chalk);
  }
  /* Gallery-white contrast stage matching Framer Folder Stack reference */
  .folder-stage {
    background: var(--folder-stage);
    border-radius: 16px;
    padding: clamp(64px, 7vw, 88px) clamp(24px, 3.5vw, 44px) clamp(36px, 4.5vw, 52px);
    color: #0B0C10;
  }
  .folder-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 26px;
  }
  .pastel-folder {
    position: relative;
    display: block;
    width: 100%;
    text-align: left;
    background: transparent;
    border: none;
    padding: 26px 0 0 0;
    cursor: pointer;
    perspective: 1000px;
    font-family: inherit;
    user-select: none;
  }
  .pf-pink { --f-col: var(--folder-pink); --f-dark: #E86F85; }
  .pf-blue { --f-col: var(--folder-blue); --f-dark: #58B6F5; }
  .pf-lav  { --f-col: var(--folder-lavender); --f-dark: #AF7BF5; }

  .pf-back {
    position: relative;
    height: 196px;
    background: var(--f-dark);
    border-radius: 0 12px 12px 12px;
    box-shadow: 0 12px 26px rgba(11, 12, 16, 0.14);
  }
  .pf-tab {
    position: absolute;
    top: -22px;
    left: 0;
    min-width: 106px;
    height: 23px;
    padding: 0 14px;
    display: flex;
    align-items: center;
    background: var(--f-dark);
    border-radius: 8px 10px 0 0;
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    color: #0B0C10;
  }
  /* 3 Fanned Editorial Preview Cards Inside Each Folder */
  .pf-card {
    position: absolute;
    width: 78%;
    left: 11%;
    top: 10px;
    height: 126px;
    border-radius: 8px;
    padding: 12px 14px;
    color: var(--ink-chalk);
    background: linear-gradient(145deg, #1A1D28 0%, #0D0F16 100%);
    border: 1px solid rgba(255, 255, 255, 0.14);
    box-shadow: 0 8px 18px rgba(0, 0, 0, 0.25);
    will-change: transform;
    transition: transform 320ms var(--ease-folder-close), box-shadow 300ms ease;
  }
  .pf-c3 {
    z-index: 1;
    background: linear-gradient(145deg, #222638 0%, #10121B 100%);
    transform: translate3d(0, 6px, 0) rotate(-2deg) scale(0.92);
    transition-delay: 0ms;
  }
  .pf-c2 {
    z-index: 2;
    background: linear-gradient(145deg, #1B2336 0%, #0E1320 100%);
    transform: translate3d(0, 4px, 0) rotate(1deg) scale(0.95);
    transition-delay: 30ms;
  }
  .pf-c1 {
    z-index: 3;
    background: linear-gradient(145deg, #14161F 0%, #08090D 100%);
    transform: translate3d(0, 2px, 0) rotate(-0.5deg) scale(0.98);
    transition-delay: 60ms;
  }
  .pf-card-k {
    font-family: var(--font-mono);
    font-size: 10px;
    color: var(--kaito-cyan);
    letter-spacing: 0.06em;
  }
  .pf-card-t {
    font-size: 13px;
    font-weight: 600;
    margin-top: 4px;
    line-height: 1.3;
    color: #FFFFFF;
  }
  .pf-card-bar {
    margin-top: 10px;
    height: 2px;
    width: 44%;
    background: var(--f-col);
    opacity: 0.85;
  }

  .pf-front {
    position: absolute;
    left: 0; right: 0; bottom: 0;
    height: 142px;
    background: var(--f-col);
    border-radius: 10px 10px 12px 12px;
    padding: 18px 20px 16px;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    z-index: 4;
    transform-origin: bottom center;
    transform: rotateX(0deg);
    box-shadow: 0 -2px 10px rgba(0, 0, 0, 0.08);
    transition: transform 320ms var(--ease-folder-close), box-shadow 300ms ease;
    transition-delay: 40ms;
    will-change: transform;
  }
  .pf-title {
    font-family: var(--font-display);
    font-size: 25px;
    font-weight: 700;
    letter-spacing: -0.025em;
    color: #0B0C10;
    line-height: 1.1;
  }
  .pf-sub {
    font-size: 13px;
    color: rgba(11, 12, 16, 0.72);
    margin-top: 4px;
  }
  .pf-foot {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding-top: 10px;
    border-top: 1px solid rgba(11, 12, 16, 0.16);
  }
  .pf-cta {
    font-family: var(--font-mono);
    font-size: 11.5px;
    font-weight: 500;
    color: #0B0C10;
  }
  .pf-arrow {
    width: 28px; height: 28px;
    border-radius: 50%;
    background: #0B0C10;
    color: var(--ink-chalk);
    display: inline-flex; align-items: center; justify-content: center;
    font-size: 13px;
    transition: transform 160ms var(--ease-out);
  }

  /* Active / Hover / Focus Fan-Out */
  .pastel-folder.is-open .pf-card,
  .pastel-folder:focus-visible .pf-card {
    transition-duration: 380ms;
    transition-timing-function: var(--ease-folder-open);
  }
  .pastel-folder.is-open .pf-c1,
  .pastel-folder:focus-visible .pf-c1 {
    transform: translate3d(18px, -36px, 0) rotate(7deg) scale(1.0);
    box-shadow: 0 18px 34px rgba(0, 0, 0, 0.34);
    transition-delay: 0ms;
  }
  .pastel-folder.is-open .pf-c2,
  .pastel-folder:focus-visible .pf-c2 {
    transform: translate3d(0px, -56px, 0) rotate(-1deg) scale(0.99);
    box-shadow: 0 18px 34px rgba(0, 0, 0, 0.32);
    transition-delay: 42ms;
  }
  .pastel-folder.is-open .pf-c3,
  .pastel-folder:focus-visible .pf-c3 {
    transform: translate3d(-18px, -72px, 0) rotate(-9deg) scale(0.97);
    box-shadow: 0 16px 30px rgba(0, 0, 0, 0.28);
    transition-delay: 85ms;
  }
  .pastel-folder.is-open .pf-front,
  .pastel-folder:focus-visible .pf-front {
    transform: rotateX(-24deg);
    box-shadow: 0 -10px 26px rgba(0, 0, 0, 0.16);
    transition-duration: 380ms;
    transition-timing-function: var(--ease-folder-open);
    transition-delay: 0ms;
  }
  .pastel-folder.is-open .pf-arrow,
  .pastel-folder:focus-visible .pf-arrow {
    transform: translateX(3px);
  }

  /* =========================================================================
     ACT 4 — LONGSHOT / VIDEO 75300 CINEMATIC SCROLLING MONOCHROME FRIEZE
     ========================================================================= */
  .longshot-footer {
    background: var(--canvas-black);
    padding: 46px 0 34px;
    border-bottom: 1px solid var(--hair);
  }
  .footer-bar {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    flex-wrap: wrap;
    gap: 18px;
  }
  .footer-say {
    font-family: var(--font-display);
    font-size: clamp(24px, 3vw, 36px);
    font-weight: 700;
    letter-spacing: -0.02em;
    color: var(--ink-chalk);
  }
  .footer-nav {
    display: flex;
    align-items: center;
    gap: 22px;
    flex-wrap: wrap;
  }
  .footer-link {
    background: none;
    border: none;
    padding: 4px 0;
    font-family: var(--font-body);
    font-size: 14px;
    font-weight: 500;
    color: var(--ink-secondary);
    border-bottom: 1px solid transparent;
    cursor: pointer;
    transition: color 140ms ease, border-color 140ms ease;
  }
  .footer-link:hover {
    color: var(--ink-chalk);
    border-bottom-color: var(--kaito-cyan);
  }

  .frieze-band {
    position: relative;
    background: #F4F4F0;
    color: #050608;
    overflow: hidden;
    padding-top: 16px;
  }
  .frieze-cue {
    position: absolute;
    top: 12px;
    left: 28px;
    z-index: 4;
    font-family: var(--font-mono);
    font-size: 11px;
    color: rgba(5, 6, 8, 0.52);
    letter-spacing: 0.08em;
    pointer-events: none;
  }
  .ghost-orva {
    position: absolute;
    top: 0;
    left: 50%;
    transform: translate3d(-50%, 0, 0);
    font-family: var(--font-display);
    font-weight: 700;
    font-size: clamp(104px, 20vw, 248px);
    line-height: 0.88;
    letter-spacing: 0.05em;
    color: rgba(5, 6, 8, 0.08);
    pointer-events: none;
    user-select: none;
    white-space: nowrap;
    will-change: transform;
  }
  .frieze-track {
    position: relative;
    z-index: 2;
    width: 118%;
    margin-left: -9%;
    will-change: transform;
  }
  .frieze-img {
    display: block;
    width: min(1260px, 100%);
    height: auto;
    margin: 0 auto;
    mix-blend-mode: multiply;
  }

  @media (max-width: 1020px) {
    .kaito-mid-grid { grid-template-columns: 1fr; }
    .kaito-right-note { justify-self: start; max-width: 48ch; }
    .xray-layout { grid-template-columns: 1fr; }
    .xray-rail { border-right: none; border-bottom: 1px solid var(--hair); padding-right: 0; padding-bottom: 20px; }
    .folder-grid { gap: 18px; }
  }
  @media (max-width: 840px) {
    .folder-grid { grid-template-columns: 1fr; gap: 56px; }
    .xray-sub-bar { grid-template-columns: 1fr; }
    .frieze-track { width: 110%; margin-left: -5%; }
  }

  @media (prefers-reduced-motion: reduce) {
    main, .pf-card, .pf-front, .pf-arrow, .xray-reveal, .kaito-hemisphere {
      transition: none !important; animation: none !important;
    }
    .xray-reveal { display: none !important; }
    .pastel-folder.is-open .pf-c1,
    .pastel-folder.is-open .pf-c2,
    .pastel-folder.is-open .pf-c3,
    .pastel-folder.is-open .pf-front { transform: none !important; }
    .frieze-track { width: 100% !important; margin-left: 0 !important; transform: none !important; }
    .ghost-orva { transform: translate3d(-50%, 0, 0) !important; }
  }
  body.reduced-motion main,
  body.reduced-motion .pf-card,
  body.reduced-motion .pf-front,
  body.reduced-motion .pf-arrow,
  body.reduced-motion .xray-reveal,
  body.reduced-motion .kaito-hemisphere {
    transition: none !important; animation: none !important;
  }
  body.reduced-motion .xray-reveal { display: none !important; }
  body.reduced-motion .pastel-folder.is-open .pf-c1,
  body.reduced-motion .pastel-folder.is-open .pf-c2,
  body.reduced-motion .pastel-folder.is-open .pf-c3,
  body.reduced-motion .pastel-folder.is-open .pf-front { transform: none !important; }
  body.reduced-motion .frieze-track { width: 100% !important; margin-left: 0 !important; transform: none !important; }
  body.reduced-motion .ghost-orva { transform: translate3d(-50%, 0, 0) !important; }
</style>
</head>
<body>
  <div class=""scroll-bar"" id=""scrollBar""></div>
  <main id=""appMain"">

    <!-- ACT 1: KAITO NOTE CREATIVE SHOWCASE HERO -->
    <section class=""kaito-hero"" id=""kaitoHero"">
      <div class=""kaito-geo"" aria-hidden=""true"">
        <svg viewBox=""0 0 1200 680"" preserveAspectRatio=""xMidYMid slice"">
          <g fill=""none"" stroke=""rgba(244,244,240,0.12)"" stroke-width=""0.85"">
            <line x1=""600"" y1=""0"" x2=""600"" y2=""680"" />
            <line x1=""0"" y1=""330"" x2=""1200"" y2=""330"" />
            <line x1=""300"" y1=""0"" x2=""300"" y2=""680"" stroke=""rgba(244,244,240,0.06)"" />
            <line x1=""900"" y1=""0"" x2=""900"" y2=""680"" stroke=""rgba(244,244,240,0.06)"" />
            <circle cx=""600"" cy=""330"" r=""240"" />
            <circle cx=""600"" cy=""330"" r=""148"" stroke=""rgba(244,244,240,0.08)"" />
            <circle cx=""360"" cy=""330"" r=""240"" stroke=""rgba(244,244,240,0.09)"" />
            <circle cx=""840"" cy=""330"" r=""240"" stroke=""rgba(244,244,240,0.09)"" />
          </g>
        </svg>
      </div>

      <div class=""kaito-orb-wrap"" aria-hidden=""true"">
        <div class=""kaito-hemisphere"" id=""kaitoOrb""></div>
      </div>

      <div class=""container kaito-top-row"">
        <span class=""mono"" id=""heroShop"">ORVA PHARMACY</span>
        <span class=""mono"" id=""heroDate"">TODAY</span>
        <span class=""mono"" id=""heroVer"">RINDLE WORKS · ORVA 4.0.0</span>
      </div>

      <div class=""container kaito-mid-grid"">
        <div>
          <h1 class=""kaito-title"" id=""heroTitle"">
            Built for the Counter,<br />
            Refined <em>Digital</em> Precision.<br />
            <em>For</em> Pharmacy <em>and</em> Trust.
          </h1>
          <div class=""cta-row"">
            <button class=""btn-cobalt"" id=""btnBilling"" type=""button"">Start Billing →</button>
            <button class=""btn-ghost"" id=""btnAddStock"" type=""button"">+ Add Stock</button>
          </div>
        </div>
        <div class=""kaito-right-note"">
          <div class=""mono"" style=""color:var(--kaito-cyan);margin-bottom:8px;"" id=""kaitoTag"">LOCAL COUNTER EDITION</div>
          <p id=""heroSub"">Fast counter billing, careful batch and expiry checks, clean GST receipts, and local records that stay strictly under your control.</p>
        </div>
      </div>

      <div class=""container readiness-strip"">
        <div class=""readiness-text"" id=""readinessText"">Ready at the counter.</div>
        <span class=""mono"" id=""heroShortcut"">CTRL+B · INSTANT BILLING</span>
      </div>
    </section>

    <!-- ACT 2: GSAP VAULT ACID-YELLOW X-RAY LANTERN INTERACTION -->
    <section class=""xray-sec"" id=""xraySec"">
      <div class=""container"">
        <div class=""xray-layout"">
          <div class=""xray-rail"">
            <span class=""mono"" id=""xrayRailKicker"">THE COUNTER PHILOSOPHY</span>
            <p id=""xrayRailCopy"">ORVA is built for independent retail pharmacies that need dependable desktop software without clutter or cloud lock-in.</p>
            <span class=""xray-hint"" id=""xrayHint"">MOVE CURSOR ACROSS TEXT →</span>
          </div>

          <div class=""xray-stage"" id=""xrayStage"">
            <div class=""xray-kicker"" id=""xrayKicker"">Everyday pharmacy reality</div>
            <div class=""xray-stack"">
              <div class=""xray-big xray-surface"" id=""xraySurface"">
                Clear bills.<br />Careful stock.<br />Local control.
              </div>
              <div class=""xray-big xray-reveal"" id=""xrayReveal"" aria-hidden=""true"">
                Zero cloud lag.<br />Zero expired sales.<br />Built for real shops.
              </div>
            </div>

            <div class=""xray-sub-bar"">
              <div><strong id=""pil1T"">Offline-First Counter</strong><span id=""pil1D"">Billing and stock keep moving with zero internet.</span></div>
              <div><strong id=""pil2T"">Clean GST Invoices</strong><span id=""pil2D"">Integer-paise accuracy and ready customer prints.</span></div>
              <div><strong id=""pil3T"">Portable Shop Backups</strong><span id=""pil3D"">Local backup and restore owned by your shop.</span></div>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ACT 3: FRAMER PHYSICAL PASTEL FOLDER STACK -->
    <section class=""folders-sec"">
      <div class=""container"">
        <div class=""folders-head"">
          <div>
            <div class=""mono"" id=""folderKicker"">EVERYDAY WORKSPACES</div>
            <h2 class=""sec-h2"" style=""margin-top:8px;"" id=""folderHeading"">Open a folder to begin.</h2>
          </div>
          <div class=""mono"" id=""folderHint"">HOVER OR FOCUS TO FAN SHEETS</div>
        </div>

        <div class=""folder-stage"">
          <div class=""folder-grid"" id=""folderGrid"">
            <button type=""button"" class=""pastel-folder pf-pink"" data-action=""start_billing"" id=""folder1"">
              <div class=""pf-back"">
                <div class=""pf-tab"" id=""f1Tab"">01 · COUNTER</div>
                <div class=""pf-card pf-c3"">
                  <div class=""pf-card-k"" id=""f1S3K"">CHECKOUT</div>
                  <div class=""pf-card-t"" id=""f1S3H"">Fast Counter Checkout</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c2"">
                  <div class=""pf-card-k"" id=""f1S2K"">RECEIPTS</div>
                  <div class=""pf-card-t"" id=""f1S2H"">Clean GST &amp; Customer Prints</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c1"">
                  <div class=""pf-card-k"" id=""f1S1K"">INPUT</div>
                  <div class=""pf-card-t"" id=""f1S1H"">Keyboard &amp; Barcode Ready</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-front"">
                  <div>
                    <div class=""pf-title"" id=""f1Title"">billing</div>
                    <div class=""pf-sub"" id=""f1Sub"">Create bills and print clean receipts.</div>
                  </div>
                  <div class=""pf-foot"">
                    <span class=""pf-cta"" id=""f1Cta"">Start Billing</span>
                    <span class=""pf-arrow"" aria-hidden=""true"">→</span>
                  </div>
                </div>
              </div>
            </button>

            <button type=""button"" class=""pastel-folder pf-blue"" data-action=""nav_inventory"" id=""folder2"">
              <div class=""pf-back"">
                <div class=""pf-tab"" id=""f2Tab"">02 · STOCK</div>
                <div class=""pf-card pf-c3"">
                  <div class=""pf-card-k"" id=""f2S3K"">PRIORITY</div>
                  <div class=""pf-card-t"" id=""f2S3H"">Earliest-Expiry Selection</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c2"">
                  <div class=""pf-card-k"" id=""f2S2K"">WATCHLIST</div>
                  <div class=""pf-card-t"" id=""f2S2H"">0 Expiring ≤ 30 Days</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c1"">
                  <div class=""pf-card-k"" id=""f2S1K"">SAFEGUARD</div>
                  <div class=""pf-card-t"" id=""f2S1H"">Batch &amp; Expiry Control</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-front"">
                  <div>
                    <div class=""pf-title"" id=""f2Title"">inventory</div>
                    <div class=""pf-sub"" id=""f2Sub"">Monitor batches and expiry alerts.</div>
                  </div>
                  <div class=""pf-foot"">
                    <span class=""pf-cta"" id=""f2Cta"">Open Inventory</span>
                    <span class=""pf-arrow"" aria-hidden=""true"">→</span>
                  </div>
                </div>
              </div>
            </button>

            <button type=""button"" class=""pastel-folder pf-lav"" data-action=""nav_about"" id=""folder3"">
              <div class=""pf-back"">
                <div class=""pf-tab"" id=""f3Tab"">03 · STUDIO</div>
                <div class=""pf-card pf-c3"">
                  <div class=""pf-card-k"" id=""f3S3K"">PRINCIPLES</div>
                  <div class=""pf-card-t"" id=""f3S3H"">Five Guiding Principles</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c2"">
                  <div class=""pf-card-k"" id=""f3S2K"">RELEASE</div>
                  <div class=""pf-card-t"" id=""f3S2H"">ORVA v4.0.0</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-card pf-c1"">
                  <div class=""pf-card-k"" id=""f3S1K"">PROVISIONAL</div>
                  <div class=""pf-card-t"" id=""f3S1H"">Rindle Works Studio</div>
                  <div class=""pf-card-bar""></div>
                </div>
                <div class=""pf-front"">
                  <div>
                    <div class=""pf-title"" id=""f3Title"">about orva</div>
                    <div class=""pf-sub"" id=""f3Sub"">Story, principles, and support.</div>
                  </div>
                  <div class=""pf-foot"">
                    <span class=""pf-cta"" id=""f3Cta"">Read About Us</span>
                    <span class=""pf-arrow"" aria-hidden=""true"">→</span>
                  </div>
                </div>
              </div>
            </button>
          </div>
        </div>
      </div>
    </section>

    <!-- ACT 4: LONGSHOT / VIDEO 75300 CINEMATIC SCROLL-SCRUBBED MONOCHROME FRIEZE -->
    <footer class=""longshot-footer"">
      <div class=""container footer-bar"">
        <div class=""footer-say"" id=""footerSay"">See you at the counter.</div>
        <nav class=""footer-nav"" aria-label=""Footer navigation"">
          <button type=""button"" class=""footer-link"" id=""lnkBilling"">Start Billing</button>
          <button type=""button"" class=""footer-link"" id=""lnkAbout"">About Us</button>
          <button type=""button"" class=""footer-link"" id=""lnkTop"">Back to top ↑</button>
          <span class=""mono"" id=""footerVer"">RINDLE WORKS · ORVA 4.0.0</span>
        </nav>
      </div>
    </footer>

    <div class=""frieze-band"" id=""friezeBand"">
      <div class=""frieze-cue"" id=""friezeCue"">SCROLL DOWN TO PAN →</div>
      <div class=""ghost-orva"" id=""ghostOrva"" aria-hidden=""true"">ORVA.</div>
      <div class=""frieze-track"" id=""friezeTrack"">");
            if (!string.IsNullOrEmpty(friezeSrc))
            {
                sb.Append(@"<img class=""frieze-img"" src=""");
                sb.Append(friezeSrc);
                sb.Append(@""" alt=""ORVA Pharmacy Counter Illustration"" />");
            }
            sb.Append(@"</div>
    </div>
  </main>

  <script id=""orva-data"" type=""application/json"">");
            sb.Append(payloadJson);
            sb.Append(@"</script>

  <script>
  (function () {
    'use strict';

    var DATA = {};
    try { DATA = JSON.parse(document.getElementById('orva-data').textContent || '{}'); } catch (e) { DATA = {}; }
    var isBn = (DATA.Language || 'en').toLowerCase() === 'bn';
    var sysReduced = !!(DATA.ReducedMotion || (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches));
    if (sysReduced) document.body.classList.add('reduced-motion');

    function send(action) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ action: action }));
      }
    }

    var shop = DATA.ShopName || 'ORVA Pharmacy';
    var ver = DATA.AppVersion || '4.0.0';
    var usable = Math.max(0, Number(DATA.UsableMedicines || 0));
    var expCount = Math.max(0, Number(DATA.ExpiringSoonCount || 0));
    var bkpText = DATA.BackupFresh
      ? (isBn ? 'সুরক্ষিত' : 'Saved')
      : ((!DATA.LastBackupText || DATA.LastBackupText === 'Not yet')
          ? (isBn ? 'এখনো হয়নি' : 'Not yet backed up')
          : (isBn ? 'বকেয়া' : 'Due today'));

    document.getElementById('heroShop').textContent = shop.toUpperCase();
    document.getElementById('heroDate').textContent = DATA.DateText || '';
    document.getElementById('heroVer').textContent = 'RINDLE WORKS · ORVA ' + ver;
    document.getElementById('footerVer').textContent = 'RINDLE WORKS · ORVA ' + ver;
    document.getElementById('f3S2H').textContent = 'ORVA v' + ver;
    document.getElementById('f2S2H').textContent = isBn ? (expCount + 'টির মেয়াদ ≤ ৩০ দিন') : (expCount + ' Expiring ≤ 30 Days');

    var rEl = document.getElementById('readinessText');
    rEl.innerHTML = '';
    rEl.appendChild(document.createTextNode(
      isBn ? (shop + '  ·  ' + usable + 'টি ওষুধ প্রস্তুত  ·  ৩০ দিনের মধ্যে মেয়াদ শেষ: ')
           : (shop + '  ·  ' + usable + ' medicines ready  ·  ')
    ));
    var hl = document.createElement('span');
    if (expCount > 0) hl.className = 'hl';
    hl.textContent = isBn ? String(expCount) : (expCount + ' expiring ≤ 30 days');
    rEl.appendChild(hl);
    rEl.appendChild(document.createTextNode(isBn ? ('  ·  ব্যাকআপ: ' + bkpText) : ('  ·  Backup: ' + bkpText)));

    if (isBn) {
      document.getElementById('heroTitle').innerHTML = 'কাউন্টারের জন্য তৈরি,<br />নিখুঁত <em>ডিজিটাল</em> শৃঙ্খলা।<br /><em>ফার্মেসি</em> ও <em>আস্থার</em> সাথে।';
      document.getElementById('kaitoTag').textContent = 'লোকাল কাউন্টার সংস্করণ';
      document.getElementById('heroSub').textContent = 'দ্রুত বিলিং, ব্যাচ ও মেয়াদের সতর্ক হিসাব, পরিষ্কার GST রসিদ এবং আপনার কম্পিউটারেই সুরক্ষিত রেকর্ড।';
      document.getElementById('btnBilling').textContent = 'বিলিং শুরু করুন →';
      document.getElementById('btnAddStock').textContent = '+ স্টক যোগ করুন';
      document.getElementById('heroShortcut').textContent = 'CTRL+B · দ্রুত বিলিং';

      document.getElementById('xrayRailKicker').textContent = 'কাউন্টারের দর্শন';
      document.getElementById('xrayRailCopy').textContent = 'স্বাধীন ফার্মেসির প্রতিদিনের কাজের জন্য জটিলতাহীন ও নির্ভরযোগ্য ডেস্কটপ সফটওয়্যার হিসেবে ORVA তৈরি।';
      document.getElementById('xrayHint').textContent = 'লেখার উপর কার্সর ঘোরান →';
      document.getElementById('xrayKicker').textContent = 'প্রতিদিনের বাস্তবতা';
      document.getElementById('xraySurface').innerHTML = 'পরিষ্কার বিল।<br />সতর্ক স্টক।<br />আপনার নিয়ন্ত্রণ।';
      document.getElementById('xrayReveal').innerHTML = 'নেট ছাড়াই চলে।<br />মেয়াদোত্তীর্ণ বিক্রি নয়।<br />আসল দোকানের জন্য।';
      document.getElementById('pil1T').textContent = 'অফলাইন কাউন্টার';
      document.getElementById('pil1D').textContent = 'ইন্টারনেট ছাড়াই বিলিং ও স্টক নির্বিঘ্নে চলে।';
      document.getElementById('pil2T').textContent = 'পরিষ্কার GST বিল';
      document.getElementById('pil2D').textContent = 'পয়সা পর্যন্ত নির্ভুল হিসাব ও ঝকঝকে রসিদ।';
      document.getElementById('pil3T').textContent = 'দোকানের নিজস্ব ব্যাকআপ';
      document.getElementById('pil3D').textContent = 'সহজ লোকাল ব্যাকআপ ও পুনরুদ্ধার।';

      document.getElementById('folderKicker').textContent = 'প্রতিদিনের ওয়ার্কস্পেস';
      document.getElementById('folderHeading').textContent = 'শুরু করতে একটি ফোল্ডার খুলুন।';
      document.getElementById('folderHint').textContent = 'কার্সর রাখুন বা ফোকাস করুন';

      document.getElementById('f1Tab').textContent = '০১ · কাউন্টার';
      document.getElementById('f1S3K').textContent = 'চেকআউট';
      document.getElementById('f1S3H').textContent = 'দ্রুত কাউন্টার বিলিং';
      document.getElementById('f1S2K').textContent = 'রসিদ';
      document.getElementById('f1S2H').textContent = 'পরিষ্কার GST ও কাস্টমার প্রিন্ট';
      document.getElementById('f1S1K').textContent = 'ইনপুট';
      document.getElementById('f1S1H').textContent = 'কিবোর্ড ও বারকোড প্রস্তুত';
      document.getElementById('f1Title').textContent = 'বিলিং';
      document.getElementById('f1Sub').textContent = 'বিল তৈরি করুন এবং রসিদ প্রিন্ট করুন।';
      document.getElementById('f1Cta').textContent = 'বিলিং শুরু করুন';

      document.getElementById('f2Tab').textContent = '০২ · স্টক';
      document.getElementById('f2S3K').textContent = 'অগ্রাধিকার';
      document.getElementById('f2S3H').textContent = 'আগে মেয়াদ শেষ ব্যাচ বাছাই';
      document.getElementById('f2S2K').textContent = 'নজরদারি';
      document.getElementById('f2S1K').textContent = 'সুরক্ষা';
      document.getElementById('f2S1H').textContent = 'ব্যাচ ও মেয়াদ নিয়ন্ত্রণ';
      document.getElementById('f2Title').textContent = 'ইনভেন্টরি';
      document.getElementById('f2Sub').textContent = 'ব্যাচ ও মেয়াদ সতর্কতা দেখুন।';
      document.getElementById('f2Cta').textContent = 'স্টক দেখুন';

      document.getElementById('f3Tab').textContent = '০৩ · স্টুডিও';
      document.getElementById('f3S3K').textContent = 'মূলনীতি';
      document.getElementById('f3S3H').textContent = 'পাঁচটি মূলনীতি';
      document.getElementById('f3S2K').textContent = 'সংস্করণ';
      document.getElementById('f3S1K').textContent = 'প্রাথমিক নাম';
      document.getElementById('f3S1H').textContent = 'Rindle Works স্টুডিও';
      document.getElementById('f3Title').textContent = 'আমাদের কথা';
      document.getElementById('f3Sub').textContent = 'গল্প, মূলনীতি ও সহায়তা।';
      document.getElementById('f3Cta').textContent = 'আরও পড়ুন';

      document.getElementById('footerSay').textContent = 'কাউন্টারের প্রতিদিনের কাজে আপনার সাথে।';
      document.getElementById('lnkBilling').textContent = 'বিলিং শুরু করুন';
      document.getElementById('lnkAbout').textContent = 'আমাদের সম্পর্কে';
      document.getElementById('lnkTop').textContent = 'উপরে যান ↑';
      document.getElementById('friezeCue').textContent = 'নিচে স্ক্রোল করুন →';
    }

    document.getElementById('btnBilling').addEventListener('click', function () { send('start_billing'); });
    document.getElementById('btnAddStock').addEventListener('click', function () { send('add_stock'); });
    document.getElementById('lnkBilling').addEventListener('click', function () { send('start_billing'); });
    document.getElementById('lnkAbout').addEventListener('click', function () { send('nav_about'); });
    document.getElementById('lnkTop').addEventListener('click', function () {
      window.scrollTo({ top: 0, behavior: sysReduced ? 'auto' : 'smooth' });
    });

    // Subtle pointer parallax on Kaito Note cobalt hemisphere
    var kaitoHero = document.getElementById('kaitoHero');
    var kaitoOrb = document.getElementById('kaitoOrb');
    if (!sysReduced && kaitoHero && kaitoOrb) {
      kaitoHero.addEventListener('pointermove', function (e) {
        if (e.pointerType === 'touch') return;
        var r = kaitoHero.getBoundingClientRect();
        var nx = ((e.clientX - r.left) / Math.max(1, r.width) - 0.5) * 16;
        var ny = ((e.clientY - r.top) / Math.max(1, r.height) - 0.5) * 12;
        kaitoOrb.style.transform = 'translate3d(' + nx.toFixed(1) + 'px,' + ny.toFixed(1) + 'px,0)';
      });
      kaitoHero.addEventListener('pointerleave', function () { kaitoOrb.style.transform = ''; });
    }

    // =========================================================================
    // ACT 2: GSAP VAULT ACID-YELLOW X-RAY LANTERN (SELF-TERMINATING rAF)
    // =========================================================================
    var xraySec = document.getElementById('xraySec');
    var xrayReveal = document.getElementById('xrayReveal');
    var xRect = xrayReveal.getBoundingClientRect();
    var xState = { x: 240, y: 120, tx: 240, ty: 120, a: 0, ta: 0, raf: 0, lastT: 0, inView: false, touchTimer: 0 };

    function applyXray() {
      xraySec.style.setProperty('--lx', xState.x.toFixed(1) + 'px');
      xraySec.style.setProperty('--ly', xState.y.toFixed(1) + 'px');
      xraySec.style.setProperty('--la', xState.a.toFixed(3));
    }

    function stepXray(now) {
      xState.raf = 0;
      if (sysReduced || !xState.inView || document.hidden) return;
      var dt = xState.lastT ? Math.min(0.05, (now - xState.lastT) / 1000) : 0.016;
      xState.lastT = now;
      var k = 1 - Math.exp(-8.5 * dt);
      xState.x += (xState.tx - xState.x) * k;
      xState.y += (xState.ty - xState.y) * k;
      xState.a += (xState.ta - xState.a) * (1 - Math.exp(-9.0 * dt));
      applyXray();
      if (Math.abs(xState.tx - xState.x) > 0.3 || Math.abs(xState.ty - xState.y) > 0.3 || Math.abs(xState.ta - xState.a) > 0.005) {
        xState.raf = requestAnimationFrame(stepXray);
      } else {
        xState.x = xState.tx; xState.y = xState.ty; xState.a = xState.ta;
        applyXray(); xState.lastT = 0;
      }
    }

    function kickXray() {
      if (!sysReduced && xState.inView && !document.hidden && !xState.raf) {
        xState.lastT = 0;
        xState.raf = requestAnimationFrame(stepXray);
      }
    }

    function extinguishXray() {
      xState.ta = 0;
      xraySec.classList.remove('xray-on');
      if (xState.touchTimer) { clearTimeout(xState.touchTimer); xState.touchTimer = 0; }
      kickXray();
    }

    if (!sysReduced) {
      xraySec.addEventListener('pointerenter', function (e) {
        if (e.pointerType === 'touch') return;
        xRect = xrayReveal.getBoundingClientRect();
        xState.x = xState.tx = e.clientX - xRect.left;
        xState.y = xState.ty = e.clientY - xRect.top;
        xState.ta = 1;
        xraySec.classList.add('xray-on');
        kickXray();
      });
      xraySec.addEventListener('pointermove', function (e) {
        if (e.pointerType === 'touch') return;
        xRect = xrayReveal.getBoundingClientRect();
        xState.tx = e.clientX - xRect.left;
        xState.ty = e.clientY - xRect.top;
        if (xState.ta !== 1) { xState.ta = 1; xraySec.classList.add('xray-on'); }
        kickXray();
      });
      xraySec.addEventListener('pointerleave', extinguishXray);
      xraySec.addEventListener('pointercancel', extinguishXray);
      xraySec.addEventListener('pointerdown', function (e) {
        if (e.pointerType !== 'touch') return;
        xRect = xrayReveal.getBoundingClientRect();
        xState.tx = e.clientX - xRect.left;
        xState.ty = e.clientY - xRect.top;
        xState.ta = 1;
        xraySec.classList.add('xray-on');
        kickXray();
        if (xState.touchTimer) clearTimeout(xState.touchTimer);
        xState.touchTimer = setTimeout(extinguishXray, 1400);
      });
    }

    // =========================================================================
    // ACT 3: FRAMER PHYSICAL PASTEL FOLDERS (EXACT REVERSIBLE STATE)
    // =========================================================================
    var folders = Array.prototype.slice.call(document.querySelectorAll('.pastel-folder'));
    function closeAllFolders(except) {
      folders.forEach(function (f) { if (f !== except) f.classList.remove('is-open'); });
    }
    folders.forEach(function (folder) {
      folder.addEventListener('pointerenter', function (e) {
        if (e.pointerType === 'touch') return;
        closeAllFolders(folder);
        folder.classList.add('is-open');
      });
      folder.addEventListener('pointerleave', function (e) {
        if (e.pointerType === 'touch') return;
        folder.classList.remove('is-open');
      });
      folder.addEventListener('pointercancel', function () { folder.classList.remove('is-open'); });
      folder.addEventListener('focus', function () { closeAllFolders(folder); folder.classList.add('is-open'); });
      folder.addEventListener('blur', function () { folder.classList.remove('is-open'); });
      folder.addEventListener('click', function (e) {
        var action = folder.getAttribute('data-action');
        if (e.pointerType === 'touch' && !folder.classList.contains('is-open')) {
          e.preventDefault();
          closeAllFolders(folder);
          folder.classList.add('is-open');
          return;
        }
        closeAllFolders(null);
        if (action) send(action);
      });
    });
    document.addEventListener('pointerdown', function (e) {
      if (!e.target.closest('.pastel-folder')) closeAllFolders(null);
    });

    // =========================================================================
    // ACT 4: LONGSHOT / VIDEO 75300 SCROLL-SCRUBBED MONOCHROME FRIEZE
    // =========================================================================
    var scrollBar = document.getElementById('scrollBar');
    var friezeBand = document.getElementById('friezeBand');
    var friezeTrack = document.getElementById('friezeTrack');
    var ghostOrva = document.getElementById('ghostOrva');
    var fState = { p: 0.5, tp: 0.5, raf: 0, inView: false, lastT: 0 };

    function computeFriezeProgress() {
      var vh = window.innerHeight || 760;
      var r = friezeBand.getBoundingClientRect();
      var startY = vh * 0.96;
      var endY = vh - r.height;
      return Math.max(0, Math.min(1, (startY - r.top) / Math.max(1, startY - endY)));
    }

    function applyFrieze(p) {
      if (sysReduced) return;
      var narrow = window.innerWidth < 860;
      var amp = narrow ? 4.0 : 6.5;
      var gAmp = narrow ? 2.0 : 4.0;
      friezeTrack.style.transform = 'translate3d(' + ((p - 0.5) * 2 * amp).toFixed(3) + '%,0,0)';
      ghostOrva.style.transform = 'translate3d(calc(-50% + ' + ((0.5 - p) * 2 * gAmp).toFixed(3) + '%),0,0)';
    }

    function stepFrieze(now) {
      fState.raf = 0;
      if (sysReduced || !fState.inView || document.hidden) return;
      var dt = fState.lastT ? Math.min(0.05, (now - fState.lastT) / 1000) : 0.016;
      fState.lastT = now;
      fState.p += (fState.tp - fState.p) * (1 - Math.exp(-9.0 * dt));
      applyFrieze(fState.p);
      if (Math.abs(fState.tp - fState.p) > 0.001) fState.raf = requestAnimationFrame(stepFrieze);
      else { fState.p = fState.tp; applyFrieze(fState.p); fState.lastT = 0; }
    }

    function onScroll() {
      var y = window.scrollY || 0;
      var maxS = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
      scrollBar.style.width = Math.min(100, (y / maxS) * 100).toFixed(1) + '%';
      if (!sysReduced && fState.inView) {
        fState.tp = computeFriezeProgress();
        if (!fState.raf && !document.hidden) { fState.lastT = 0; fState.raf = requestAnimationFrame(stepFrieze); }
      }
    }
    window.addEventListener('scroll', onScroll, { passive: true });

    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (entries) {
        xState.inView = entries[0].isIntersecting;
        if (!xState.inView) extinguishXray();
      }, { threshold: 0 }).observe(xraySec);

      new IntersectionObserver(function (entries) {
        fState.inView = entries[0].isIntersecting;
        if (fState.inView && !sysReduced) {
          fState.tp = computeFriezeProgress(); fState.p = fState.tp; applyFrieze(fState.p);
        } else if (fState.raf) { cancelAnimationFrame(fState.raf); fState.raf = 0; }
      }, { threshold: 0 }).observe(friezeBand);
    }

    function suspendAll() {
      extinguishXray();
      closeAllFolders(null);
      if (xState.raf) { cancelAnimationFrame(xState.raf); xState.raf = 0; }
      if (fState.raf) { cancelAnimationFrame(fState.raf); fState.raf = 0; }
    }
    window.addEventListener('blur', suspendAll);
    document.addEventListener('visibilitychange', function () { if (document.hidden) suspendAll(); });

    if (!sysReduced) {
      fState.p = fState.tp = computeFriezeProgress();
      applyFrieze(fState.p);
    }
  })();
  </script>
</body>
</html>");
            return sb.ToString();
        }

        public static string BuildAboutHtml(AboutShowcaseData data)
        {
            if (data == null) data = new AboutShowcaseData();
            string payloadJson = SerializeSafeJson(data);
            string friezeSrc = GetFriezeDataUri();
            string langAttr = string.Equals(data.Language, "bn", StringComparison.OrdinalIgnoreCase) ? "bn" : "en";

            var sb = new StringBuilder(24576);
            sb.Append(@"<!DOCTYPE html>
<html lang=""");
            sb.Append(langAttr);
            sb.Append(@""">
<head>
<meta charset=""UTF-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>ORVA — About Us</title>
<link rel=""preconnect"" href=""https://fonts.googleapis.com"" />
<link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin />
<link href=""https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Instrument+Serif:ital@0;1&family=Noto+Sans+Bengali:wght@400;600;700&family=Noto+Serif+Bengali:wght@400;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700&family=Space+Grotesk:wght@500;600;700&display=swap"" rel=""stylesheet"" />
<style>
  :root {
    --canvas-black: #060709;
    --canvas-deep: #0B0C10;
    --canvas-secondary: #101218;
    --ink-chalk: #F4F4F0;
    --ink-secondary: #B4B6BE;
    --ink-muted: #7C808C;
    --hair: rgba(244, 244, 240, 0.13);
    --hair-strong: rgba(244, 244, 240, 0.28);

    /* Ref 5: Elementum Acid-Lime Micro Anchors + Kaito Cobalt/Violet Wave */
    --elem-lime: #C4F042;
    --accent-electric: #8B7CFF;
    --accent-highlight: #B4A9FF;
    --accent-tint: rgba(139, 124, 255, 0.10);

    --font-display: 'Space Grotesk', 'Noto Sans', 'Noto Sans Bengali', 'Nirmala UI', 'Segoe UI', sans-serif;
    --font-body: 'Plus Jakarta Sans', 'Noto Sans', 'Noto Sans Bengali', 'Nirmala UI', 'Segoe UI', sans-serif;
    --font-mono: 'IBM Plex Mono', 'Noto Sans Mono', 'Noto Sans Bengali', Consolas, monospace;
    --ease-out: cubic-bezier(0.22, 1, 0.36, 1);
  }

  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body {
    background: var(--canvas-black);
    color: var(--ink-chalk);
    font-family: var(--font-body);
    font-size: 16px;
    line-height: 1.68;
    -webkit-font-smoothing: antialiased;
    overflow-x: hidden;
  }
  html[lang=""bn""] body {
    font-family: 'Noto Sans Bengali', 'Nirmala UI', 'Plus Jakarta Sans', sans-serif;
    line-height: 1.78;
  }
  html[lang=""bn""] .elem-mega {
    font-family: 'Noto Serif Bengali', 'Noto Sans Bengali', 'Nirmala UI', sans-serif;
    letter-spacing: 0 !important;
  }

  @keyframes orvaEnter { from { opacity: 0; } to { opacity: 1; } }
  main { opacity: 1; animation: orvaEnter 220ms var(--ease-out) both; }

  .scroll-bar {
    position: fixed; top: 0; left: 0; height: 2px; width: 0%;
    background: linear-gradient(90deg, var(--accent-electric), var(--elem-lime));
    z-index: 95; pointer-events: none;
  }
  .container {
    width: 100%;
    max-width: 1200px;
    margin: 0 auto;
    padding: 0 clamp(22px, 4.2vw, 52px);
  }
  .mono {
    font-family: var(--font-mono);
    font-size: 11.5px;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--ink-muted);
    white-space: nowrap;
  }
  html[lang=""bn""] .mono { letter-spacing: 0; text-transform: none; }

  /* =========================================================================
     ACT 1 — ELEMENTUM ATMOSPHERIC ARCH COMPOSITION (About Us Hero)
     ========================================================================= */
  .elem-hero {
    position: relative;
    min-height: clamp(460px, 68vh, 640px);
    background:
      radial-gradient(ellipse 34% 62% at 50% 54%, rgba(210, 214, 224, 0.24) 0%, rgba(92, 98, 112, 0.14) 44%, rgba(16, 18, 24, 0.92) 76%, #060709 100%);
    border-bottom: 1px solid var(--hair);
    overflow: hidden;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    padding: 28px 0 42px;
  }
  .elem-arch-svg {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    pointer-events: none;
    z-index: 1;
  }
  .elem-top {
    position: relative;
    z-index: 3;
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 16px;
    align-items: start;
  }
  .elem-lime {
    font-family: var(--font-mono);
    font-size: 11px;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--elem-lime);
  }
  .elem-mega-wrap {
    position: relative;
    z-index: 3;
    text-align: center;
    padding: 24px 0 10px;
  }
  .elem-mega {
    font-family: var(--font-display);
    font-weight: 700;
    font-size: clamp(58px, 11.2vw, 152px);
    line-height: 0.88;
    letter-spacing: -0.04em;
    color: var(--ink-chalk);
    text-transform: uppercase;
    user-select: none;
  }
  .elem-bottom {
    position: relative;
    z-index: 3;
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 28px;
    align-items: end;
    margin-top: 24px;
  }
  .elem-callout {
    max-width: 44ch;
    background: rgba(11, 12, 16, 0.72);
    border: 1px solid var(--hair);
    padding: 20px 22px;
  }
  .elem-callout h1 {
    font-family: var(--font-display);
    font-size: clamp(22px, 2.3vw, 28px);
    font-weight: 600;
    line-height: 1.22;
    color: var(--ink-chalk);
    margin-bottom: 10px;
  }
  .elem-callout p {
    font-size: 14.5px;
    color: var(--ink-secondary);
    line-height: 1.62;
  }
  .elem-tag {
    display: inline-block;
    margin-top: 12px;
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--elem-lime);
    letter-spacing: 0.08em;
  }

  /* ACT 2: GROUNDED STORY */
  .story-sec {
    background: var(--canvas-deep);
    padding: clamp(52px, 6.5vw, 76px) 0;
    border-bottom: 1px solid var(--hair);
  }
  .story-grid {
    display: grid;
    grid-template-columns: 0.36fr 1fr;
    gap: 44px;
    align-items: start;
  }
  .story-grid p {
    font-size: 16.5px;
    line-height: 1.76;
    color: var(--ink-chalk);
    max-width: 62ch;
  }
  .story-grid p + p {
    margin-top: 16px;
    color: var(--ink-secondary);
  }

  /* ACT 3: FIVE-NODE CURVED PRINCIPLES TIMELINE */
  .wave-sec {
    background: var(--canvas-secondary);
    padding: clamp(56px, 7vw, 84px) 0;
    border-bottom: 1px solid var(--hair);
  }
  .wave-svg {
    width: 100%;
    height: 96px;
    display: block;
    overflow: visible;
    margin: 26px 0 22px;
  }
  .wave-track { fill: none; stroke: var(--hair-strong); stroke-width: 1.2; }
  .wave-glow {
    fill: none;
    stroke: var(--accent-electric);
    stroke-width: 2;
    stroke-linecap: round;
    filter: drop-shadow(0 0 6px rgba(139, 124, 255, 0.45));
    transition: stroke-dashoffset 440ms var(--ease-out);
  }
  .w-node { cursor: pointer; outline: none; }
  .w-dot {
    fill: var(--canvas-black);
    stroke: var(--hair-strong);
    stroke-width: 1.6;
    transition: fill 200ms ease, stroke 200ms ease, filter 200ms ease;
  }
  .w-node.active .w-dot,
  .w-node:hover .w-dot,
  .w-node:focus-visible .w-dot {
    fill: var(--elem-lime);
    stroke: var(--accent-highlight);
    filter: drop-shadow(0 0 8px rgba(196, 240, 66, 0.65));
  }

  .p-cols {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 18px;
  }
  .p-item {
    background: transparent;
    border: none;
    border-top: 1.5px solid var(--hair);
    padding: 16px 12px;
    text-align: left;
    cursor: pointer;
    font-family: inherit;
    transition: border-color 200ms ease, background 200ms ease;
  }
  .p-item.active, .p-item:hover {
    border-top-color: var(--elem-lime);
    background: var(--accent-tint);
  }
  .p-item:focus-visible, .action-link:focus-visible {
    outline: 2px solid var(--elem-lime);
    outline-offset: 3px;
  }
  .p-item h4 {
    font-family: var(--font-mono);
    font-size: 12px;
    font-weight: 500;
    color: var(--ink-chalk);
    margin-bottom: 8px;
    line-height: 1.45;
  }
  .p-item.active h4 { color: var(--elem-lime); }
  .p-item p {
    font-size: 13.5px;
    color: var(--ink-secondary);
    line-height: 1.6;
  }

  /* ACT 4: SUPPORT, OWNERSHIP & RETURN NAVIGATION */
  .meta-sec {
    background: var(--canvas-deep);
    padding: clamp(52px, 6.5vw, 76px) 0;
  }
  .meta-split {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 48px;
  }
  .meta-col h2 {
    font-family: var(--font-display);
    font-size: 26px;
    font-weight: 600;
    letter-spacing: -0.02em;
    margin: 10px 0 18px;
    color: var(--ink-chalk);
  }
  .kv {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: 16px;
    padding: 12px 0;
    border-bottom: 1px solid var(--hair);
    font-size: 14.5px;
    color: var(--ink-secondary);
  }
  .kv strong {
    font-family: var(--font-mono);
    font-size: 13px;
    color: var(--ink-chalk);
    font-weight: 500;
    text-align: right;
  }
  .fine {
    font-size: 13.5px;
    color: var(--ink-muted);
    margin-top: 16px;
    line-height: 1.65;
  }
  .return-links {
    display: flex;
    align-items: center;
    gap: 14px;
    margin-top: 24px;
    flex-wrap: wrap;
  }
  .action-link {
    background: transparent;
    border: 1px solid var(--hair-strong);
    color: var(--ink-chalk);
    padding: 10px 18px;
    border-radius: 6px;
    font-family: var(--font-body);
    font-size: 13.5px;
    font-weight: 600;
    cursor: pointer;
    transition: border-color 140ms ease, background 140ms ease;
  }
  .action-link:hover { border-color: var(--elem-lime); background: rgba(244, 244, 240, 0.06); }
  .action-link.primary { background: var(--accent-electric); border-color: var(--accent-electric); color: #060709; }
  .action-link.primary:hover { background: var(--accent-highlight); border-color: var(--accent-highlight); }

  /* SHARED SCROLL-SCRUBBED MONOCHROME FRIEZE */
  .frieze-band {
    position: relative;
    background: #F4F4F0;
    color: #050608;
    overflow: hidden;
    padding-top: 16px;
  }
  .ghost-orva {
    position: absolute;
    top: 0;
    left: 50%;
    transform: translate3d(-50%, 0, 0);
    font-family: var(--font-display);
    font-weight: 700;
    font-size: clamp(104px, 20vw, 248px);
    line-height: 0.88;
    letter-spacing: 0.05em;
    color: rgba(5, 6, 8, 0.08);
    pointer-events: none;
    user-select: none;
    white-space: nowrap;
    will-change: transform;
  }
  .frieze-track {
    position: relative;
    z-index: 2;
    width: 118%;
    margin-left: -9%;
    will-change: transform;
  }
  .frieze-img {
    display: block;
    width: min(1260px, 100%);
    height: auto;
    margin: 0 auto;
    mix-blend-mode: multiply;
  }

  @media (max-width: 960px) {
    .elem-top { grid-template-columns: repeat(2, 1fr); }
    .elem-bottom { grid-template-columns: 1fr; }
    .p-cols { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  }
  @media (max-width: 820px) {
    .story-grid, .meta-split { grid-template-columns: 1fr; gap: 22px; }
    .frieze-track { width: 110%; margin-left: -5%; }
  }
  @media (prefers-reduced-motion: reduce) {
    main, .wave-glow { transition: none !important; animation: none !important; }
    .frieze-track { width: 100% !important; margin-left: 0 !important; transform: none !important; }
    .ghost-orva { transform: translate3d(-50%, 0, 0) !important; }
  }
  body.reduced-motion main,
  body.reduced-motion .wave-glow { transition: none !important; animation: none !important; }
  body.reduced-motion .frieze-track { width: 100% !important; margin-left: 0 !important; transform: none !important; }
  body.reduced-motion .ghost-orva { transform: translate3d(-50%, 0, 0) !important; }
</style>
</head>
<body>
  <div class=""scroll-bar"" id=""scrollBar""></div>
  <main id=""appMain"">

    <!-- ACT 1: ELEMENTUM ATMOSPHERIC ARCH HERO -->
    <section class=""elem-hero"">
      <svg class=""elem-arch-svg"" viewBox=""0 0 1200 620"" preserveAspectRatio=""xMidYMid slice"" aria-hidden=""true"">
        <g fill=""none"" stroke=""rgba(244,244,240,0.14)"" stroke-width=""1"">
          <path d=""M 240,620 L 240,260 A 360,250 0 0,1 960,260 L 960,620"" />
          <path d=""M 310,620 L 310,275 A 290,220 0 0,1 890,275 L 890,620"" stroke=""rgba(244,244,240,0.22)"" />
          <path d=""M 380,620 L 380,290 A 220,180 0 0,1 820,290 L 820,620"" stroke=""rgba(244,244,240,0.10)"" />
          <line x1=""0"" y1=""164"" x2=""1200"" y2=""164"" stroke=""rgba(244,244,240,0.08)"" />
          <line x1=""600"" y1=""0"" x2=""600"" y2=""620"" stroke=""rgba(244,244,240,0.06)"" />
        </g>
      </svg>

      <div class=""container elem-top"">
        <span class=""mono"" id=""kickerLeft"">ORVA — ABOUT US</span>
        <span class=""elem-lime"" id=""elemTag1"">LOCAL BY DESIGN</span>
        <span class=""elem-lime"" id=""elemTag2"">PHARMACY PRECISION</span>
        <span class=""mono"" style=""text-align:right;"" id=""kickerRight"">RINDLE WORKS · V4.0.0</span>
      </div>

      <div class=""container elem-mega-wrap"">
        <div class=""elem-mega"" id=""elemMega"">ORVA</div>
      </div>

      <div class=""container elem-bottom"">
        <div class=""elem-callout"">
          <h1 id=""heroH1"">Made for the work behind the counter.</h1>
          <p id=""heroLead"">Clear bills. Careful stock. Your records, kept close.</p>
          <span class=""elem-tag"" id=""elemBracket"">[ ABOUT RINDLE WORKS ]</span>
        </div>
        <div style=""text-align:right;"">
          <span class=""mono"" id=""elemCoord"">WEST BENGAL · INDIA · OFFLINE DESKTOP</span>
        </div>
      </div>
    </section>

    <section class=""story-sec"">
      <div class=""container"">
        <div class=""story-grid"">
          <div><span class=""mono"" id=""storyKicker"">OUR STORY</span></div>
          <div>
            <p id=""storyP1""></p>
            <p id=""storyP2""></p>
          </div>
        </div>
      </div>
    </section>

    <section class=""wave-sec"" id=""waveSec"">
      <div class=""container"">
        <span class=""mono"" id=""prinKicker"">THE PRINCIPLES BEHIND ORVA</span>
        <svg class=""wave-svg"" viewBox=""0 0 1000 96"" preserveAspectRatio=""none"" aria-label=""Five principles wave"">
          <path class=""wave-track"" d=""M 50,58 C 150,14 210,14 275,54 C 340,92 420,88 500,40 C 580,4 660,16 725,60 C 790,94 870,82 950,34"" />
          <path id=""waveGlow"" class=""wave-glow"" d=""M 50,58 C 150,14 210,14 275,54 C 340,92 420,88 500,40 C 580,4 660,16 725,60 C 790,94 870,82 950,34"" />
          <g class=""w-node"" data-i=""0"" tabindex=""0"" role=""button"" aria-pressed=""true"" transform=""translate(50,58)""><circle class=""w-dot"" r=""5"" /></g>
          <g class=""w-node"" data-i=""1"" tabindex=""0"" role=""button"" aria-pressed=""false"" transform=""translate(275,54)""><circle class=""w-dot"" r=""5"" /></g>
          <g class=""w-node"" data-i=""2"" tabindex=""0"" role=""button"" aria-pressed=""false"" transform=""translate(500,40)""><circle class=""w-dot"" r=""5"" /></g>
          <g class=""w-node"" data-i=""3"" tabindex=""0"" role=""button"" aria-pressed=""false"" transform=""translate(725,60)""><circle class=""w-dot"" r=""5"" /></g>
          <g class=""w-node"" data-i=""4"" tabindex=""0"" role=""button"" aria-pressed=""false"" transform=""translate(950,34)""><circle class=""w-dot"" r=""5"" /></g>
        </svg>
        <div class=""p-cols"" id=""pCols""></div>
      </div>
    </section>

    <section class=""meta-sec"">
      <div class=""container"">
        <div class=""meta-split"">
          <div class=""meta-col"">
            <span class=""mono"" id=""supKicker"">SUPPORT &amp; CONTACT</span>
            <h2 id=""supTitle"">Need assistance?</h2>
            <div id=""supportRows""></div>
            <p class=""fine"" id=""supNote""></p>
          </div>

          <div class=""meta-col"">
            <span class=""mono"" id=""prodKicker"">PRODUCT &amp; OWNERSHIP</span>
            <h2>ORVA</h2>
            <div class=""kv""><span id=""lblSub"">Product</span><strong id=""valSub""></strong></div>
            <div class=""kv""><span id=""lblDev"">Developed by</span><strong>Rindle Works</strong></div>
            <div class=""kv""><span id=""lblVer"">Version</span><strong id=""valVer""></strong></div>
            <div class=""kv""><span id=""lblBuild"">Build</span><strong id=""valBuild""></strong></div>
            <p class=""fine"" id=""prodNote""></p>
            <div class=""return-links"">
              <button type=""button"" class=""action-link primary"" id=""btnStartBilling"">Start Billing →</button>
              <button type=""button"" class=""action-link"" id=""btnBackHome"">← Back to Home</button>
            </div>
          </div>
        </div>
      </div>
    </section>

    <div class=""frieze-band"" id=""friezeBand"">
      <div class=""ghost-orva"" id=""ghostOrva"" aria-hidden=""true"">ORVA.</div>
      <div class=""frieze-track"" id=""friezeTrack"">");
            if (!string.IsNullOrEmpty(friezeSrc))
            {
                sb.Append(@"<img class=""frieze-img"" src=""");
                sb.Append(friezeSrc);
                sb.Append(@""" alt=""ORVA Pharmacy Counter Illustration"" />");
            }
            sb.Append(@"</div>
    </div>
  </main>

  <script id=""orva-data"" type=""application/json"">");
            sb.Append(payloadJson);
            sb.Append(@"</script>

  <script>
  (function () {
    'use strict';

    var DATA = {};
    try { DATA = JSON.parse(document.getElementById('orva-data').textContent || '{}'); } catch (e) { DATA = {}; }
    var isBn = (DATA.Language || 'en').toLowerCase() === 'bn';
    var sysReduced = !!(DATA.ReducedMotion || (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches));
    if (sysReduced) document.body.classList.add('reduced-motion');

    function send(action) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ action: action }));
      }
    }

    var COPY = isBn ? {
      kickerLeft: 'ORVA — আমাদের কথা',
      elemTag1: 'স্থানীয় ব্যবস্থায়',
      elemTag2: 'ফার্মেসি শৃঙ্খলা',
      elemBracket: '[ RINDLE WORKS পরিচিতি ]',
      elemCoord: 'পশ্চিমবঙ্গ · ভারত · অফলাইন ডেস্কটপ',
      heroH1: 'কাউন্টারের প্রতিদিনের কাজের জন্য তৈরি।',
      heroLead: 'পরিষ্কার বিল। সতর্ক স্টক ব্যবস্থাপনা। আপনার রেকর্ড, আপনার কাছেই।',
      storyKicker: 'আমাদের কথা',
      storyP1: 'ORVA-এর পেছনের নির্মাণ-পরিচয় Rindle Works। স্বাধীন ফার্মেসির প্রতিদিনের বাস্তব প্রয়োজন—দ্রুত বিলিং, ব্যাচ ও মেয়াদের সঠিক হিসাব এবং নির্ভরযোগ্য ব্যবসায়িক রেকর্ড—মাথায় রেখেই ORVA তৈরি হচ্ছে।',
      storyP2: 'আমাদের উদ্দেশ্য সহজ: প্রয়োজনীয় দোকানের কাজগুলোকে আরও গুছিয়ে দেওয়া, সফটওয়্যারকে জটিল না করে। ORVA দৈনন্দিন কাজকে স্থানীয়ভাবে পরিচালিত, ব্যবহারিক এবং দোকানের নিজস্ব নিয়ন্ত্রণে রাখতে সাহায্য করে।',
      prinKicker: 'ORVA-এর পাঁচটি মূলনীতি',
      principles: [
        ['০১ — স্থানীয় ব্যবস্থায়, ইন্টারনেট ছাড়াও', 'দৈনন্দিন বিলিং ও স্টকের কাজ ইন্টারনেট সংযোগ ছাড়াই চালিয়ে যাওয়ার জন্য ORVA তৈরি।'],
        ['০২ — পয়সা পর্যন্ত নির্ভুল হিসাব', 'বিলের পরিমাণ ও GST-এর হিসাব পূর্ণ পয়সার এককে করা হয়, যাতে আর্থিক হিসাব নির্ভুল থাকে।'],
        ['০৩ — প্রতিটি ব্যাচ গুরুত্বপূর্ণ', 'ব্যাচ নম্বর, মেয়াদ এবং আগে মেয়াদ শেষ হওয়া ব্যাচ আগে বাছাইয়ের মাধ্যমে মেয়াদোত্তীর্ণ বা অনুপযুক্ত স্টক সাধারণ বিক্রি থেকে বাদ রাখা হয়।'],
        ['০৪ — আপনার রেকর্ড, আপনার নিয়ন্ত্রণে', 'দোকানের তথ্য স্থানীয় স্টোরেজে থাকে। .shopbackup ফাইলের মাধ্যমে দোকানের মালিক নিজেই ব্যাকআপ, পুনরুদ্ধার ও অন্য কম্পিউটারে তথ্য স্থানান্তর করতে পারেন।'],
        ['০৫ — ফার্মেসির বাস্তব কাজের কথা ভেবে', 'কিবোর্ডের মাধ্যমে দ্রুত বিলিং, বারকোড স্ক্যানিং এবং বাংলা–ইংরেজি ভাষার সুবিধা প্রতিদিনের কাজকে সহজ ও গতিশীল রাখার জন্য তৈরি।']
      ],
      supKicker: 'সহায়তা ও যোগাযোগ',
      supTitle: 'সাহায্য প্রয়োজন?',
      lblPhone: 'ফোন',
      lblEmail: 'ইমেল',
      supNote: 'যোগাযোগের তথ্য অ্যাপ্লিকেশনের সেটিংস থেকে প্রদর্শিত হয়।',
      supEmpty: 'সহায়তার যোগাযোগের তথ্য এখনো সেটিংসে যুক্ত করা হয়নি।',
      prodKicker: 'পণ্য ও পরিচিতি',
      lblSub: 'পণ্য',
      valSub: 'ফার্মেসি বিলিং ও ইনভেন্টরি',
      lblDev: 'নির্মাতা',
      lblVer: 'সংস্করণ',
      lblBuild: 'বিল্ড',
      prodNote: 'স্টুডিওর নাম: আপাতত ব্যবহৃত; ভারতে ট্রেডমার্ক যাচাই ও অনুমোদন বাকি। আপনার দোকানের ব্যবসায়িক রেকর্ড স্থানীয় স্টোরেজে থাকে এবং তার নিয়ন্ত্রণ আপনার কাছেই থাকে।',
      btnBilling: 'বিলিং শুরু করুন →',
      btnHome: '← হোমে ফিরে যান'
    } : {
      kickerLeft: 'ORVA — ABOUT US',
      elemTag1: 'LOCAL BY DESIGN',
      elemTag2: 'PHARMACY PRECISION',
      elemBracket: '[ ABOUT RINDLE WORKS ]',
      elemCoord: 'WEST BENGAL · INDIA · OFFLINE DESKTOP',
      heroH1: 'Made for the work behind the counter.',
      heroLead: 'Clear bills. Careful stock. Your records, kept close.',
      storyKicker: 'OUR STORY',
      storyP1: 'Rindle Works is the working studio name behind ORVA. We are building ORVA around the everyday realities of independent pharmacies: quick billing, careful batch and expiry management, and reliable business records.',
      storyP2: 'Our aim is simple: make essential shop operations easier to manage, without making the software itself complicated. ORVA keeps everyday work local, practical and under the shop\'s control.',
      prinKicker: 'THE PRINCIPLES BEHIND ORVA',
      principles: [
        ['01 — LOCAL BY DESIGN', 'Everyday billing and inventory operations are designed to work without an internet connection.'],
        ['02 — ACCURATE TO THE PAISE', 'Invoice amounts and GST calculations use integer-paise precision for careful financial handling.'],
        ['03 — EVERY BATCH MATTERS', 'Batch numbers, expiry dates and earliest-expiry-first stock selection help ensure that unsuitable stock is excluded from ordinary sales.'],
        ['04 — YOUR RECORDS, YOUR CONTROL', 'Shop data is stored locally. The portable .shopbackup file gives the shop owner control over backup, recovery and migration.'],
        ['05 — MADE FOR REAL COUNTERS', 'Keyboard-friendly billing, barcode scanning and English–Bengali support are designed around the pace of everyday pharmacy work.']
      ],
      supKicker: 'SUPPORT & CONTACT',
      supTitle: 'Need assistance?',
      lblPhone: 'Phone',
      lblEmail: 'Email',
      supNote: 'Contact details are provided by the application settings.',
      supEmpty: 'Support contact details are not configured yet. Add them in Settings.',
      prodKicker: 'PRODUCT & OWNERSHIP',
      lblSub: 'Product',
      valSub: 'Pharmacy Billing & Inventory',
      lblDev: 'Developed by',
      lblVer: 'Version',
      lblBuild: 'Build',
      prodNote: 'Studio name: Provisional; Indian trademark clearance pending. Your shop\'s business records remain in local storage under your control.',
      btnBilling: 'Start Billing →',
      btnHome: '← Back to Home'
    };

    var ver = DATA.AppVersion || '4.0.0';
    var build = DATA.BuildNumber || '0';
    document.getElementById('kickerLeft').textContent = COPY.kickerLeft;
    document.getElementById('elemTag1').textContent = COPY.elemTag1;
    document.getElementById('elemTag2').textContent = COPY.elemTag2;
    document.getElementById('elemBracket').textContent = COPY.elemBracket;
    document.getElementById('elemCoord').textContent = COPY.elemCoord;
    document.getElementById('kickerRight').textContent = 'RINDLE WORKS · V' + ver;
    document.getElementById('heroH1').textContent = COPY.heroH1;
    document.getElementById('heroLead').textContent = COPY.heroLead;
    document.getElementById('storyKicker').textContent = COPY.storyKicker;
    document.getElementById('storyP1').textContent = COPY.storyP1;
    document.getElementById('storyP2').textContent = COPY.storyP2;
    document.getElementById('prinKicker').textContent = COPY.prinKicker;
    document.getElementById('supKicker').textContent = COPY.supKicker;
    document.getElementById('supTitle').textContent = COPY.supTitle;
    document.getElementById('prodKicker').textContent = COPY.prodKicker;
    document.getElementById('lblSub').textContent = COPY.lblSub;
    document.getElementById('valSub').textContent = COPY.valSub;
    document.getElementById('lblDev').textContent = COPY.lblDev;
    document.getElementById('lblVer').textContent = COPY.lblVer;
    document.getElementById('lblBuild').textContent = COPY.lblBuild;
    document.getElementById('valVer').textContent = ver;
    document.getElementById('valBuild').textContent = build;
    document.getElementById('prodNote').textContent = COPY.prodNote;
    document.getElementById('btnStartBilling').textContent = COPY.btnBilling;
    document.getElementById('btnBackHome').textContent = COPY.btnHome;

    var supRows = document.getElementById('supportRows');
    var phone = (DATA.SupportPhone || '').trim();
    var email = (DATA.SupportEmail || '').trim();
    if (phone) {
      var r1 = document.createElement('div'); r1.className = 'kv';
      var l1 = document.createElement('span'); l1.textContent = COPY.lblPhone;
      var v1 = document.createElement('strong'); v1.textContent = phone;
      r1.appendChild(l1); r1.appendChild(v1); supRows.appendChild(r1);
    }
    if (email) {
      var r2 = document.createElement('div'); r2.className = 'kv';
      var l2 = document.createElement('span'); l2.textContent = COPY.lblEmail;
      var v2 = document.createElement('strong'); v2.textContent = email;
      r2.appendChild(l2); r2.appendChild(v2); supRows.appendChild(r2);
    }
    document.getElementById('supNote').textContent = (phone || email) ? COPY.supNote : COPY.supEmpty;

    document.getElementById('btnStartBilling').addEventListener('click', function () { send('start_billing'); });
    document.getElementById('btnBackHome').addEventListener('click', function () { send('nav_home'); });

    var pCols = document.getElementById('pCols');
    COPY.principles.forEach(function (item, idx) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'p-item' + (idx === 0 ? ' active' : '');
      btn.setAttribute('data-i', String(idx));
      var h4 = document.createElement('h4'); h4.textContent = item[0];
      var p = document.createElement('p'); p.textContent = item[1];
      btn.appendChild(h4); btn.appendChild(p);
      pCols.appendChild(btn);
    });

    var waveGlow = document.getElementById('waveGlow');
    var wLen = waveGlow.getTotalLength();
    waveGlow.style.strokeDasharray = String(wLen);
    var fracs = [0.04, 0.28, 0.52, 0.76, 1.0];
    var userOverride = false;

    function selectWave(idx) {
      waveGlow.style.strokeDashoffset = String(wLen * (1 - fracs[idx]));
      document.querySelectorAll('.w-node').forEach(function (n) {
        var active = Number(n.getAttribute('data-i')) === idx;
        n.classList.toggle('active', active);
        n.setAttribute('aria-pressed', active ? 'true' : 'false');
      });
      document.querySelectorAll('.p-item').forEach(function (c) {
        c.classList.toggle('active', Number(c.getAttribute('data-i')) === idx);
      });
    }
    selectWave(0);

    var waveSec = document.getElementById('waveSec');
    document.querySelectorAll('.w-node, .p-item').forEach(function (el) {
      var idx = Number(el.getAttribute('data-i'));
      el.addEventListener('mouseenter', function () { userOverride = true; selectWave(idx); });
      el.addEventListener('click', function () { userOverride = true; selectWave(idx); });
      el.addEventListener('focus', function () { userOverride = true; selectWave(idx); });
      el.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); userOverride = true; selectWave(idx); }
      });
    });
    waveSec.addEventListener('pointerleave', function () { userOverride = false; updateWaveFromScroll(); });
    waveSec.addEventListener('focusout', function (e) {
      if (!waveSec.contains(e.relatedTarget)) { userOverride = false; updateWaveFromScroll(); }
    });

    function updateWaveFromScroll() {
      if (userOverride || sysReduced) return;
      var r = waveSec.getBoundingClientRect();
      var vh = window.innerHeight || 760;
      var prog = Math.max(0, Math.min(1, (vh * 0.85 - r.top) / (vh * 0.65)));
      selectWave(Math.min(4, Math.floor(prog * 5)));
    }

    var scrollBar = document.getElementById('scrollBar');
    var friezeBand = document.getElementById('friezeBand');
    var friezeTrack = document.getElementById('friezeTrack');
    var ghostOrva = document.getElementById('ghostOrva');
    var fState = { p: 0.5, tp: 0.5, raf: 0, inView: false, lastT: 0 };

    function computeFriezeProgress() {
      var vh = window.innerHeight || 760;
      var r = friezeBand.getBoundingClientRect();
      var startY = vh * 0.96;
      var endY = vh - r.height;
      return Math.max(0, Math.min(1, (startY - r.top) / Math.max(1, startY - endY)));
    }

    function applyFrieze(p) {
      if (sysReduced) return;
      var narrow = window.innerWidth < 860;
      var amp = narrow ? 4.0 : 6.5;
      var gAmp = narrow ? 2.0 : 4.0;
      friezeTrack.style.transform = 'translate3d(' + ((p - 0.5) * 2 * amp).toFixed(3) + '%,0,0)';
      ghostOrva.style.transform = 'translate3d(calc(-50% + ' + ((0.5 - p) * 2 * gAmp).toFixed(3) + '%),0,0)';
    }

    function stepFrieze(now) {
      fState.raf = 0;
      if (sysReduced || !fState.inView || document.hidden) return;
      var dt = fState.lastT ? Math.min(0.05, (now - fState.lastT) / 1000) : 0.016;
      fState.lastT = now;
      fState.p += (fState.tp - fState.p) * (1 - Math.exp(-9.0 * dt));
      applyFrieze(fState.p);
      if (Math.abs(fState.tp - fState.p) > 0.001) fState.raf = requestAnimationFrame(stepFrieze);
      else { fState.p = fState.tp; applyFrieze(fState.p); fState.lastT = 0; }
    }

    function onScroll() {
      var y = window.scrollY || 0;
      var maxS = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
      scrollBar.style.width = Math.min(100, (y / maxS) * 100).toFixed(1) + '%';
      updateWaveFromScroll();
      if (!sysReduced && fState.inView) {
        fState.tp = computeFriezeProgress();
        if (!fState.raf && !document.hidden) { fState.lastT = 0; fState.raf = requestAnimationFrame(stepFrieze); }
      }
    }
    window.addEventListener('scroll', onScroll, { passive: true });

    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (entries) {
        fState.inView = entries[0].isIntersecting;
        if (fState.inView && !sysReduced) {
          fState.tp = computeFriezeProgress(); fState.p = fState.tp; applyFrieze(fState.p);
        } else if (fState.raf) { cancelAnimationFrame(fState.raf); fState.raf = 0; }
      }, { threshold: 0 }).observe(friezeBand);
    }

    if (!sysReduced) {
      fState.p = fState.tp = computeFriezeProgress();
      applyFrieze(fState.p);
    }
  })();
  </script>
</body>
</html>");
            return sb.ToString();
        }
    }
}
