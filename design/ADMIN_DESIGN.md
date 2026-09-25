# Design System: CinemaFlow Admin Operational Cockpit

> **Semantic Design Specification for CinemaFlow Admin Control Center**  
> Compliant with `design-taste-frontend-v1` & `stitch-design-taste`  
> Baseline Configuration: `DESIGN_VARIANCE: 7`, `MOTION_INTENSITY: 5`, `VISUAL_DENSITY: 8 (Cockpit Mode)`

---

## 1. Visual Theme & Atmosphere

An authoritative, high-density cinematic command cockpit designed for precision streaming operations and real-time content governance. The aesthetic bridges high-end media workflows with aerospace telemetry — calm, dark obsidian surfaces, crystalline 1px micro-dividers with subtle top refraction, tabular monospace numbers for data density, and surgical crimson-amber accents. The interface avoids frivolous decoration, preferring spacious content density, responsive sliding drawers, and immediate tactile feedback.

---

## 2. Color Palette & Roles

- **Canvas Void** (`#090b10`) — Root application background surface, absolute deep space.
- **Sidebar Obsidian** (`#0b0e15`) — Left persistent navigation rail, ground-truth orientation.
- **Panel Surface** (`#10141e`) — Operational tables, telemetry panels, inspector drawers.
- **Elevated Glass** (`#161b28`) — Command palette dropdowns, context popups, tooltips.
- **Whisper Border** (`rgba(255, 255, 255, 0.08)`) — 1px razor-sharp structural divider lines.
- **Refraction Highlight** (`rgba(255, 255, 255, 0.05)`) — Top-edge 1px inner refraction highlight simulating polished glass edges.
- **Primary Action Crimson** (`#e50914`, Saturation 78%) — Critical triggers, primary CTA buttons, featured indicators.
- **Telemetry Cyan** (`#06b6d4`) — Infrastructure metrics, stream bitrate, server ping latency.
- **Electric Amber** (`#f59e0b`) — VIP memberships, system warnings, pending approvals.
- **Operational Emerald** (`#10b981`) — Online health badges, completed payments, active stream status.
- **Muted Steel** (`#94a3b8`) — Secondary labels, timestamps, metadata captions.
- **Pure Text White** (`#f8fafc`) — Primary headings, titles, and active figures.

*(The generic "AI Purple/Blue Neon" aesthetic is strictly BANNED. Max 1 primary accent per context).*

---

## 3. Typography Rules

- **Display & Headings:** `Be Vietnam Pro` (or `Outfit`) — `tracking-tight`, weight 700 to 900. Scaled cleanly with weight-driven hierarchy rather than gigantic noisy sizes.
- **Body & Controls:** `Be Vietnam Pro` (or `Satoshi`) — Weight 400 to 500, relaxed leading, maximum 65ch width for narrative descriptions.
- **Technical Monospace (High-Density Mandatory):** `ui-monospace`, `SFMono-Regular`, `Menlo`, `Monaco`, `Consolas`, `JetBrains Mono` — Applied to ALL numbers, monetary currency, order codes, stream bitrates, frame rates, IDs, and timestamps with `font-variant-numeric: tabular-nums`.
- **Banned:** `Inter` for creative branding, generic serifs (Times, Georgia) on dashboards, and cartoonish fonts.

---

## 4. Component Stylings

- **Buttons:** Tactile push feedback on `:active` (`transform: scale(0.98)`). Flat aesthetic with 1px border. Primary in Cinema Crimson, Secondary in ghost outline with subtle hover brighten.
- **Command Palette (`Ctrl+K`):** Floating frosted pill/modal (`backdrop-filter: blur(16px)`), keyboard navigatable (`ArrowUp`, `ArrowDown`, `Enter`), live fuzzy filtering.
- **Tables (Cockpit Dense):** Borderless cell interiors with clean 1px row borders (`border-b border-white/5`). Monospace tabular numbers. In-line action switches (`IsTrending`, `IsFeatured`) with immediate visual confirmation.
- **Side Drawers (Inspector):** Sliding 440px wide inspection drawers for movie media, user 360° audit, and stream health testing, keeping context visible without jarring full-screen modal blocking.
- **Input Fields:** Label always sits above input. Monospace helper text below. Subtle focus ring in Crimson (`box-shadow: 0 0 0 2px rgba(229, 9, 20, 0.25)`).
- **Loaders:** Shimmering skeleton bars matching exact row/column dimensions (`animation: shimmer 1.8s infinite`). Generic circular spinners are strictly BANNED.
- **Badges & Pills:** Micro-pills with 4px inner dot indicators (`live-pulse`), semi-transparent background (`rgba(x, y, z, 0.12)`), and crisp 11px uppercase typography.

---

## 5. Layout Principles

- **Grid Systems:** Asymmetric Bento Grid (70% primary analytics / 30% live audit stream) instead of predictable 3-column rows.
- **Spacing:** Cockpit-tight padding (`0.75rem` to `1.25rem` row gaps, `1.5rem` section spacing) to maximize operational visibility above the fold.
- **Mobile Collapse:** Below 992px, the left sidebar smoothly collapses into a bottom dock or drawer menu; tables enable horizontal kinetic scrolling with sticky frozen ID columns.
- **Full Viewport Stability:** Always use `min-h-[100dvh]` and `height: 100dvh` to avoid mobile browser address bar layout jumps.

---

## 6. Motion & Interaction

- **Spring Physics:** `cubic-bezier(0.16, 1, 0.3, 1)` for smooth acceleration and crisp landing.
- **Tactile Micro-interactions:** Push depression on active buttons, micro-scale on row hover.
- **Staggered Cascade:** List items appear with a 40ms sequential delay for fluid loading feel.
- **Hardware Acceleration:** Only animate `transform` and `opacity`. Never animate `top`, `left`, `width`, or `height`.

---

## 7. Anti-Patterns (Banned)

- NO emojis anywhere in the markup, text, or table rows (use sharp SVG `CinemaIcon` primitives only).
- NO pure black (`#000000`).
- NO neon purple glowing borders or AI gradient fills.
- NO generic 3-column symmetrical card layouts.
- NO circular loading spinners.
- NO fake round numbers (`100%`, `50%`); use live realistic precision metrics.
- NO full-screen modal spam for simple property toggles; use in-line switches and sliding drawers.
