namespace Aethermeter.Combat;

public enum ActorKind : byte
{
    Unknown,
    Player,
    /// <summary>A pet, summon or companion: anything with an owner.</summary>
    Pet,
    /// <summary>Trust and Duty Support allies.</summary>
    Ally,
    /// <summary>A chocobo companion: counted as a combatant of its own, not folded into its owner.</summary>
    Chocobo,
    Enemy,
    Other,
}

/// <summary>
/// What we know about someone when an event happened. Kept by value on events and combatants,
/// so names survive the actor despawning or leaving the party.
/// </summary>
public sealed class Actor
{
    public uint EntityId { get; init; }
    public string Name { get; set; } = string.Empty;
    public uint JobId { get; set; }
    public ActorKind Kind { get; init; }
    public uint OwnerId { get; init; }
    /// <summary>Chocobos: their rider's name, remembered in case the rider leaves.</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>The local player, the party, their pets and allies: everyone the meter is about.</summary>
    public bool Friendly { get; set; }
    public bool IsLocalPlayer { get; set; }

    public static readonly Actor Unknown = new() { Name = "?", Kind = ActorKind.Unknown };
}
