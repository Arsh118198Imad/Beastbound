using System;
using UnityEngine;

namespace Beastbound.Core
{
    public enum NotificationKind
    {
        Info,
        Success,
        Warning,
        Discovery
    }

    /// <summary>
    /// Small static event hub for cross-system messages (UI toasts, quest progress).
    /// Systems that own state (collection, battle) expose their own events; this is only for broadcast-style news.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<string, NotificationKind> Notification;

        /// <summary>Raised whenever the Bestiary screen is opened (used by the tutorial objective).</summary>
        public static event Action BestiaryOpened;

        /// <summary>Raised when the player first gets close enough to see a wild creature.</summary>
        public static event Action<string> WildCreatureSpotted;

        public static void Notify(string message, NotificationKind kind = NotificationKind.Info)
        {
            if (string.IsNullOrEmpty(message)) return;
            Notification?.Invoke(message, kind);
        }

        public static void RaiseBestiaryOpened() => BestiaryOpened?.Invoke();

        public static void RaiseWildCreatureSpotted(string speciesId) => WildCreatureSpotted?.Invoke(speciesId);

        // Static events survive between play sessions when "Enter Play Mode Options" disables domain reload.
        // Clearing them here prevents duplicate handlers and missing-object errors in that setup.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Notification = null;
            BestiaryOpened = null;
            WildCreatureSpotted = null;
        }
    }

    /// <summary>
    /// Layer usage. We only use Unity's built-in layers so no Tag Manager setup is needed.
    /// Characters live on "Ignore Raycast" so default raycasts (camera collision, ground checks) skip them.
    /// </summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int Characters = 2;   // Built-in "Ignore Raycast"
        public const int Water = 4;        // Built-in "Water"
        public const int Preview = 31;     // Unnamed layer used only by the Bestiary portrait camera

        public const int CharacterMask = 1 << Characters;
        public const int PreviewMask = 1 << Preview;

        /// <summary>World geometry: everything except characters, water and the preview layer.</summary>
        public const int EnvironmentMask = ~(CharacterMask | (1 << Water) | PreviewMask | (1 << 1) | (1 << 5));

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
