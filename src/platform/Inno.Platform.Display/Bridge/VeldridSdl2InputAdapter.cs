using System;

using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Platform.Display.Bridge;

internal static class VeldridSdl2InputAdapter
{
    private static Vector2? m_lastMousePos;
    
    public static void AdaptInputEvents(Veldrid.InputSnapshot snapshot, Action<Event> onEvent)
    {
        // Key
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            var key = ConvertKey(keyEvent.Key);
            if (key == KeyCode.Unknown) continue;

            var modifiers = ConvertKeyModifiers(keyEvent.Modifiers);
            if (keyEvent.Down)
                onEvent(new KeyPressedEvent(key, modifiers, keyEvent.Repeat));
            else
                onEvent(new KeyReleasedEvent(key));
        }

        // MouseButton
        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            var btn = ConvertMouseButton(mouseEvent.MouseButton);
            if (mouseEvent.Down)
                onEvent(new MouseButtonPressedEvent(btn));
            else
                onEvent(new MouseButtonReleasedEvent(btn));
        }

        // MouseMove
        if (m_lastMousePos != (Vector2)snapshot.MousePosition)
        {
            m_lastMousePos = (Vector2)snapshot.MousePosition;
            onEvent(new MouseMovedEvent(snapshot.MousePosition.X, snapshot.MousePosition.Y));
        }

        // MouseScroll
        if (snapshot.WheelDelta != 0)
            onEvent(new MouseScrolledEvent(0, snapshot.WheelDelta));
    }

    private static MouseButton ConvertMouseButton(Veldrid.MouseButton veldridButton)
    {
        return veldridButton switch
        {
            Veldrid.MouseButton.Left => MouseButton.Left,
            Veldrid.MouseButton.Right => MouseButton.Right,
            Veldrid.MouseButton.Middle => MouseButton.Middle,
            Veldrid.MouseButton.Button1 => MouseButton.XButton1,
            Veldrid.MouseButton.Button2 => MouseButton.XButton2,
            _ => MouseButton.Left
        };
    }

    private static KeyCode ConvertKey(Veldrid.Key key)
    {
        return key switch
        {
            // Letters
            Veldrid.Key.A => KeyCode.A,
            Veldrid.Key.B => KeyCode.B,
            Veldrid.Key.C => KeyCode.C,
            Veldrid.Key.D => KeyCode.D,
            Veldrid.Key.E => KeyCode.E,
            Veldrid.Key.F => KeyCode.F,
            Veldrid.Key.G => KeyCode.G,
            Veldrid.Key.H => KeyCode.H,
            Veldrid.Key.I => KeyCode.I,
            Veldrid.Key.J => KeyCode.J,
            Veldrid.Key.K => KeyCode.K,
            Veldrid.Key.L => KeyCode.L,
            Veldrid.Key.M => KeyCode.M,
            Veldrid.Key.N => KeyCode.N,
            Veldrid.Key.O => KeyCode.O,
            Veldrid.Key.P => KeyCode.P,
            Veldrid.Key.Q => KeyCode.Q,
            Veldrid.Key.R => KeyCode.R,
            Veldrid.Key.S => KeyCode.S,
            Veldrid.Key.T => KeyCode.T,
            Veldrid.Key.U => KeyCode.U,
            Veldrid.Key.V => KeyCode.V,
            Veldrid.Key.W => KeyCode.W,
            Veldrid.Key.X => KeyCode.X,
            Veldrid.Key.Y => KeyCode.Y,
            Veldrid.Key.Z => KeyCode.Z,

            // Numbers
            Veldrid.Key.Number0 => KeyCode.D0,
            Veldrid.Key.Number1 => KeyCode.D1,
            Veldrid.Key.Number2 => KeyCode.D2,
            Veldrid.Key.Number3 => KeyCode.D3,
            Veldrid.Key.Number4 => KeyCode.D4,
            Veldrid.Key.Number5 => KeyCode.D5,
            Veldrid.Key.Number6 => KeyCode.D6,
            Veldrid.Key.Number7 => KeyCode.D7,
            Veldrid.Key.Number8 => KeyCode.D8,
            Veldrid.Key.Number9 => KeyCode.D9,

            // Function Keys
            Veldrid.Key.F1 => KeyCode.F1,
            Veldrid.Key.F2 => KeyCode.F2,
            Veldrid.Key.F3 => KeyCode.F3,
            Veldrid.Key.F4 => KeyCode.F4,
            Veldrid.Key.F5 => KeyCode.F5,
            Veldrid.Key.F6 => KeyCode.F6,
            Veldrid.Key.F7 => KeyCode.F7,
            Veldrid.Key.F8 => KeyCode.F8,
            Veldrid.Key.F9 => KeyCode.F9,
            Veldrid.Key.F10 => KeyCode.F10,
            Veldrid.Key.F11 => KeyCode.F11,
            Veldrid.Key.F12 => KeyCode.F12,

            // Controls
            Veldrid.Key.Space => KeyCode.Space,
            Veldrid.Key.Enter => KeyCode.Enter,
            Veldrid.Key.Tab => KeyCode.Tab,
            Veldrid.Key.BackSpace => KeyCode.Backspace,
            Veldrid.Key.Escape => KeyCode.Escape,
            Veldrid.Key.Insert => KeyCode.Insert,
            Veldrid.Key.Delete => KeyCode.Delete,
            Veldrid.Key.Home => KeyCode.Home,
            Veldrid.Key.End => KeyCode.End,
            Veldrid.Key.PageUp => KeyCode.PageUp,
            Veldrid.Key.PageDown => KeyCode.PageDown,

            // Arrows
            Veldrid.Key.Left => KeyCode.LeftArrow,
            Veldrid.Key.Right => KeyCode.RightArrow,
            Veldrid.Key.Up => KeyCode.UpArrow,
            Veldrid.Key.Down => KeyCode.DownArrow,

            // Modifiers
            Veldrid.Key.LWin => KeyCode.LeftSuper,
            Veldrid.Key.RWin => KeyCode.RightSuper,
            Veldrid.Key.LShift => KeyCode.LeftShift,
            Veldrid.Key.RShift => KeyCode.RightShift,
            Veldrid.Key.LControl => KeyCode.LeftCtrl,
            Veldrid.Key.RControl => KeyCode.RightCtrl,
            Veldrid.Key.LAlt => KeyCode.LeftAlt,
            Veldrid.Key.RAlt => KeyCode.RightAlt,
            Veldrid.Key.CapsLock => KeyCode.CapsLock,
            Veldrid.Key.ScrollLock => KeyCode.ScrollLock,
            Veldrid.Key.NumLock => KeyCode.NumLock,

            // Numpad
            Veldrid.Key.Keypad0 => KeyCode.NumPad0,
            Veldrid.Key.Keypad1 => KeyCode.NumPad1,
            Veldrid.Key.Keypad2 => KeyCode.NumPad2,
            Veldrid.Key.Keypad3 => KeyCode.NumPad3,
            Veldrid.Key.Keypad4 => KeyCode.NumPad4,
            Veldrid.Key.Keypad5 => KeyCode.NumPad5,
            Veldrid.Key.Keypad6 => KeyCode.NumPad6,
            Veldrid.Key.Keypad7 => KeyCode.NumPad7,
            Veldrid.Key.Keypad8 => KeyCode.NumPad8,
            Veldrid.Key.Keypad9 => KeyCode.NumPad9,

            // Symbols
            Veldrid.Key.Plus => KeyCode.Plus,
            Veldrid.Key.Minus => KeyCode.Minus,
            Veldrid.Key.Comma => KeyCode.Comma,
            Veldrid.Key.Period => KeyCode.Period,
            Veldrid.Key.Slash => KeyCode.Slash,
            Veldrid.Key.BackSlash => KeyCode.Backslash,
            Veldrid.Key.Semicolon => KeyCode.Semicolon,
            Veldrid.Key.Quote => KeyCode.Quote,
            Veldrid.Key.BracketLeft => KeyCode.LeftBracket,
            Veldrid.Key.BracketRight => KeyCode.RightBracket,
            Veldrid.Key.Grave => KeyCode.Tilde,

            _ => KeyCode.Unknown
        };
    }

    private static KeyModifier ConvertKeyModifiers(Veldrid.ModifierKeys modifiers)
    {
        return (KeyModifier) (int) modifiers;
    }
}