using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SpinTheScrap.Player
{
    /// <summary>
    /// 入力の読み取りを 1 か所にまとめる。Input System（新）と Input Manager（旧）のどちらの設定でも動く。
    /// フェーズ 1 でネットワーク化するときは、ここで読んだ値をそのままホストへ送る入力コマンドにする想定。
    /// </summary>
    public static class GameInput
    {
        /// <summary>Input System の Mouse.delta（ピクセル）を旧 Input Manager の "Mouse X" と同程度に揃える係数。</summary>
        const float MouseDeltaScale = 0.1f;

        public static Vector2 Move
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
                float y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1);
#else
                return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1);
#endif
            }
        }

        public static Vector2 Look
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m == null ? Vector2.zero : m.delta.ReadValue() * MouseDeltaScale;
#else
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        public static bool JumpPressed => KeyDown(Key.Space);
        public static bool RunHeld => KeyHeld(Key.LeftShift);
        public static bool InteractPressed => KeyDown(Key.E);
        public static bool ReviveHeld => KeyHeld(Key.R);
        public static bool DragPressed => KeyDown(Key.F);
        public static bool AttackHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool ThrowPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool ThrowHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
        public static bool DropPressed => KeyDown(Key.G);
        public static bool FlashlightPressed => KeyDown(Key.L);
        public static bool BandagePressed => KeyDown(Key.H);
        public static bool FirecrackerPressed => KeyDown(Key.Q);
        public static bool BaseMenuPressed => KeyDown(Key.Tab);
        public static bool EscapePressed => KeyDown(Key.Escape);
        public static bool AnyMouseButtonPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        static readonly Key[] SlotKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        public static int SlotSelectPressed()
        {
            for (int i = 0; i < SlotKeys.Length; i++)
                if (KeyDown(SlotKeys[i])) return i;
            return -1;
        }

        static bool KeyDown(Key key)
        {
            var k = Keyboard.current;
            return k != null && k[key].wasPressedThisFrame;
        }

        static bool KeyHeld(Key key)
        {
            var k = Keyboard.current;
            return k != null && k[key].isPressed;
        }
#else
        public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
        public static bool RunHeld => Input.GetKey(KeyCode.LeftShift);
        public static bool InteractPressed => Input.GetKeyDown(KeyCode.E);
        public static bool ReviveHeld => Input.GetKey(KeyCode.R);
        public static bool DragPressed => Input.GetKeyDown(KeyCode.F);
        public static bool AttackHeld => Input.GetMouseButton(0);
        public static bool ThrowPressed => Input.GetMouseButtonDown(1);
        public static bool ThrowHeld => Input.GetMouseButton(1);
        public static bool DropPressed => Input.GetKeyDown(KeyCode.G);
        public static bool FlashlightPressed => Input.GetKeyDown(KeyCode.L);
        public static bool BandagePressed => Input.GetKeyDown(KeyCode.H);
        public static bool FirecrackerPressed => Input.GetKeyDown(KeyCode.Q);
        public static bool BaseMenuPressed => Input.GetKeyDown(KeyCode.Tab);
        public static bool EscapePressed => Input.GetKeyDown(KeyCode.Escape);
        public static bool AnyMouseButtonPressed => Input.GetMouseButtonDown(0);

        static readonly KeyCode[] SlotKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

        public static int SlotSelectPressed()
        {
            for (int i = 0; i < SlotKeys.Length; i++)
                if (Input.GetKeyDown(SlotKeys[i])) return i;
            return -1;
        }
#endif
    }
}
