# Beastbound

An original third-person creature-bonding adventure built in Unity.
Explore **Sunmeadow Vale**, sneak up on (or battle) wild creatures, bind them with a fair, fully visible capture system, and recruit them as partners.

> **Status: first playable prototype (v0.1).**
> All C# code compiles against Unity reference assemblies with zero errors and zero warnings, and the rules unit tests pass (see [Validation](#validation)).
> **It has not yet been opened or played in the Unity Editor.** Unity is not available in the environment where the code was written. Please follow the [testing checklist](docs/TESTING_CHECKLIST.md) the first time you press Play and report anything odd.

---

## What is in the prototype

| Feature | Status |
|---|---|
| Third-person movement: walk, run, sneak, jump (coyote time + jump buffering), wading in water | Done |
| Orbit camera with mouse/gamepad, sensitivity, invert-Y, FOV, collision, zoom | Done |
| Procedurally generated vale: terrain, lake, stream, forests, rocks, grass, flowers, reeds, ruins, Waystone circle, distant mountains | Done |
| Day/night cycle with sky, fog, ambient light and nocturnal behaviour | Done |
| Original creature **Pebbloom** with procedural model and animation (hop, breathe, ears, tail-flower, sleep, hit reactions) | Done |
| Creature AI: roam, idle, sleep, notice ("?" then "!"), approach, flee, fight, give up, return home | Done |
| Five individual Pebblooms with different personalities (Bold, Timid, Sleepy, Curious, Playful) | Done |
| Binding (collecting) with an on-screen chance breakdown, free unlimited Bind Orbs, pity "Resonance" | Done |
| Real-time battles: 3-hit staff combo, Ember Spark (ranged, element advantage), dodge with i-frames, telegraphed enemy attacks, victory (exhausted) and defeat states | Done |
| Partner creature that follows you and helps in fights | Done |
| Bestiary (collection screen) with live 3D portrait, stats, personality, lore, "Set as Partner" | Done |
| Title screen, pause menu, settings, controls screen, defeat screen, restart | Done |
| Save/load (autosave + manual), settings persistence | Done |
| Guided objectives so new players always know what to do | Done |
| Procedurally synthesised sound effects, ambience (day/night) and music (explore/battle) | Done |
| Graphics quality presets, VSync, frame cap, FPS counter | Done |
| Keyboard + mouse, and gamepad (with the Input System package) | Done |

Everything is generated from code: **no third-party art, audio or paid assets**. All placeholder visuals are easy to swap for real models later (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)).

---

## Play in the browser (web edition)

**Play now: <https://tfl-beastbound.vercel.app>**

The `web/` folder contains a **browser version** of the same prototype, built with [three.js](https://threejs.org) (WebGL). It has the same vale, Pebblooms, stealth, binding, battles, partner, Bestiary, menus and saving (saves live in your browser's local storage). No install needed: open the published site, click **New Game**, then click the game view to capture the mouse.

Run it locally: `cd web && python3 -m http.server 8000`, then open <http://localhost:8000>. (Opening `index.html` directly from disk does not work because browsers block ES modules on `file://`.)

The web edition was tested end-to-end in headless Chromium (title → stealth bind → partner → battle → exhausted bind → Bestiary → defeat/retry → pause/settings → save → continue) with no console errors. Desktop with keyboard & mouse or a gamepad is recommended; touch controls are not implemented yet.

**Visual upgrade, step 0 (web only):** physically based sky with drifting clouds and sky-based ambient lighting, a golden-hour default, reflective water, alpha-cut leaf and needle foliage with wind, a detail-textured terrain, and a cinematic post-processing chain (HDR + MSAA, bloom, sun god rays, sharpening, colour grade, vignette), plus the gold BEASTBOUND title screen. Foliage between the camera and the Warden dissolves so trees never hide the player. Graphics presets: Low turns post-processing off; **Medium is the default** for typical laptops; High/Ultra raise resolution, shadows and grass. The Warden and Pebbloom are still placeholder shapes until the Blender model milestone. Frame rates were only measured in a software renderer here, so check real FPS on your own machine.

---

## 1. Install Unity (one time)

1. Download and install **Unity Hub** from <https://unity.com/download>.
2. Open Unity Hub and sign in (a free *Unity Personal* licence is fine).
3. Go to **Installs → Install Editor** and choose the latest **Unity 6.3 LTS** (version numbers start with `6000.3`).
   - In the module list, tick **Microsoft Visual Studio Community** (Windows) if you do not already have a code editor.
   - Optional: tick **Windows Build Support (IL2CPP)** / **Mac Build Support** if you want to make a standalone build later. Not needed to play in the Editor.
4. Wait for the install to finish.

Why 6.3 LTS? It is Unity's current long-term-support release (supported until late 2027), which is what you want for a project you will grow for a long time. The code also uses only APIs that exist in Unity 6.0 LTS, so `6000.0.x` works too.

## 2. Get the project

**Option A – GitHub Desktop (easiest):** install [GitHub Desktop](https://desktop.github.com/), then *File → Clone repository* → `Arsh118198Imad/Beastbound`.

**Option B – command line:**
```bash
git clone https://github.com/Arsh118198Imad/Beastbound.git
```

## 3. Open it in Unity

1. In Unity Hub click **Add → Add project from disk** and select the `Beastbound` folder (the one containing `Assets` and `ProjectSettings`).
2. Click the project to open it.
   - If the Hub says the editor version `6000.3.3f1` is not installed, choose **your installed 6000.3.x** version and confirm. This is normal; Unity will update the version file.
3. The first import takes a few minutes. Unity creates the `Library`, `Packages` and remaining `ProjectSettings` files automatically.
4. **Recommended – install the Input System package for full gamepad support:**
   *Window → Package Manager → Unity Registry → Input System → Install.*
   When Unity asks to enable the new input backends and restart, click **Yes**.
   (The game also works without it, using keyboard and mouse through the older Input Manager.)

## 4. Create the scene and press Play

1. When the project opens you will see **"Welcome to Beastbound – Create Scene?"**. Click **Create Scene**.
   *(If you missed it: top menu **Beastbound → Create Prototype Scene**.)*
2. This creates `Assets/Beastbound/Scenes/SunmeadowVale.unity` and adds it to the build settings.
3. Press the **▶ Play** button at the top of the Editor. World generation takes a second or two.
4. Click inside the Game view so it captures the mouse, then choose **New Journey**.

Tip: maximise the Game view (*right-click its tab → Maximize* or press `Shift+Space`) for the best experience.

## 5. How to play

**Your goal:** find the wild Pebblooms of Sunmeadow Vale and bind them. The objective panel (top-left) always tells you what to do next.

| Action | Keyboard & mouse | Gamepad* |
|---|---|---|
| Move | W A S D | Left stick |
| Look | Mouse | Right stick |
| Run | Hold Left Shift | Hold LB |
| Jump | Space | A |
| Sneak | Hold Left Ctrl, or press C to toggle | R3 (toggle) |
| Staff Strike (combo) | Left mouse | X / RT |
| Ember Spark (ranged) | Right mouse | Y / LT |
| Dodge | Q | B |
| Bind creature | E | RB |
| Bestiary | Tab | View |
| Pause | Esc | Start |
| Zoom | Mouse wheel | D-pad up/down |
| Controls hint | F1 | – |

\*Full gamepad support requires the Input System package (step 3.4).

**Binding tips**
- Face a creature within a few metres: the bottom of the screen shows the exact bind chance and *why* (asleep ×2, unaware ×1.5, its health, Resonance).
- Sneak up on sleeping or unaware creatures for big bonuses. A **?** above a creature means it is getting suspicious; **!** means it noticed you.
- Or weaken it in battle. A creature at 0 HP becomes **exhausted** and is a **guaranteed** bind.
- Every failed attempt adds **Resonance** (+12% next time). Bind Orbs are unlimited and free.
- Your first bound creature becomes your **partner**: it follows you and helps in fights.

---

## Project layout

```
Assets/Beastbound/
  Scripts/Runtime/        Game code (assembly: Beastbound.Runtime)
    Core/                 Bootstrap, GameManager, events, layers, procedural mesh/material helpers
    Rules/                Pure maths: capture, damage, stats (no Unity dependency, unit-tested)
    Controls/             Input abstraction (Input System or legacy Input Manager)
    Player/               Movement, combat, binding, procedural avatar
    CameraSystem/         Third-person orbit camera
    Creatures/            Species data (ScriptableObjects), records, model, animator, spawner
    CreatureAI/           Brain (state machine), perception (stealth), motor (movement)
    Combat/               Health, projectiles, hit queries, battle tracking
    Collection/           Bestiary bookkeeping and partner hand-off
    World/                Terrain, vegetation, water, day/night, landmarks, nests
    Audio/                Procedural sound synthesis and playback
    Saving/               Save data, atomic JSON save system, settings
    Quests/               Guided objective chain
    UI/                   HUD, menus, Bestiary (built from code with uGUI)
  Scripts/Editor/         "Beastbound" menu: scene creation, data assets, save tools
  Tests/EditMode/         Unity Test Runner tests
docs/                     Design, architecture, testing checklist, roadmap
```

## Useful editor menus

- **Beastbound → Create Prototype Scene** – (re)creates the playable scene.
- **Beastbound → Create Editable Creature Data** – writes Pebbloom and its attacks as ScriptableObject assets in `Assets/Beastbound/Data` so you can tweak stats, colours, senses and attacks in the Inspector without code.
- **Beastbound → Open Save Folder / Delete Save Data** – manage local saves.
- On the `Beastbound` object in the scene: **World Seed** (different layouts) and **Skip Title Screen** (faster testing).

## Running the tests

*Window → General → Test Runner → EditMode → Run All.* (Needs the *Test Framework* package, which Unity includes by default.)

## Validation

What was actually checked before this commit:

- All runtime, editor and test code was compiled with the .NET SDK C# compiler against Unity 2021.3 reference assemblies (plus Unity's Input System 1.11.2 compiled from source) in five configurations: legacy input, Input System, Input System enabled without the package, Editor mode, and the Editor-tools assembly. **Result: 0 errors, 0 warnings.**
- The 15 pure rules tests (capture, damage, stats) were run with NUnit: **15/15 passed.** The save-system tests need Unity's `JsonUtility`, so they were compiled but can only run inside Unity's Test Runner.

What was **not** possible here: opening the project in the Unity Editor, playing, rendering, profiling or making a build. Reference assemblies are older than Unity 6.3, so a small API difference is possible even though only long-stable APIs were used. The [testing checklist](docs/TESTING_CHECKLIST.md) walks through everything to verify on first run.

## Troubleshooting

| Problem | Fix |
|---|---|
| Errors mentioning `UnityEngine.UI` | *Window → Package Manager → Unity Registry → **Unity UI** → Install.* |
| Console says "No usable input backend" | Install the **Input System** package, or set *Edit → Project Settings → Player → Active Input Handling* to **Both**. |
| Mouse does not turn the camera | Click inside the Game view first. Press Esc to free the cursor. |
| Scene is missing | *Beastbound → Create Prototype Scene.* |
| Want a fresh start | *Beastbound → Delete Save Data*, or *Pause → Restart Journey*. |
| Something else | Copy the first red error from *Window → General → Console* and share it. |

## Saving to Git

After the first open Unity will create `.meta` files next to every asset and a few `ProjectSettings` files. **Commit them** – they are part of the project. Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` or builds (already in `.gitignore`).

## Design principles

Beastbound is inspired by the genre of modern 3D creature-collecting adventures, but all names, creatures, characters, world and code are original. It is designed around what players *disliked* in recent games of the genre – see [docs/DESIGN.md](docs/DESIGN.md):

- No microtransactions, gacha, energy timers, battle passes or pay-to-skip. Ever.
- Offline single-player. No launcher, login queue or always-online requirement.
- No anti-cheat drivers, no telemetry; the game only touches its own save folder.
- Transparent capture odds, unlimited free Bind Orbs, guaranteed binds for exhausted creatures.
- Clean, minimal UI and full controller support.
- Scalable graphics so it runs well on modest PCs.

## Next steps

See [docs/ROADMAP.md](docs/ROADMAP.md). Milestone 2 is "make it feel great": rigged models and animation, URP + post-processing, more creatures and a second biome.
