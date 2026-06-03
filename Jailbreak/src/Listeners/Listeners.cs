using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class Listeners
{
    private readonly IJBPlayerManagement    _players;
    private readonly ISwiftlyCore           _core;
    private readonly ModelsConfig          _modelsConfig;

    public Listeners(ISwiftlyCore core, IJBPlayerManagement playerManagement, IOptions<ModelsConfig> modelsConfig)
    {
        _core    = core;
        _modelsConfig = modelsConfig.Value;
         _players = playerManagement;
    }

    public void Register()
    {
        _core.Event.OnPrecacheResource += OnPrecacheResource;
        _core.Event.OnMapUnload        += OnMapUnload;
        _core.Event.OnEntityTakeDamage += OnEntityTakeDamage;
    }

    public void Unregister()
    {
        _core.Event.OnPrecacheResource -= OnPrecacheResource;
        _core.Event.OnMapUnload        -= OnMapUnload;
        _core.Event.OnEntityTakeDamage -= OnEntityTakeDamage;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        @event.AddItem(IconManager.CoinModelPath);
        @event.AddItem(_modelsConfig.WardenModel);
        @event.AddItem(_modelsConfig.DeputyModel);
        @event.AddItem(_modelsConfig.FreedayModel);
        foreach (var m in _modelsConfig.GuardModels)    @event.AddItem(m);
        foreach (var m in _modelsConfig.PrisonerModels) @event.AddItem(m);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
    }

    private void OnEntityTakeDamage(IOnEntityTakeDamageEvent e)
    {
        if (!e.Entity.DesignerName.Contains("weapon"))
            return; // ignore everything else except weapons.

        var attackerPawn = e.Info.AttackerInfo.AttackerPawn.Value;
        if (attackerPawn == null)
            return;

        var rawAttacker = attackerPawn.ToPlayer();
        if (rawAttacker == null)
            return;

        var attacker = _players.SyncPlayer(rawAttacker);
        if (attacker == null || !attacker.IsWarden)
            return;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (e.Entity.IsValid)
                e.Entity.Despawn();
        });
    }
}
