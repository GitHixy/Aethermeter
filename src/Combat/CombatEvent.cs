namespace Aethermeter.Combat;

public enum EventKind : byte
{
    Damage,
    Heal,
    Miss,
}

/// <summary>
/// One resolved hit, heal or miss, already attributed to the actors involved.
/// <see cref="Source"/> is the owner when a pet acted; <see cref="Pet"/> keeps the pet itself.
/// </summary>
public struct CombatEvent
{
    public double Time;
    public EventKind Kind;

    public Actor Source;
    public Actor? Pet;
    public Actor Target;

    /// <summary>The action that landed, or 0 for a tick.</summary>
    public uint ActionId;
    /// <summary>The status that ticked, for DoTs and HoTs.</summary>
    public uint StatusId;

    public long Amount;
    /// <summary>Heals only: the part that landed on an already full bar (estimated).</summary>
    public long Overheal;

    /// <summary>Heals only: damage soaked by a shield rather than HP restored.</summary>
    public bool Absorb;

    public bool Crit;
    public bool DirectHit;
    public bool Blocked;
    public bool Parried;

    /// <summary>Target HP just before the effect, for death recaps.</summary>
    public uint TargetHp;
    public uint TargetMaxHp;

    public readonly bool IsTick => ActionId == 0 && StatusId != 0 && !Absorb;
}
