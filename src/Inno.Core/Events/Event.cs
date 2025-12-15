namespace Inno.Core.Events;

[Flags]
public enum EventCategory
{
    None = 0,
    Application = 1 << 0,
    Input       = 1 << 1,
    Keyboard    = 1 << 2,
    Mouse       = 1 << 3
}

public enum EventType
{
    None = 0,

    // Application
    WindowClose,
    WindowResize,
        
    // Keyboard
    KeyPressed,
    KeyReleased,
        
    // Mouse
    MouseButtonPressed,
    MouseButtonReleased,
    MouseMoved,
    MouseScrolled
}

public abstract class Event
{
    public abstract EventType type { get; }
    public abstract EventCategory category { get; }
    
    public bool handled { get; set; } = false;

    public bool IsInCategory(EventCategory c)
    {
        return (this.category & c) != 0;
    }
}

// Application
public abstract class WindowEvent : Event
{
    public sealed override EventCategory category => EventCategory.Application;
}


public class WindowResizeEvent(int width, int height) : WindowEvent
{
    public int width { get; } = width;
    public int height { get; } = height;

    public override EventType type => EventType.WindowResize;
}

public class WindowCloseEvent : WindowEvent
{
    public override EventType type => EventType.WindowClose;
}

// Keyboard
public abstract class KeyEvent(Input.KeyCode key, Input.KeyModifier modifiers = Input.KeyModifier.None)
    : Event
{
    public Input.KeyCode key { get; } = key;
    public Input.KeyModifier modifiers { get; } = modifiers;

    public sealed override EventCategory category => EventCategory.Input | EventCategory.Keyboard;
}

public class KeyPressedEvent(
    Input.KeyCode key,
    Input.KeyModifier modifiers = Input.KeyModifier.None,
    bool repeat = false)
    : KeyEvent(key, modifiers)
{
    public bool repeat { get; } = repeat;

    public override EventType type => EventType.KeyPressed;
}

public class KeyReleasedEvent(Input.KeyCode key, Input.KeyModifier modifiers = Input.KeyModifier.None)
    : KeyEvent(key, modifiers)
{
    public override EventType type => EventType.KeyReleased;
}

// Mouse
public abstract class MouseEvent : Event
{
    public sealed override EventCategory category => EventCategory.Input | EventCategory.Mouse;
}


public class MouseMovedEvent(float x, float y) : MouseEvent
{
    public float x { get; } = x;
    public float y { get; } = y;
    public override EventType type => EventType.MouseMoved;
}

public class MouseScrolledEvent(float offsetX, float offsetY) : MouseEvent
{
    public float offsetX { get; } = offsetX;
    public float offsetY { get; } = offsetY;
    public override EventType type => EventType.MouseScrolled;
}

public abstract class MouseButtonEvent(Input.MouseButton button) : MouseEvent
{
    public Input.MouseButton button { get; } = button;
}

public class MouseButtonPressedEvent(Input.MouseButton button) : MouseButtonEvent(button)
{
    public override EventType type => EventType.MouseButtonPressed;
}

public class MouseButtonReleasedEvent(Input.MouseButton button) : MouseButtonEvent(button)
{
    public override EventType type => EventType.MouseButtonReleased;
}
