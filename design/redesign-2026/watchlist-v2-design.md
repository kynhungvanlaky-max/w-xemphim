# CinemaFlow personal library v2

Design Read: personal film library for Vietnamese viewers; cinematic Obsidian, Crimson actions, Amber membership.
DESIGN_VARIANCE: 7; MOTION_INTENSITY: 6; VISUAL_DENSITY: 5.

Image-first references generated using the built-in image generator:
- watchlist-v2-guest-reference.png: flat open guest state, two route tabs, theater photograph and VIP strip.
- watchlist-v2-grid-reference.png: saved-film collection, open portrait grid, search and sort controls.
- /images/generated/redesign-2026/library-theater.png: standalone original theater photograph, no baked-in UI.

Extracted implementation rules: 1200px content width within 1280px wrapper, 40px desktop gutters, 48px top padding; 56px heading; two-column 1.25:1 welcome layout with 40px gap; 16:9 welcome media; 2:3 poster frames; 24px collection gap; 12px media radius and 8px control radius. Be Vietnam Pro heading typography. Background #0b0c10; Crimson actions; Amber only for membership or feedback. CSS enter transitions communicate loaded content, hover communicates clickability; reduced-motion disables animations.

Adaptation: reference movies are illustrative, not production catalog data. Actual movie titles, artwork, count and membership always come from API. Guest reference's demo label replaced by explicit guest identity. No fabricated progress filters because playback history is not yet persisted. History honestly shows empty until a real history API exists. Mobile uses a single-column welcome and two-column collection.

Prompt intent: generate an Obsidian Vietnamese CinemaFlow guest-library UI with theater media, explicit login/register, two route tabs, amber VIP strip; generate a matching flat saved-film grid; generate a separate realistic red-seat theater/projector photo without text or UI. Full prompts are recorded in the task's image tool calls.

Authentication: encrypted per-tab session token, identity verified through /api/auth/me on reload. Account HTTP client is scoped so VIP and auth share the same bearer token. Authenticated saved movies use /api/library and durable user-scoped storage; legacy /api/watchlist remains demo-only compatibility. VIP is explicitly a simulation, not an integrated payment gateway.
