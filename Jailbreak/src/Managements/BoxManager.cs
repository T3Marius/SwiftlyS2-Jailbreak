using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Sounds;

namespace Jailbreak;

public sealed class BoxManager
{
    private readonly ISwiftlyCore _core;
    private readonly UtilsConfig  _utilsConfig;
    private IConVar<bool>?        _teammatesAreEnemies; // convar to start box between prisoners.

    /// <summary>
    /// Whether the box is enabled or not.
    /// </summary>
    public bool BoxEnabled { get; set; }

    public BoxManager(ISwiftlyCore core, IOptions<UtilsConfig> utilsConfig)
    {
        _core = core;
        _utilsConfig = utilsConfig.Value;

        _teammatesAreEnemies = _core.ConVar.Find<bool>("mp_teammates_are_enemies");
    }

    public void Register()
    {
        _core.Event.OnEntityTakeDamage += OnEntityTakeDamage;
    }

    public void Unregister()
    {
        _core.Event.OnEntityTakeDamage -= OnEntityTakeDamage;
        StopBox();
    }

    public void StartBox()
    {
        BoxEnabled = true;

        _teammatesAreEnemies?.SetInternal(true);

        if (!string.IsNullOrEmpty(_utilsConfig.BoxStartSound))
        {
            var boxSound = new SoundEvent()
            {
                Name = _utilsConfig.BoxStartSound,
                Volume = _utilsConfig.BoxStartSoundVolume,
            };
            boxSound.Recipients.AddAllPlayers();
            boxSound.Emit();

        }

        if (_utilsConfig.HideTeammatesName)
        {
            _core.Engine.ExecuteCommand("sv_teamid_overhead 0");
        }
    }
    public void StopBox()
    {
        BoxEnabled = false;

        _teammatesAreEnemies?.SetInternal(false);

        if (_utilsConfig.HideTeammatesName)
        {
            _core.Engine.ExecuteCommand("sv_teamid_overhead 1");
        }
    }

    private void OnEntityTakeDamage(IOnEntityTakeDamageEvent e)
    {
        if (!BoxEnabled) // completly skip the check if the box is not enabled to save performance, since this event is called very often.
            return;
    
        if (!e.Entity.DesignerName.Contains("player")) // skip check if the entity is not a player.
            return;
        
        var attackerPawn = e.Info.AttackerInfo.AttackerPawn;
        if (attackerPawn.Value == null)
            return;
        
        var attacker = attackerPawn.Value.ToPlayer();
        if (attacker == null)
            return;
        
        var victim = GetPlayerFromEntityAddress(e.Entity.Address);
        if (victim == null)
            return;

        if (attacker.Controller.Team != Team.T && victim.Controller.Team != Team.T) 
        {
            e.Info.Damage = 0;
            e.Result = HookResult.Stop;
        }
    }
    private IPlayer? GetPlayerFromEntityAddress(nint addres)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.PlayerPawn?.Address == addres)
                return player;
        }

        return null;
    }
}
