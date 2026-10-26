using System;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace Aethermeter.Combat;

/// <summary>
/// The two game functions Aethermeter listens to. Both are read-only taps: the original always
/// runs, and our work happens first so HP still reads as it was before the hit.
///
/// <c>ActionEffectHandler.Receive</c> handles every ActionEffect packet: each action's damage,
/// heals and misses on up to 32 targets. <c>StatusManager.ProcessHotDot</c> handles DoT and HoT
/// ticks; those go to the <see cref="TickAttributor"/>, which works out whose they were. Both
/// addresses come from FFXIVClientStructs, so there are no signatures of our own to keep up to date.
/// </summary>
public sealed unsafe class CombatHooks : IDisposable
{
    // ActionEffect types we read (the rest are statuses, enmity, knockback and so on).
    private const byte EffectMiss = 1;
    private const byte EffectDamage = 3;
    private const byte EffectHeal = 4;
    private const byte EffectBlocked = 5;
    private const byte EffectParried = 6;

    private const byte FlagLargeValue = 0x40;
    private const byte FlagOnSource = 0x80;
    private const byte DamageCrit = 0x20;
    private const byte DamageDirectHit = 0x40;
    private const byte HealCrit = 0x20;

    private readonly ActorResolver actors;
    private readonly EventEmitter emitter;
    private readonly TickAttributor ticks;
    private readonly Func<double> clock;

    private Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;
    private Hook<StatusManager.Delegates.ProcessHotDot>? tickHook;
    private Hook<EventFramework.Delegates.ProcessDirectorUpdate>? directorHook;

    /// <summary>Gets the raw lines for the FFLogs log, when it's recording.</summary>
    public Logging.NetworkLog? Log { get; set; }

    public string? FailureReason { get; private set; }
    public bool IsActive => receiveHook?.IsEnabled == true;
    public bool TicksActive => tickHook?.IsEnabled == true;
    public int Errors { get; private set; }

    public CombatHooks(ActorResolver actors, Func<double> clock, EventEmitter emitter, TickAttributor ticks)
    {
        this.actors = actors;
        this.clock = clock;
        this.emitter = emitter;
        this.ticks = ticks;
    }

    public bool Enable()
    {
        try
        {
            var receive = (nint)ActionEffectHandler.Addresses.Receive.Value;
            if (receive == 0)
            {
                FailureReason = "ActionEffectHandler.Receive was not found in this game version.";
                Plugin.Log.Error("Combat hook unavailable: {Reason}", FailureReason);
                return false;
            }

            receiveHook = Plugin.GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(receive, ReceiveDetour);
            receiveHook.Enable();

            var tick = (nint)StatusManager.Addresses.ProcessHotDot.Value;
            if (tick != 0)
            {
                tickHook = Plugin.GameInteropProvider.HookFromAddress<StatusManager.Delegates.ProcessHotDot>(tick, TickDetour);
                tickHook.Enable();
            }
            else
                Plugin.Log.Warning("DoT/HoT tick hook unavailable; ticks will not be counted");

            // Only the FFLogs log needs director updates (pull start, wipe, clear); the meter works without them.
            var director = (nint)EventFramework.Addresses.ProcessDirectorUpdate.Value;
            if (director != 0)
            {
                directorHook = Plugin.GameInteropProvider.HookFromAddress<EventFramework.Delegates.ProcessDirectorUpdate>(director, DirectorDetour);
                directorHook.Enable();
            }
            else
                Plugin.Log.Warning("Director hook unavailable; logs will have no pull start/wipe markers");

            FailureReason = null;
            Plugin.Log.Info("Combat hooks enabled (receive 0x{Receive:X}, ticks 0x{Tick:X})", receive, tick);
            return true;
        }
        catch (Exception ex)
        {
            FailureReason = $"Hook setup failed: {ex.Message}";
            Plugin.Log.Error(ex, "Combat hooks unavailable");
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        receiveHook?.Dispose();
        tickHook?.Dispose();
        directorHook?.Dispose();
        receiveHook = null;
        tickHook = null;
        directorHook = null;
    }

    public bool DirectorActive => directorHook?.IsEnabled == true;

    private void DirectorDetour(EventFramework* framework, EventId eventId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6)
    {
        try
        {
            Log?.OnDirector(eventId.Id, category, arg1, arg2, arg3, arg4);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        directorHook!.Original(framework, eventId, category, arg1, arg2, arg3, arg4, arg5, arg6);
    }

    private void ReceiveDetour(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        try
        {
            Log?.OnActionEffect(casterEntityId, header, effects, targetEntityIds);
            HandleEffects(casterEntityId, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        receiveHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private void TickDetour(StatusManager* manager, BattleChara* target, uint statusId, int tickMode, uint value, uint sourceEntityId, int damageType)
    {
        try
        {
            if (target != null && value > 0)
            {
                var heal = ticks.Queue(target->EntityId, statusId, tickMode, value, sourceEntityId, damageType, clock());
                Log?.OnTick(target->EntityId, statusId, heal, value, sourceEntityId, damageType);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        tickHook!.Original(manager, target, statusId, tickMode, value, sourceEntityId, damageType);
    }

    private void HandleEffects(uint casterId, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetIds)
    {
        if (header == null || effects == null || targetIds == null) return;

        var caster = actors.Resolve(casterId);
        var owner = actors.Owner(caster);
        var now = clock();
        var count = Math.Min((int)header->NumTargets, 32);

        for (var i = 0; i < count; i++)
        {
            var targetId = targetIds[i].ObjectId;
            if (targetId == 0 || targetId == ActorResolver.InvalidId) continue;

            Actor? target = null;
            var list = effects[i].Effects;
            for (var j = 0; j < list.Length; j++)
            {
                ref readonly var fx = ref list[j];
                if (fx.Type is not (EffectMiss or EffectDamage or EffectHeal or EffectBlocked or EffectParried)) continue;

                // Some effects (drain, self-heals riding on an attack) land on the caster instead.
                var onSource = (fx.Param4 & FlagOnSource) != 0;
                var who = onSource ? caster : target ??= actors.Resolve(targetId);
                if (!owner.Friendly && !who.Friendly) continue;

                long amount = fx.Value;
                if ((fx.Param4 & FlagLargeValue) != 0) amount += (long)fx.Param3 << 16;

                var e = new CombatEvent
                {
                    Time = now,
                    Source = owner,
                    Pet = owner == caster ? null : caster,
                    Target = who,
                    ActionId = header->ActionId,
                    Amount = amount,
                };

                switch (fx.Type)
                {
                    case EffectMiss:
                        e.Kind = EventKind.Miss;
                        e.Amount = 0;
                        break;
                    case EffectHeal:
                        e.Kind = EventKind.Heal;
                        e.Crit = (fx.Param1 & HealCrit) != 0;
                        break;
                    default:
                        e.Kind = EventKind.Damage;
                        e.Crit = (fx.Param0 & DamageCrit) != 0;
                        e.DirectHit = (fx.Param0 & DamageDirectHit) != 0;
                        e.Blocked = fx.Type == EffectBlocked;
                        e.Parried = fx.Type == EffectParried;
                        break;
                }

                emitter.Emit(e);
            }
        }
    }

    private void Fail(Exception ex)
    {
        if (Errors++ < 5)
            Plugin.Log.Error(ex, "Error while reading a combat event");
    }
}
