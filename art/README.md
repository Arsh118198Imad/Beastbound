# Beastbound art pipeline (Blender, scripted)

Every model here is **original** and is built from scratch by a Python script — no downloaded or
third-party assets. The scripts are re-runnable, so changes are made by editing code, not by hand.

| Script | Builds | Output |
|---|---|---|
| `blender/thalorix.py` | Thalorix, the forest guardian (maned wolf-lion, crystal-tipped horns, glowing veins) | `web/assets/models/thalorix.glb` |
| `blender/pebbloom.py` | Pebbloom, the starter creature (mossy stone plates, petal ears, tail flower, amber crystal) | `web/assets/models/pebbloom.glb` |
| `blender/warden.py` | The Warden, the player (straw hat, coat and cape, scarf, satchel, staff) | `web/assets/models/warden.glb` |
| `blender/rocks.py` | Environment kit: six fractured boulders and two chipped cut-stone blocks (intact and broken), with moss, lichen and crevice shading in vertex colour | `web/assets/models/env.glb` |
| `blender/common.py` | Shared helpers: skin-modifier bodies, fur/hair locks, rocks, texture baking, rigging, glTF export, preview renders | — |

## How each model is made
1. **Body:** a joint graph turned into a smooth mesh with Blender's Skin + Subdivision modifiers, then shaped in code.
2. **Details:** fur and mane locks, horns, ears, cloth layers (coat, cape, sleeves), gear, eyes, all generated in code.
3. **Materials:** procedural fur, cloth and vein shaders are **baked** (Cycles) into colour, normal and emission textures; small parts use vertex colours.
4. **Rig:** an armature with named bones (`hips`, `spine`, `chest`, `neck`, `head`, `earL`, `tail1`…`tail3`, `legFL1`…, or `torso`, `armR`, `forearmR`, `legL`, `shinL`… for the Warden). The body is skinned with automatic weights; clothing and fur copy weights from the body.
5. **Export:** one skinned `.glb` per model (JPEG textures embedded).

Animation is **procedural** in the game code (`web/js/creatures.js` `QuadrupedAnimator`, `web/js/player.js` `SkinnedAvatar`), driven by the bone names above. No keyframed clips are exported yet.

## Rebuild a model
Requires Blender 4.0+ (tested with 4.0.2). Run these from the repository root:

```sh
blender -b -P art/blender/thalorix.py             # model, bake, export
blender -b -P art/blender/thalorix.py -- render   # also save a preview to art/renders/
```

The `.glb` files are also usable in Unity through the glTFast package (not yet set up in the Unity project).

## About the renders
Files in `art/renders/` are **offline Blender (Cycles) preview renders** of the models on a plain backdrop. They are not in-game screenshots.
