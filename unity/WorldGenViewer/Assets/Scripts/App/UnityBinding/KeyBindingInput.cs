#nullable enable
using System;
using UnityEngine;
using WorldGen.App.Controls;

namespace WorldGen.App.UnityBinding
{
    /// <summary>
    /// <see cref="KeyChord"/> → legacy Input Manager (a projekt „Both” input-kezelésű).
    /// A módosítóknak pontosan egyezniük kell: a Shift+F12 nem váltja ki az F12-t, és fordítva.
    /// </summary>
    public static class KeyBindingInput
    {
        public static bool TryGetKeyCode(KeyChord chord, out KeyCode code)
        {
            code = KeyCode.None;
            return !chord.IsEmpty && Enum.TryParse(chord.Key, true, out code) && Enum.IsDefined(typeof(KeyCode), code) && code != KeyCode.None;
        }

        public static bool WasPressedThisFrame(KeyChord chord)
        {
            if (!TryGetKeyCode(chord, out var code) || !Input.GetKeyDown(code)) return false;
            return CurrentModifiers() == chord.Modifiers;
        }

        public static KeyModifiers CurrentModifiers()
        {
            var m = KeyModifiers.None;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) m |= KeyModifiers.Control;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) m |= KeyModifiers.Shift;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) m |= KeyModifiers.Alt;
            return m;
        }
    }
}
