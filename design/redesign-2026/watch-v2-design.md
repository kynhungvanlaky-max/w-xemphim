# Watch v2: approved reference translation

Design Read: cinema-grade Vietnamese viewing workspace, Obsidian / Crimson.
DESIGN_VARIANCE: 7; MOTION_INTENSITY: 6; VISUAL_DENSITY: 5.

References: watch-v2-desktop-reference.png, watch-v2-mobile-reference.png,
watch-v2-details-reference.png. These are mockups, not real movie metadata.

## Extracted layout
- Desktop: 40px gutters at 1536px, compact breadcrumb, 72/28 player/episode split.
- Player: 16:9, 12px corners, no outer nested wrapper or ambient glow.
- Episode rail: search, flat thumbnail rows, crimson active marker, internal scroll.
- Dedicated source/quality toolbar below player; title never shares space with selects.
- Title 30-34px, subtitle 16px, previous/next buttons 44-48px high.
- Secondary save/share actions and auto-next sit below title. Buttons radius 8px.
- Mobile: one column, 16px gutters, title then two-column selectors, previous/next,
  secondary actions, toggle, episode list, information/discussion navigation.
- Lower content: poster plus synopsis/metadata, divider, readable discussion composer.
- Navigation to lower sections uses anchor links, not misleading tabs hiding both sections.

## Tokens and motion
Background #101114; surface #191b20; text #f0f1f4; muted #adb0ba;
primary button #b72d40 for readable off-white text; active text #f0808b.
Amber reserved for the existing VIP navigation. Existing logo/routes are preserved.
Motivated entrance fade and button feedback; reduced-motion removes entrance animation.

## Data / assets
Use the existing generated catalog poster/backdrop and API episode thumbnails, not
crops of mockups. The mock film name and five sample episodes are not copied to runtime.
Only real available stream variants are offered. No hardcoded HDR badge or fake seasons.
No fake comment counts, playback duration, report success, or personal library status.
Stream selection and ended/auto-next decisions remain C#. Existing browser media adapter
handles the DOM video element and native fullscreen/PiP/seek operations only.
