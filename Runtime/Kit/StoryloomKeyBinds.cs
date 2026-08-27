// Storyloom Unity Kit — input (Unity Input System package).
// Bindings are plain fields you edit in the inspector; the asset builds InputActions from them at runtime, so there is no
// .inputactions file to maintain. Keyboard + mouse + gamepad out of the box.
// Stardew-style defaults: WASD / arrows / left stick move, Shift / stick-click run, E or Space / south button interact and
// advance, Esc / east button cancel, Tab / north button inventory, hold M / left shoulder for the story map.
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Storyloom
{
#if !ENABLE_INPUT_SYSTEM
    // Input System package not active (Project Settings ▸ Player ▸ Active Input Handling). Same enum names as UnityEngine.InputSystem.Key for the ones we use,
    // so the asset keeps its values if you switch later; the legacy Input Manager is used underneath.
    public enum Key { None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu, Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete, CapsLock, NumLock, PrintScreen, ScrollLock, Pause, NumpadEnter, NumpadDivide, NumpadMultiply, NumpadPlus, NumpadMinus, NumpadPeriod, NumpadEquals, Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }
#endif
    [CreateAssetMenu(menuName = "Storyloom/Key Binds", fileName = "StoryloomKeyBinds")]
    public class StoryloomKeyBinds : ScriptableObject
    {
        [Header("Movement (keyboard)")]
        public Key up = Key.W, down = Key.S, left = Key.A, right = Key.D;
        public Key altUp = Key.UpArrow, altDown = Key.DownArrow, altLeft = Key.LeftArrow, altRight = Key.RightArrow;
        public Key run = Key.LeftShift;
        [Header("Actions (keyboard)")]
        public Key interact = Key.E;        // talk / pick up / examine
        public Key interactAlt = Key.Space;
        public Key advance = Key.Space;     // next dialogue line (mouse left button and the interact keys also advance)
        public Key cancel = Key.Escape;
        public Key inventory = Key.Tab;
        public Key inventoryAlt = Key.I;    // second inventory key (Tab is swallowed by some editor layouts / browsers)
        public Key journal = Key.J;         // story journal (reserved)
        public Key map = Key.M;             // hold: story map overlay
        [Header("Gamepad")]
        public bool gamepad = true;         // left stick / d-pad move, south = interact / advance, east = cancel, north = inventory, left shoulder (hold) = map, stick click = run
        [Header("Feel")]
        public float walkSpeed = 3.5f, runSpeed = 5.5f;
        public float interactRadius = 1.1f;
        [Header("Look (third / first person)")]
        public float mouseSensitivity = 0.12f;     // degrees per pixel of mouse movement
        public float stickSensitivity = 150f;      // degrees per second at full deflection
        public bool invertY = false;

        // Assets made with v0.2 stored UnityEngine.KeyCode ints in these fields. Key and KeyCode share names but not values
        // (KeyCode.W = 119 reads back as Key.F20), so old assets are converted by name once and stamped with this version.
        [SerializeField, HideInInspector] int bindsVersion = 0;
        const int CurrentBindsVersion = 2;
        void OnEnable() { MigrateIfNeeded(); }
        void OnValidate() { MigrateIfNeeded(); }
        void MigrateIfNeeded()
        {
            if (bindsVersion >= CurrentBindsVersion) return;
            Key Conv(Key k) { var name = Enum.GetName(typeof(KeyCode), (int)k); if (name == null) return k; if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5); else if (name.StartsWith("Keypad")) name = "Numpad" + name.Substring(6); else if (name == "LeftControl") name = "LeftCtrl"; else if (name == "RightControl") name = "RightCtrl"; else if (name == "Return") name = "Enter"; return Enum.TryParse<Key>(name, out var r) ? r : k; }
            bool looksLegacy = (int)up > 60 || (int)interact > 60 || (int)inventory == 9;   // ascii letters / Tab as KeyCode
            if (looksLegacy) { inventoryAlt = Key.I; up = Conv(up); down = Conv(down); left = Conv(left); right = Conv(right); altUp = Conv(altUp); altDown = Conv(altDown); altLeft = Conv(altLeft); altRight = Conv(altRight); run = Conv(run); interact = Conv(interact); interactAlt = Conv(interactAlt); advance = Conv(advance); cancel = Conv(cancel); inventory = Conv(inventory); journal = Conv(journal); map = Conv(map); Debug.Log("Storyloom: converted key binds asset from KeyCode to Input System keys."); }
            bindsVersion = CurrentBindsVersion;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        // ---- actions (built lazily; enabled while the asset is in use)
        [NonSerialized] InputAction _move, _run, _interact, _advance, _cancel, _inventory, _journal, _map, _navUp, _navDown, _lookMouse, _lookStick;
        [NonSerialized] bool _built;

        static string K(Key k) { var n = k.ToString(); if (n.StartsWith("Digit")) n = n.Substring(5); else if (n.StartsWith("Numpad")) n = "numpad" + n.Substring(6); else n = char.ToLowerInvariant(n[0]) + n.Substring(1); return "<Keyboard>/" + n; }   // Key.LeftShift → <Keyboard>/leftShift, Key.Digit1 → <Keyboard>/1

        void Build()
        {
            if (_built) return; _built = true;
            Debug.Log("Storyloom: input backend = Input System (keyboard + mouse" + (gamepad ? " + gamepad bindings" : "") + ")");
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", K(up)).With("Down", K(down)).With("Left", K(left)).With("Right", K(right));
            _move.AddCompositeBinding("2DVector").With("Up", K(altUp)).With("Down", K(altDown)).With("Left", K(altLeft)).With("Right", K(altRight));
            if (gamepad) { _move.AddBinding("<Gamepad>/leftStick"); _move.AddBinding("<Gamepad>/dpad"); }
            _run = new InputAction("Run", InputActionType.Button, K(run)); if (gamepad) _run.AddBinding("<Gamepad>/leftStickPress");
            _interact = new InputAction("Interact", InputActionType.Button, K(interact)); _interact.AddBinding(K(interactAlt)); if (gamepad) _interact.AddBinding("<Gamepad>/buttonSouth");
            _advance = new InputAction("Advance", InputActionType.Button, K(advance)); _advance.AddBinding(K(interact)); _advance.AddBinding("<Mouse>/leftButton"); if (gamepad) _advance.AddBinding("<Gamepad>/buttonSouth");
            _cancel = new InputAction("Cancel", InputActionType.Button, K(cancel)); if (gamepad) _cancel.AddBinding("<Gamepad>/buttonEast");
            _inventory = new InputAction("Inventory", InputActionType.Button, K(inventory)); _inventory.AddBinding(K(inventoryAlt)); if (gamepad) _inventory.AddBinding("<Gamepad>/buttonNorth");
            _journal = new InputAction("Journal", InputActionType.Button, K(journal)); if (gamepad) _journal.AddBinding("<Gamepad>/buttonWest");
            _map = new InputAction("Map", InputActionType.Button, K(map)); if (gamepad) _map.AddBinding("<Gamepad>/leftShoulder");
            _navUp = new InputAction("NavUp", InputActionType.Button, K(up)); _navUp.AddBinding(K(altUp)); if (gamepad) { _navUp.AddBinding("<Gamepad>/dpad/up"); _navUp.AddBinding("<Gamepad>/leftStick/up"); }
            _navDown = new InputAction("NavDown", InputActionType.Button, K(down)); _navDown.AddBinding(K(altDown)); if (gamepad) { _navDown.AddBinding("<Gamepad>/dpad/down"); _navDown.AddBinding("<Gamepad>/leftStick/down"); }
            _lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("LookStick", InputActionType.Value); if (gamepad) _lookStick.AddBinding("<Gamepad>/rightStick");
            foreach (var a in All()) a.Enable();
        }
        InputAction[] All() => new[] { _move, _run, _interact, _advance, _cancel, _inventory, _journal, _map, _navUp, _navDown, _lookMouse, _lookStick };
        /// <summary>Call after editing keys at runtime to rebuild the actions.</summary>
        public void Rebuild() { Teardown(); Build(); }
        void Teardown() { if (!_built) return; foreach (var a in All()) { a.Disable(); a.Dispose(); } _built = false; }
        void OnDisable() { Teardown(); }
        void OnDestroy() { Teardown(); }

        // ---- queries (same names as before, so the rest of the kit is unchanged)
        public Vector2 MoveAxis() { Build(); var v = _move.ReadValue<Vector2>(); return v.sqrMagnitude > 1 ? v.normalized : v; }
        public bool Running() { Build(); return _run.IsPressed(); }
        public bool InteractDown() { Build(); return _interact.WasPressedThisFrame(); }
        public bool AdvanceDown() { Build(); return _advance.WasPressedThisFrame(); }
        public bool CancelDown() { Build(); return _cancel.WasPressedThisFrame(); }
        public bool InventoryDown() { Build(); return _inventory.WasPressedThisFrame(); }
        public bool JournalDown() { Build(); return _journal.WasPressedThisFrame(); }
        public bool MapHeld() { Build(); return _map.IsPressed(); }
        public bool NavUpDown() { Build(); return _navUp.WasPressedThisFrame(); }
        public bool NavDownDown() { Build(); return _navDown.WasPressedThisFrame(); }
        /// <summary>Look delta in degrees for this frame (mouse + right stick). x = yaw, y = pitch (already inverted if invertY).</summary>
        public Vector2 LookAxis() { Build(); var m = _lookMouse.ReadValue<Vector2>() * mouseSensitivity; var st = _lookStick.ReadValue<Vector2>() * stickSensitivity * Time.deltaTime; var v = m + st; if (invertY) v.y = -v.y; return v; }

#else
        // ---- legacy Input Manager fallback (Active Input Handling = Input Manager (Old)). Gamepad not supported on this path.
        static KeyCode KC(Key k) { var n = k.ToString(); if (n.StartsWith("Digit")) n = "Alpha" + n.Substring(5); if (n.StartsWith("Numpad")) n = "Keypad" + n.Substring(6); if (n == "LeftCtrl") n = "LeftControl"; if (n == "RightCtrl") n = "RightControl"; if (n == "Enter") n = "Return"; return Enum.TryParse<KeyCode>(n, out var kc) ? kc : KeyCode.None; }
        static bool Down(Key k) => Input.GetKeyDown(KC(k)); static bool Held(Key k) => Input.GetKey(KC(k));
        bool _warned; void Warn() { if (_warned) return; _warned = true; Debug.LogWarning("Storyloom: using the legacy Input Manager. For the Input System (gamepad, rebinding) set Project Settings ▸ Player ▸ Active Input Handling to 'Input System Package' or 'Both'."); }
        public void Rebuild() { }
        public Vector2 MoveAxis() { Warn(); float x = 0, y = 0; if (Held(left) || Held(altLeft)) x -= 1; if (Held(right) || Held(altRight)) x += 1; if (Held(down) || Held(altDown)) y -= 1; if (Held(up) || Held(altUp)) y += 1; var v = new Vector2(x, y); return v.sqrMagnitude > 1 ? v.normalized : v; }
        public bool Running() => Held(run);
        public bool InteractDown() => Down(interact) || Down(interactAlt);
        public bool AdvanceDown() => Down(advance) || Down(interact) || Input.GetMouseButtonDown(0);
        public bool CancelDown() => Down(cancel);
        public bool InventoryDown() => Down(inventory) || Down(inventoryAlt);
        public bool JournalDown() => Down(journal);
        public bool MapHeld() => Held(map);
        public bool NavUpDown() => Down(up) || Down(altUp);
        public bool NavDownDown() => Down(down) || Down(altDown);
        /// <summary>Look delta in degrees for this frame (mouse only on the legacy path).</summary>
        public Vector2 LookAxis() { var v = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * (mouseSensitivity * 10f); if (invertY) v.y = -v.y; return v; }
#endif
        /// <summary>Which input path this build uses.</summary>
        public static string Backend =>
#if ENABLE_INPUT_SYSTEM
            "Input System";
#else
            "legacy Input Manager";
#endif
        /// <summary>Human-readable hint for on-screen help.</summary>
        static string N(Key k) { var s = k.ToString(); return s.StartsWith("Digit") ? s.Substring(5) : s == "Space" ? "Space" : s.Length == 1 ? s : System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2"); }
        bool GamepadPresent()
        {
#if ENABLE_INPUT_SYSTEM
            return gamepad && Gamepad.current != null;
#else
            return false;
#endif
        }
        public string HelpLine() => HelpLine(GameStyle.TopDown);
        public string HelpLine(GameStyle style) => $"{N(up)}{N(left)}{N(down)}{N(right)} / arrows move · {N(run)} run{(style != GameStyle.TopDown ? " · mouse / right stick look" : "")} · {N(interact)} / {N(interactAlt)} talk, pick up, examine · {N(inventory)} / {N(inventoryAlt)} inventory · hold {N(map)} story map{(style != GameStyle.TopDown ? " · " + N(cancel) + " frees the mouse" : "")}{(GamepadPresent() ? " · gamepad connected" : "")}";

        public static StoryloomKeyBinds Default() { var k = CreateInstance<StoryloomKeyBinds>(); k.name = "Default key binds"; return k; }
    }
}
