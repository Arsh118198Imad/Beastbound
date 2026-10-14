# Beastbound – Design

## Vision

A colourful, cinematic third-person adventure about exploring a wild fantasy world and **befriending** its creatures. The player is a young *Warden* who binds creatures with a staff and Bind Orbs, fights alongside a partner, and uncovers the old ruins and Waystones scattered across the land.

Tone: warm, curious and gentle. Creatures are never killed – defeated creatures become *exhausted* and can be bound or left to recover.

## Inspiration and originality

Beastbound belongs to the same genre as recent 3D creature-collecting adventures (for example Aniimo, Palworld and Pokémon Legends). It deliberately shares the **genre conventions** – open exploration, sneaking up on creatures, real-time battles, a collection screen – and deliberately does **not** copy any game's characters, creature designs, names, maps, UI, music, signature mechanics or assets. Everything in this repository is original.

Signature ideas that are Beastbound's own:

- **Wardens and Waystones** – the player's identity and the respawn/checkpoint landmarks.
- **Transparent binding** – the capture formula is shown on screen, with *Resonance* (pity) after misses.
- **Personalities that drive behaviour, not just stats** – Bold creatures challenge you, Timid ones bolt, Sleepy ones doze, Playful ones circle you.
- **Pebbloom**, the first creature: a pebble-backed Verdant creature with petal-ears, a living tail-flower that closes at dusk, and an amber brow crystal that glows with its mood.

## Player feedback we designed against

Aniimo's launch (2026) was a commercial success but drew a large volume of negative Steam reviews. Public reporting summarised the main complaints as below. Each one maps to a firm rule for Beastbound.

| Complaint in reviews of the genre's recent hit | Beastbound rule |
|---|---|
| Turns into a gacha-style experience about an hour in; monthly passes, stores and pay-to-skip appear early | **Premium, complete game. No microtransactions, gacha, battle pass, energy/stamina timers or pay-to-skip – ever.** No store code exists in the project. |
| "Hidden gacha" systems; paid capture items that improve creature quality | **Bind Orbs are free and unlimited.** Creature quality (Vigor) is a visible 1–3 star value found by exploring, never bought. |
| Capture success left to chance | **The exact chance and its breakdown is on screen; what you see is what is rolled.** Misses add +12% Resonance. Exhausted creatures are a guaranteed bind. |
| Reconnect loops, login queues, a launcher that downloads again after Steam | **Offline single-player.** No launcher, account or server dependency. |
| Crashes on some CPUs at launch | Conservative, mainstream Unity APIs; scalable quality presets; frame cap; robust error handling (the bootstrap shows a readable message instead of failing silently). Profile on low/mid hardware before each milestone. |
| Kernel-level anti-cheat added after launch; claims of reading files outside the game folder; telemetry | **No anti-cheat, no telemetry, no network calls.** The game reads and writes only its own two JSON files in `Application.persistentDataPath/Saves`. |
| Controller issues for some players | Gamepad is a first-class input (Input System), with on-screen prompts that switch automatically between keyboard and controller glyph text. |
| Cluttered interface, frustrating event tabs | **Minimal HUD**: one objective, health/cooldowns, a context prompt, and an enemy bar only during fights. No event tabs, no notification spam. |
| Controversy over AI-generated marketing visuals | All prototype art and audio is procedurally generated from original code; future art will be made or licensed by the team and credited. |

Sources: Steam user reviews and press coverage of Aniimo's launch, e.g. [games.gg](https://games.gg/news/aniimo-steam-mixed-reviews-gacha/), [Cheat Code Central](https://www.cheatcc.com/articles/aniimo-launches-on-steam-tops-100-000-players-despite-gacha-complaints/), [Lumnix](https://lumnix.fr/en/article/aniimo-lancement-127000-joueurs-microtransactions-critiques), [allthings.how](https://allthings.how/aniimo-s-steam-rating-recovers-to-mostly-positive-amid-ai-controversy/), [AniimoVerse](https://www.aniimoverse.com/blog/aniimo-launch-by-the-numbers), [Inven Global review](https://www.invenglobal.com/articles/26174/aniimo-review-the-joy-of-collecting-is-the-joy-of-exploring-83). Some of these complaints (for example the anti-cheat and file-access claims) are user reports that we could not independently verify; we simply design so that they can never apply to Beastbound.

What reviewers **praised**, and we keep: the joy of exploring to find creatures, sneaking up on sleeping or distracted creatures for an easier capture, real-time battles, and individual creatures differing by personality.

## Core loop (prototype)

1. **Explore** the vale; creatures roam, nap and react to you.
2. **Approach** – sneak (quiet, smaller sight radius) or charge in (loud).
3. **Bind** – throw a Bind Orb when the odds are good, or
4. **Battle** – weaken the creature with combos and Ember Spark, dodge its telegraphed attacks, then bind it (guaranteed once exhausted).
5. **Grow** – your partner follows and fights with you; check them in the Bestiary.

## Combat rules

- Damage = `power × (0.5 + ATK/(ATK+DEF)) × element × (0.92–1.08)` (see `Rules/DamageRules.cs`).
- Elements: Verdant > Tide, Stone · Ember > Verdant, Gale · Tide > Ember, Stone · Gale > Verdant · Stone > Ember, Gale. Advantage ×1.25, disadvantage ×0.8.
- Every enemy attack has a visible wind-up (crouch, ears back, crystal glow, sound). Charges leave the creature briefly vulnerable.
- The player regenerates health out of combat. Defeat costs nothing except walking back from the Waystone.

## Capture rules

`chance = baseRate × (1 + 2 × missingHealth%) × awareness + 0.12 × failedAttempts`, clamped to 5–100%.
Awareness: asleep ×2.0 · unaware ×1.5 · alert ×1.0 · hostile ×0.85 · exhausted = 100%.

## Art direction

Bright, readable, storybook fantasy: saturated greens and golds by day, cool blues at night, soft shadows, atmospheric fog, chunky low-poly shapes with clear silhouettes. Creatures use 3–5 main colours and one glowing accent. UI uses deep navy panels with warm gold and mint accents and generous spacing.

## Multiplayer later

Not in scope until single-player is solid. The code is prepared for it: game rules are pure functions (`Rules/`), creature records are plain serialisable data, and systems communicate through the `GameManager` hub rather than global scene searches – so an authoritative server could reuse the same rules and data.
