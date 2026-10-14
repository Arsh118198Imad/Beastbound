using UnityEngine;
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Beastbound.Controls
{
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// One place that reads raw input. Gameplay scripts never touch Unity input APIs directly, which keeps
    /// controls easy to rebind later and lets the project run with either input backend:
    ///  * Unity Input System package (recommended; full gamepad support), or
    ///  * the legacy Input Manager (keyboard + mouse; basic gamepad buttons).
    ///
    /// Default bindings
    ///   Move        WASD / Left stick          Look      Mouse / Right stick
    ///   Run         Left Shift / LB (hold)     Jump      Space / A
    ///   Sneak       Left Ctrl or C / R3        Attack    Left mouse / X
    ///   Spark Burst Right mouse / Y            Dodge     Q / B
    ///   Bind        E / RB                     Bestiary  Tab / View(Select)
    ///   Pause       Esc / Start                Help      F1
    /// </summary>
    public static class GameInput
    {
        /// <summary>The device the player used most recently (for button prompts).</summary>
        public static InputDeviceKind LastDevice { get; private set; } = InputDeviceKind.KeyboardMouse;

        /// <summary>When false, gameplay queries return neutral values (menus, cutscenes).</summary>
        public static bool GameplayEnabled = true;

        private static bool _sneakToggled;
        private static int _lastUpdateFrame = -1;
#if !(BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM) && !ENABLE_LEGACY_INPUT_MANAGER
        private static bool _warnedNoBackend;
#endif

        // Mouse pixels -> "look units". Matches the legacy Input Manager's default 0.1 mouse sensitivity.
        private const float MousePixelsToLookUnits = 0.1f;
        private const float StickDeadZone = 0.15f;

        /// <summary>Call once per frame (GameManager does this) to track device switches and toggles.</summary>
        public static void Tick()
        {
            if (_lastUpdateFrame == Time.frameCount) return;
            _lastUpdateFrame = Time.frameCount;

#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var gp = Gamepad.current;
            if (gp != null && (gp.leftStick.ReadValue().sqrMagnitude > 0.1f || gp.rightStick.ReadValue().sqrMagnitude > 0.1f
                               || gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame
                               || gp.buttonWest.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame
                               || gp.startButton.wasPressedThisFrame))
            {
                LastDevice = InputDeviceKind.Gamepad;
            }
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if ((kb != null && kb.anyKey.wasPressedThisFrame) ||
                (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame)))
            {
                LastDevice = InputDeviceKind.KeyboardMouse;
            }
            if (gp != null && gp.rightStickButton.wasPressedThisFrame) _sneakToggled = !_sneakToggled;
            if (kb != null && kb.cKey.wasPressedThisFrame) _sneakToggled = !_sneakToggled;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.anyKeyDown)
            {
                LastDevice = AnyJoystickButtonDown() ? InputDeviceKind.Gamepad : InputDeviceKind.KeyboardMouse;
            }
            if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.JoystickButton9)) _sneakToggled = !_sneakToggled;
#else
            if (!_warnedNoBackend)
            {
                _warnedNoBackend = true;
                Debug.LogError("[Beastbound] No usable input backend. Install the 'Input System' package " +
                               "(Window > Package Manager) or set Project Settings > Player > Active Input Handling to 'Both'.");
            }
#endif
        }

        // ------------------------------------------------------------------ Gameplay axes

        public static Vector2 Move
        {
            get
            {
                if (!GameplayEnabled) return Vector2.zero;
                Vector2 v = Vector2.zero;
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                }
                var gp = Gamepad.current;
                if (gp != null)
                {
                    Vector2 stick = ApplyDeadZone(gp.leftStick.ReadValue());
                    if (stick.sqrMagnitude > v.sqrMagnitude) v = stick;
                }
#elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1f;
                Vector2 stick = ApplyDeadZone(new Vector2(SafeAxis("Horizontal"), SafeAxis("Vertical")));
                if (stick.sqrMagnitude > v.sqrMagnitude) v = stick;
#endif
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Mouse look delta for this frame, already frame-rate independent (pixels based).</summary>
        public static Vector2 LookMouse
        {
            get
            {
                if (!GameplayEnabled) return Vector2.zero;
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                var mouse = Mouse.current;
                return mouse != null ? mouse.delta.ReadValue() * MousePixelsToLookUnits : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return new Vector2(SafeAxis("Mouse X"), SafeAxis("Mouse Y"));
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Right stick in [-1,1]; multiply by a degrees-per-second rate and deltaTime.</summary>
        public static Vector2 LookStick
        {
            get
            {
                if (!GameplayEnabled) return Vector2.zero;
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                var gp = Gamepad.current;
                return gp != null ? ApplyDeadZone(gp.rightStick.ReadValue()) : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>-1, 0 or +1 per frame from the scroll wheel or the D-pad.</summary>
        public static float Zoom
        {
            get
            {
                if (!GameplayEnabled) return 0f;
                float z = 0f;
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    float y = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(y) > 0.01f) z = Mathf.Sign(y);
                }
                var gp = Gamepad.current;
                if (gp != null)
                {
                    if (gp.dpad.up.wasPressedThisFrame) z = 1f;
                    if (gp.dpad.down.wasPressedThisFrame) z = -1f;
                }
#elif ENABLE_LEGACY_INPUT_MANAGER
                float y = Input.mouseScrollDelta.y;
                if (Mathf.Abs(y) > 0.01f) z = Mathf.Sign(y);
#endif
                return z;
            }
        }

        // ------------------------------------------------------------------ Gameplay buttons

        public static bool RunHeld => GameplayEnabled && Held(Btn.Run);
        public static bool SneakActive => GameplayEnabled && (_sneakToggled || Held(Btn.SneakHold));
        public static bool JumpPressed => GameplayEnabled && Pressed(Btn.Jump);
        public static bool JumpHeld => GameplayEnabled && Held(Btn.Jump);
        public static bool AttackPressed => GameplayEnabled && Pressed(Btn.Attack);
        public static bool SparkPressed => GameplayEnabled && Pressed(Btn.Spark);
        public static bool DodgePressed => GameplayEnabled && Pressed(Btn.Dodge);
        public static bool BindPressed => GameplayEnabled && Pressed(Btn.Bind);

        // Menu buttons work regardless of GameplayEnabled.
        public static bool PausePressed => Pressed(Btn.Pause);
        public static bool BestiaryPressed => Pressed(Btn.Bestiary);
        public static bool HelpPressed => Pressed(Btn.Help);
        public static bool CancelPressed => Pressed(Btn.Cancel);

        /// <summary>Clears the sneak toggle (e.g. after respawning).</summary>
        public static void ClearSneakToggle() => _sneakToggled = false;

        /// <summary>Human-readable prompt for an action on the current device, e.g. "[E]" or "(RB)".</summary>
        public static string Prompt(string action)
        {
            bool pad = LastDevice == InputDeviceKind.Gamepad;
            switch (action)
            {
                case "Bind": return pad ? "RB" : "E";
                case "Attack": return pad ? "X" : "LMB";
                case "Spark": return pad ? "Y" : "RMB";
                case "Dodge": return pad ? "B" : "Q";
                case "Sneak": return pad ? "R3" : "Ctrl / C";
                case "Run": return pad ? "LB" : "Shift";
                case "Jump": return pad ? "A" : "Space";
                case "Bestiary": return pad ? "View" : "Tab";
                case "Pause": return pad ? "Start" : "Esc";
                case "Help": return "F1";
                default: return action;
            }
        }

        // ------------------------------------------------------------------ Internals

        private enum Btn { Run, SneakHold, Jump, Attack, Spark, Dodge, Bind, Pause, Bestiary, Help, Cancel }

        private static Vector2 ApplyDeadZone(Vector2 v)
        {
            float m = v.magnitude;
            if (m < StickDeadZone) return Vector2.zero;
            float scaled = Mathf.InverseLerp(StickDeadZone, 1f, Mathf.Min(1f, m));
            return v / m * scaled;
        }

#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private static bool Held(Btn key)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var gp = Gamepad.current;
            switch (key)
            {
                case Btn.Run: return (kb != null && kb.leftShiftKey.isPressed) || (gp != null && gp.leftShoulder.isPressed);
                case Btn.SneakHold: return kb != null && kb.leftCtrlKey.isPressed;
                case Btn.Jump: return (kb != null && kb.spaceKey.isPressed) || (gp != null && gp.buttonSouth.isPressed);
                case Btn.Attack: return (mouse != null && mouse.leftButton.isPressed) || (gp != null && gp.buttonWest.isPressed);
                default: return false;
            }
        }

        private static bool Pressed(Btn key)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var gp = Gamepad.current;
            switch (key)
            {
                case Btn.Jump: return (kb != null && kb.spaceKey.wasPressedThisFrame) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
                case Btn.Attack: return (mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && (gp.buttonWest.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame));
                case Btn.Spark: return (mouse != null && mouse.rightButton.wasPressedThisFrame) || (gp != null && (gp.buttonNorth.wasPressedThisFrame || gp.leftTrigger.wasPressedThisFrame));
                case Btn.Dodge: return (kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
                case Btn.Bind: return (kb != null && kb.eKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame);
                case Btn.Pause: return (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
                case Btn.Bestiary: return (kb != null && kb.tabKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
                case Btn.Help: return kb != null && kb.f1Key.wasPressedThisFrame;
                case Btn.Cancel: return (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
                default: return false;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        // Legacy joystick button numbers follow the common XInput layout on Windows:
        // 0=A 1=B 2=X 3=Y 4=LB 5=RB 6=View 7=Start 8=L3 9=R3
        private static bool Held(Btn key)
        {
            switch (key)
            {
                case Btn.Run: return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4);
                case Btn.SneakHold: return Input.GetKey(KeyCode.LeftControl);
                case Btn.Jump: return Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton0);
                case Btn.Attack: return Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton2);
                default: return false;
            }
        }

        private static bool Pressed(Btn key)
        {
            switch (key)
            {
                case Btn.Jump: return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
                case Btn.Attack: return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.JoystickButton2);
                case Btn.Spark: return Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton3);
                case Btn.Dodge: return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton1);
                case Btn.Bind: return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton5);
                case Btn.Pause: return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7);
                case Btn.Bestiary: return Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton6);
                case Btn.Help: return Input.GetKeyDown(KeyCode.F1);
                case Btn.Cancel: return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
                default: return false;
            }
        }

        private static bool AnyJoystickButtonDown()
        {
            for (int i = 0; i < 10; i++)
            {
                if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) return true;
            }
            return false;
        }

        private static float SafeAxis(string axis)
        {
            try
            {
                return Input.GetAxisRaw(axis);
            }
            catch (System.ArgumentException)
            {
                return 0f; // Axis missing from Project Settings > Input Manager.
            }
        }
#else
        private static bool Held(Btn key) => false;
        private static bool Pressed(Btn key) => false;
#endif
    }
}
