# First-run testing checklist

The code compiles cleanly and the rules tests pass outside Unity, but the game has **not yet been played in the Unity Editor**. Work through this list the first time you press Play. Tick items off, and copy the first red Console error for anything that fails.

Before you start: *Window → General → Console*, and enable **Error Pause** so play stops on the first error.

## A. Project opens
- [ ] Project imports in Unity 6.3 LTS with **no red errors** in the Console.
- [ ] The *Beastbound* menu appears in the top menu bar.
- [ ] The "Welcome to Beastbound" dialog appears (or *Beastbound → Create Prototype Scene* works).
- [ ] `Assets/Beastbound/Scenes/SunmeadowVale.unity` exists and is listed in *File → Build Profiles / Build Settings*.
- [ ] *Window → General → Test Runner → EditMode → Run All*: all tests green.

## B. Title screen
- [ ] Press Play: within a few seconds the vale appears behind the **BEASTBOUND** title, the camera slowly orbits.
- [ ] Console shows `[Beastbound] World generated in … ms`. (Note the number; under ~3000 ms is fine.)
- [ ] Buttons respond to mouse hover/click and to keyboard arrows/Enter (and gamepad if installed).
- [ ] Settings and Controls open and close (Esc / B goes back).
- [ ] "Continue Journey" is hidden on the very first run.

## C. Movement and camera
- [ ] **New Journey** starts; the Warden stands near the glowing Waystone, facing the meadow and lake.
- [ ] WASD moves relative to the camera; Shift runs; Space jumps (holding jumps higher); Ctrl/C sneaks (character crouches).
- [ ] The camera follows smoothly, never goes through the ground or water surface, and pulls in when a tree/rock is behind you.
- [ ] Mouse wheel zooms. Settings → Mouse sensitivity / Invert Y / FOV change the camera immediately.
- [ ] Walking into the lake slows you (wading) and plays splashy footsteps.
- [ ] Walking to the edge of the valley stops you with "The mountains are too steep to cross — for now."

## D. World
- [ ] Terrain has sand near water, green meadows, darker forest floor, dirt paths, rocky slopes and snowy peaks.
- [ ] Trees, rocks, grass, flowers, reeds, mushrooms, the stone circle and the old ruins with the arch are visible.
- [ ] Distant mountains form a horizon; fog adds depth.
- [ ] Settings → Day length 2 minutes: watch sunset → blue night with fireflies → sunrise. Music/ambience change from birds to crickets.
- [ ] Settings → Graphics quality Low/Ultra visibly changes grass density and shadows.

## E. Creatures and stealth
- [ ] Five Pebblooms exist: meadow (Bold), lakeshore (Timid), ruins (Sleepy, starts asleep with "Zz"), grove (Curious), knoll (Playful).
- [ ] They wander, idle, chirp, and occasionally nap.
- [ ] Approaching normally makes a **?** appear and grow, then **!**.
- [ ] Sneaking lets you get much closer before they notice; you can tiptoe right up to a sleeping one.
- [ ] Timid flees; Curious/Sleepy approach and watch you; Playful circles you; Bold starts a fight (**!!**, enemy health bar, battle music).
- [ ] The objective changes to "Bind a Pebbloom" once you find one.

## F. Binding
- [ ] Facing a nearby creature shows "[E] Bind Pebbloom NN%" with a breakdown line.
- [ ] Sleeping creature shows "Asleep x2.0"; the percentage is higher than for an alert one.
- [ ] Pressing E throws a glowing orb, a ring holds the creature, three chimes, then success or "broke free".
- [ ] After a miss, the next chance is higher (Resonance shown in the breakdown).
- [ ] Success: toast "Pebbloom bound…", the creature becomes your partner and follows you.
- [ ] Objective changes to "Meet your partner"; pressing Tab opens the Bestiary with a spinning 3D portrait and stats; objective advances.

## G. Battle
- [ ] Left-click performs a 3-hit staff combo with hit-stop, camera shake and damage numbers.
- [ ] Right-click fires Ember Spark (homing); damage shows "Effective!" against Pebbloom.
- [ ] Q dodges with a roll; enemy attacks pass through you during the roll.
- [ ] Enemy telegraphs (crouch + glow + sound) before Pebble Toss / Bramble Rush / Petal Swipe.
- [ ] Your partner joins the fight.
- [ ] Bringing a creature to 0 HP shows "Victory!" and the creature lies exhausted with a "100%" marker; binding it always succeeds.
- [ ] Running ~30 m away makes it lose interest and return home.
- [ ] Let yourself be defeated: slow-motion, "You were overwhelmed" screen, **Try Again** respawns you at the Waystone with full health; creatures calm down.

## H. Menus, saving and settings
- [ ] Esc opens pause; Esc closes it (one press = one action).
- [ ] Pause → Save Journey shows "Journey saved." Quit play mode, press Play again: **Continue Journey** restores position, partner, Bestiary and objective.
- [ ] Pause → Restart Journey (confirm) resets progress.
- [ ] Settings persist after restarting play mode.
- [ ] Beastbound → Open Save Folder shows `journey.json` and `settings.json` and nothing else.

## I. Performance (Stats window: Game view → Stats)
- [ ] Note FPS and batches on High in the meadow and looking across the lake.
- [ ] Low quality is noticeably faster if your PC struggles.
- [ ] No growing memory or stutter after 10 minutes of play (Window → Analysis → Profiler).

## J. Optional build test
- [ ] *File → Build Profiles → Windows/Mac → Build*. Run the build; check the title screen, a bind, a save/continue.
- [ ] If anything looks untextured or missing glow in the build only, note it (runtime material keywords can be stripped from builds; see Roadmap).

Report results as: section letter + item + what happened + the Console error text.
