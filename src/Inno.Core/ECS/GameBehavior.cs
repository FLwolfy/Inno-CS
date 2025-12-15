namespace Inno.Core.ECS;

/// <summary>
/// Abstract class for game behaviors.
/// GameBehaviors are GameComponents whose active states can be set.
/// </summary>
public abstract class GameBehavior : GameComponent
{
    public new bool isActive
    {
        get => base.isActive;
        private set => base.isActive = value;
    }

    protected internal override void Initialize(GameObject obj)
    {
        base.Initialize(obj);
        if (isActive) OnEnable();
    }

    protected internal override void OnDeactivate()
    {
        OnDisable();
    }

    /// <summary>
    /// This is called when the behavior is set to active.
    /// </summary>
    protected virtual void OnEnable() { }
    
    /// <summary>
    /// This is called when the behavior is set to inactive.
    /// </summary>
    protected virtual void OnDisable() { }

    /// <summary>
    /// Set the active state of the behavior.
    /// </summary>
    /// <param name="active">The active state</param>
    public void SetActive(bool active)
    {
        if (isActive == active) return;

        isActive = active;

        if (active) { OnEnable(); }
        else { OnDisable(); }
    }
}