# Beastbound – Architecture

## Big picture

The prototype scene contains a camera, a light and one GameObject with `GameBootstrap`. On Play, the bootstrap:

1. Loads settings (`Saving/SaveSystem`).
2. Builds the creature catalog (`Creatures/CreatureCatalog`) from ScriptableObject assets or built-in defaults.
3. Generates the world (`World/WorldBuilder`) → returns a `WorldContext` (ground height, water, walkability, spawns, nests).
4. Adds the systems: `GameManager`, `AudioManager`, `BattleController`, `CollectionManager`, `CreatureSpawner`, `ObjectiveTracker`.
5. Creates the player (`Player/PlayerController.Create`) and hooks up the camera (`CameraSystem/ThirdPersonCamera`).
6. Builds the UI (`UI/UIManager`).
7. Spawns wild creatures and enters the title screen.

`GameManager` is the hub: it owns the game state (Title → Playing ⇄ Paused, Defeat) and holds references to every system, so systems never search the scene for each other.

## Assemblies

| Assembly | Folder | Notes |
|---|---|---|
| `Beastbound.Runtime` | `Scripts/Runtime` | All game code. References uGUI and (optionally) the Input System via `versionDefines` → `BB_INPUT_SYSTEM`. |
| `Beastbound.Editor` | `Scripts/Editor` | Editor-only menu tools. |
| `Beastbound.Tests.EditMode` | `Tests/EditMode` | NUnit tests, compiled only when the Test Framework is present. |

## Separation of concerns

| Concern | Lives in | Talks to |
|---|---|---|
| Raw input | `Controls/GameInput` (static) | Nobody – others read it |
| Player movement | `Player/PlayerController` | `GameInput`, `WorldContext` |
| Player combat | `Player/PlayerCombat` | `Combat/*`, `PlayerController` |
| Binding (collecting) | `Player/PlayerBinder` | `Creature`, `Rules/CaptureRules`, `CollectionManager` |
| Creature data | `Creatures/CreatureSpecies`, `AttackDefinition` (ScriptableObjects), `CreatureRecord` (plain data) | – |
| Creature decisions | `CreatureAI/CreatureBrain` (state machine) | Motor, Perception, Battle |
| Creature senses | `CreatureAI/CreaturePerception` | Player noise/sneak state |
| Creature movement | `CreatureAI/CreatureMotor` | `WorldContext` |
| Creature visuals | `Creatures/PebbloomModel`, `CreatureAnimator` | – |
| Numbers (damage, capture, stats) | `Rules/*` (pure C#, no UnityEngine) | – |
| Fights in progress | `Combat/BattleController` | UI, music |
| Bestiary | `Collection/CollectionManager` | Spawner, save |
| Persistence | `Saving/SaveSystem` (atomic JSON) | `GameManager` builds `SaveData` |
| Presentation | `UI/*` | Reads state; calls `GameManager` for actions |

## Layers (no Tag Manager setup required)

- **Ignore Raycast (2)** – all characters. Default raycasts (camera collision, line of sight, ground checks) automatically skip them; combat queries target them explicitly with `Layers.CharacterMask`.
- **Water (4)** – the water plane.
- **31** – Bestiary portrait stage (rendered only by the portrait camera).

## Render pipeline

The prototype uses the **Built-in Render Pipeline** so it works with zero setup. Materials are created by cloning the active pipeline's default material (`Core/MaterialFactory`) and setting both Built-in and URP property names, so the same code renders correctly if you later switch to URP. A URP migration (for post-processing, better lighting and Shader Graph water/grass) is planned in the roadmap.

## Replacing prototype assets

- **Creature model:** create a prefab, then in `Creature.BuildModel` instantiate it instead of `PebbloomModel.Build`. Swap `CreatureAnimator` for an Animator-based component that reads the same properties (`Speed`, `Pose`, `PlayHurt`, `PlayLunge`, …).
- **Player model:** replace `PlayerAvatar`; gameplay only calls its `Tick` and `Play*` methods.
- **Terrain/props:** `WorldBuilder` is split into small steps (terrain, water, landmarks, trees, rocks, grass, flora); replace any step with authored content.
- **Audio:** `AudioManager` maps `SfxId` → `AudioClip`; assign recorded clips instead of `ProceduralAudio.Build`.
- **UI:** `UIFactory` centralises fonts and sprites; swap `UIFactory.Font` for a TextMeshPro setup when you want richer typography.

## Adding a new creature species

1. *Beastbound → Create Editable Creature Data* (once), then duplicate `Species_Pebbloom` and its attacks.
2. Give it a unique `speciesId`, stats, palette, senses and attacks.
3. Add a model builder (or prefab) and a new `CreatureModelKind` value.
4. Add nests for it in `WorldBuilder.DefineNests` and assign the asset to the bootstrap's *Species Overrides*.

## Error handling

- The bootstrap catches start-up exceptions and shows a readable on-screen message.
- Saves are written atomically with a `.bak` fallback; corrupted saves fall back to the backup.
- Settings are sanitised on load (NaN, out-of-range values).
- Every static event/cache resets on play-mode start, so the project also works with *Enter Play Mode Options* (domain reload disabled).
