using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class Listeners
{
    private readonly IJBPlayerManagement    _players;
    private readonly ISwiftlyCore           _core;
    private readonly ModelsConfig          _modelsConfig;
    private readonly SoundsConfig          _soundsConfig;
    private readonly SpecialDayManager     _specialDayManager;

    public Listeners(ISwiftlyCore core, IJBPlayerManagement playerManagement, IOptions<ModelsConfig> modelsConfig, IOptions<SoundsConfig> soundsConfig, SpecialDayManager specialDayManager)
    {
        _core    = core;
        _modelsConfig = modelsConfig.Value;
        _soundsConfig=  soundsConfig.Value;
        _players = playerManagement;
        _specialDayManager = specialDayManager;
    }

    public void Register()
    {
        _core.Event.OnPrecacheResource           += OnPrecacheResource;
        _core.GameHooks.Entities.TakeDamage.Post += OnEntityTakeDamage;
    }

    public void Unregister()
    {
        _core.Event.OnPrecacheResource           -= OnPrecacheResource;
        _core.GameHooks.Entities.TakeDamage.Post -= OnEntityTakeDamage;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        var resources = new HashSet<string>(StringComparer.Ordinal);
        AddResource(IconManager.CoinModelPath);
        AddResource(_modelsConfig.WardenModel);
        AddResource(_modelsConfig.DeputyModel);
        AddResource(_modelsConfig.FreedayModel);
        foreach (var model in _modelsConfig.GuardModels) AddResource(model);
        foreach (var model in _modelsConfig.PrisonerModels) AddResource(model);
        foreach (var sound in _soundsConfig.SoundEventFiles) AddResource(sound);

        void AddResource(string resource)
        {
            if (!string.IsNullOrWhiteSpace(resource) && resources.Add(resource))
                @event.AddItem(resource);
        }
    }

    private void OnEntityTakeDamage(ref TakeDamageEntityPostContext ctx)
    {
        var e = ctx.Params;
        if (_specialDayManager.IsSpecialDayCountdownActive)
        {
            e.Info.Damage = 0;
            e.Info.TotalledDamage = 0;
            ctx.SetHookResult(HookResult.Stop);
            return;
        }

        if (_specialDayManager.IsSpecialDayActive)
            return;

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
