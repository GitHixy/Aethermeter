using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Aethermeter.Models;

namespace Aethermeter.Combat;

/// <summary>
/// Turns entity ids into <see cref="Actor"/>s and decides who counts as "us". The party is read
/// once per frame; actors are cached so names stay known after someone despawns.
/// </summary>
public sealed class ActorResolver
{
    public const uint InvalidId = 0xE0000000;

    private readonly Configuration config;
    private readonly Dictionary<uint, Actor> cache = new();
    private readonly HashSet<uint> party = new();
    private uint localId;
    private long partyFrame = -1;

    public ActorResolver(Configuration config) => this.config = config;

    public void Clear() => cache.Clear();

    /// <summary>The ids the meter follows this frame, for death and combat checks.</summary>
    public IReadOnlyCollection<uint> Party
    {
        get
        {
            RefreshParty();
            return party;
        }
    }

    public Actor Resolve(uint entityId)
    {
        if (entityId == 0 || entityId == InvalidId) return Actor.Unknown;
        RefreshParty();

        var obj = Plugin.ObjectTable.SearchByEntityId(entityId);
        if (obj is null)
            return cache.TryGetValue(entityId, out var known) ? known : Actor.Unknown;

        if (!cache.TryGetValue(entityId, out var actor) || actor.Kind != KindOf(obj))
        {
            actor = new Actor { EntityId = entityId, Kind = KindOf(obj), OwnerId = obj.OwnerId };
            cache[entityId] = actor;
        }

        actor.Name = obj.Name.TextValue;
        if (obj is ICharacter ch) actor.JobId = ch.ClassJob.RowId;
        actor.IsLocalPlayer = entityId == localId;
        actor.Friendly = actor.Kind switch
        {
            ActorKind.Player => actor.IsLocalPlayer || party.Contains(entityId) || config.TrackScope == TrackScope.Everyone,
            ActorKind.Ally => true,
            ActorKind.Chocobo => ChocoboRider(actor),
            _ => false,
        };
        return actor;
    }

    /// <summary>A chocobo is on our side when its rider is; it also learns the rider's name here.</summary>
    private bool ChocoboRider(Actor chocobo)
    {
        if (chocobo.OwnerId == 0 || chocobo.OwnerId == InvalidId) return false;
        var rider = Resolve(chocobo.OwnerId);
        if (rider == Actor.Unknown) return false;
        if (rider.Name.Length > 0) chocobo.OwnerName = rider.Name;
        return rider.Friendly;
    }

    /// <summary>The actor that should be credited: a pet's owner, or the actor itself.</summary>
    public Actor Owner(Actor actor)
    {
        if (actor.Kind != ActorKind.Pet || actor.OwnerId == 0 || actor.OwnerId == InvalidId) return actor;
        var owner = Resolve(actor.OwnerId);
        if (owner == Actor.Unknown) return actor;
        actor.Friendly = owner.Friendly;
        return owner;
    }

    private static ActorKind KindOf(IGameObject obj)
    {
        if (obj.ObjectKind == ObjectKind.Pc) return ActorKind.Player;
        if (obj.ObjectKind == ObjectKind.Companion) return ActorKind.Pet;
        if (obj.ObjectKind != ObjectKind.BattleNpc) return ActorKind.Other;

        if (obj is IBattleNpc npc)
        {
            switch (npc.BattleNpcKind)
            {
                case BattleNpcSubKind.Pet:
                    return ActorKind.Pet;
                case BattleNpcSubKind.Buddy:
                    return ActorKind.Chocobo;
                case BattleNpcSubKind.NpcPartyMember:
                    return ActorKind.Ally;
                case BattleNpcSubKind.Combatant:
                    return obj.OwnerId != 0 && obj.OwnerId != InvalidId ? ActorKind.Pet : ActorKind.Enemy;
            }
        }

        return obj.OwnerId != 0 && obj.OwnerId != InvalidId ? ActorKind.Pet : ActorKind.Other;
    }

    private void RefreshParty()
    {
        var frame = Plugin.Framework.LastUpdateUTC.Ticks;
        if (frame == partyFrame) return;
        partyFrame = frame;

        party.Clear();
        localId = Plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        if (localId != 0) party.Add(localId);

        foreach (var member in Plugin.PartyList)
            if (member.EntityId != 0 && member.EntityId != InvalidId)
                party.Add(member.EntityId);
    }
}
