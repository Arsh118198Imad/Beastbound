# Beastbound – Roadmap

Each milestone must be stable and playable before the next starts.

## Milestone 1 – First playable prototype ✅ (code complete, awaiting first Editor test)
Player controller, camera, generated vale, Pebbloom, stealth AI, binding, battles, partner, Bestiary, menus, saving, audio, settings. See README.

**Immediately after the first Editor test:** fix anything found by `docs/TESTING_CHECKLIST.md`, tune numbers (speeds, detection radii, damage), and commit the Unity-generated `.meta`, `Packages/` and `ProjectSettings/` files.

## Milestone 2 – Make it feel great
- Switch to **URP** (Universal 3D), add post-processing (bloom on crystals, colour grading, ambient occlusion), Shader Graph water and wind-swept grass.
- Replace procedural player/creature models with rigged, animated models (original or properly licensed – e.g. made in Blender). Animator controllers for locomotion, attacks and reactions.
- Shader-variant collection so glowing materials keep their emission in builds.
- Better VFX: particle hits, bind ring, dust, splashes.
- Replace synthesised SFX/music with original recordings/compositions.
- Lock-on targeting option and camera framing for fights.

## Milestone 3 – More to do
- 4–6 new creature species across elements (Ember, Tide, Gale, Stone), each with a distinct silhouette, personality behaviours and 3 moves.
- Creature levelling from battles, a move upgrade path, and an evolution/transformation mechanic tied to bonds and exploration (never to purchases).
- Partner commands (attack / hold / come back) and swapping partners in the field.
- NPC village with a few quests and dialogue system; inventory for crafting materials gathered in the world.

## Milestone 4 – A bigger world
- Second biome (e.g. misty highland lakes or a crystal cave network) with streaming/additive scenes.
- Points of interest: hidden Waystones (fast travel), ruins puzzles, collectibles, weather (rain, wind).
- Map screen and compass.

## Milestone 5 – Polish and ship-readiness
- Accessibility: key rebinding, colour-blind-friendly markers, subtitles, hold/toggle options.
- Performance pass on low-end hardware, LODs, occlusion culling.
- Localisation framework.
- Steam build pipeline; still no microtransactions.

## Later (only once single-player is solid)
- Co-op exploration experiment, reusing the pure `Rules/` code and serialisable creature records with an authoritative host.
