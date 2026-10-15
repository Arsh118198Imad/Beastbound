using System;
using Beastbound.Controls;
using Beastbound.Core;
using Beastbound.Creatures;
using UnityEngine;

namespace Beastbound.Quests
{
    /// <summary>
    /// The prototype's guided objective chain, so a new player always knows what to do next:
    ///   0. Find a wild creature   1. Bind one   2. Open the Bestiary   3. Bind the rest   4. Done
    /// Text adapts to keyboard or gamepad prompts.
    /// </summary>
    public class ObjectiveTracker : MonoBehaviour
    {
        public const int StepFind = 0;
        public const int StepBind = 1;
        public const int StepBestiary = 2;
        public const int StepBindAll = 3;
        public const int StepComplete = 4;

        public int Step { get; private set; }
        public bool BestiaryOpened { get; private set; }
        public event Action Changed;

        private void OnEnable()
        {
            GameEvents.WildCreatureSpotted += OnSpotted;
            GameEvents.BestiaryOpened += OnBestiaryOpened;
        }

        private void OnDisable()
        {
            GameEvents.WildCreatureSpotted -= OnSpotted;
            GameEvents.BestiaryOpened -= OnBestiaryOpened;
        }

        public void ResetProgress()
        {
            Step = StepFind;
            BestiaryOpened = false;
            Changed?.Invoke();
        }

        public void Load(int step, bool bestiaryOpened)
        {
            Step = Mathf.Clamp(step, StepFind, StepComplete);
            BestiaryOpened = bestiaryOpened;
            Refresh();
        }

        private void Update()
        {
            // Also count "being near" a creature as finding it, in case it never noticed you.
            if (Step != StepFind) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.Player == null || gm.Spawner == null || gm.State != GameState.Playing) return;
            foreach (var c in gm.Spawner.WildCreatures)
            {
                if (c == null) continue;
                if ((c.transform.position - gm.Player.transform.position).sqrMagnitude < 14f * 14f)
                {
                    Advance(StepBind);
                    GameEvents.Notify($"A wild {c.Species.displayName}! Sneak closer, or weaken it, then bind it.", NotificationKind.Discovery);
                    AudioPing();
                    return;
                }
            }
        }

        private static void AudioPing() => Audio.AudioManager.PlayUi(Audio.SfxId.Discovery, 0.6f);

        private bool _guardianSeen;

        private void OnSpotted(string speciesId)
        {
            if (speciesId == Creatures.DefaultContent.ThalorixId && !_guardianSeen)
            {
                _guardianSeen = true;
                GameEvents.Notify("A Thalorix, guardian of the western woods. It is strong and hard to bind — weaken it first.",
                    NotificationKind.Discovery);
            }
            if (Step == StepFind) Advance(StepBind);
        }

        private void OnBestiaryOpened()
        {
            BestiaryOpened = true;
            if (Step == StepBestiary) Refresh();
        }

        /// <summary>Re-evaluates progress from the collection (call after binds and loads).</summary>
        public void Refresh()
        {
            var gm = GameManager.Instance;
            int bound = gm != null && gm.Collection != null ? gm.Collection.Count : 0;
            int total = gm != null && gm.World != null ? gm.World.Nests.Count : 0;
            int remaining = RemainingWild();

            int step = Step;
            if (bound > 0 && step < StepBestiary) step = StepBestiary;
            if (step == StepBestiary && BestiaryOpened) step = StepBindAll;
            if (step == StepBindAll && total > 0 && remaining == 0) step = StepComplete;
            if (step != Step)
            {
                Step = step;
                if (Step == StepComplete)
                {
                    GameEvents.Notify("Every creature in Sunmeadow Vale is bound. Prototype complete — thank you for playing!", NotificationKind.Discovery);
                }
            }
            Changed?.Invoke();
        }

        private int RemainingWild()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.World == null || gm.Collection == null) return 0;
            int remaining = 0;
            foreach (var nest in gm.World.Nests)
            {
                if (!gm.Collection.IsNestCaptured(nest.Id)) remaining++;
            }
            return remaining;
        }

        private void Advance(int step)
        {
            if (step <= Step) return;
            Step = step;
            Refresh();
        }

        public string Title
        {
            get
            {
                switch (Step)
                {
                    case StepFind: return "Explore Sunmeadow Vale";
                    case StepBind: return "Bind a wild creature";
                    case StepBestiary: return "Meet your partner";
                    case StepBindAll: return "Befriend the vale";
                    default: return "Sunmeadow Vale complete";
                }
            }
        }

        public string Description
        {
            get
            {
                var gm = GameManager.Instance;
                switch (Step)
                {
                    case StepFind:
                        return "Wild creatures live around the meadow, the lake, the old ruins and the western woods. Find one.";
                    case StepBind:
                        return $"Sneak [{GameInput.Prompt("Sneak")}] to stay unnoticed, or weaken it with " +
                               $"[{GameInput.Prompt("Attack")}] / [{GameInput.Prompt("Spark")}]. Then press " +
                               $"[{GameInput.Prompt("Bind")}] to throw a Bind Orb. The chance is shown on screen.";
                    case StepBestiary:
                        return $"Open your Bestiary [{GameInput.Prompt("Bestiary")}] to see your new partner's stats.";
                    case StepBindAll:
                    {
                        int total = gm != null && gm.World != null ? gm.World.Nests.Count : 0;
                        int bound = total - RemainingWild();
                        return $"Bind every creature in the vale ({bound}/{total}). Each has its own personality.";
                    }
                    default:
                        return "You can keep exploring. More regions and creatures arrive in future milestones.";
                }
            }
        }
    }
}
