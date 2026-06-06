using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class RebelManager
{
    private readonly ISwiftlyCore        _core;
    private readonly IJBPlayerManagement _players;
    private readonly UtilsConfig         _utilsConfig;
    private readonly SpecialDayManager   _specialDayManager;
    private readonly LastRequestManager  _lastRequestManager;

    private Guid? _weaponFireHookId;
    private Guid? _playerHurtHookId;
    private Guid? _playerDeathHookId;

    public RebelManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        IOptions<UtilsConfig> utilsConfig,
        SpecialDayManager specialDayManager,
        LastRequestManager lastRequestManager)
    {
        _core = core;
        _players = players;
        _utilsConfig = utilsConfig.Value;
        _specialDayManager = specialDayManager;
        _lastRequestManager = lastRequestManager;
    }

    public void Register()
    {
        _weaponFireHookId = _core.GameEvent.HookPost<EventWeaponFire>(EventWeaponFire);
        _playerHurtHookId = _core.GameEvent.HookPost<EventPlayerHurt>(EventPlayerHurt);
        _playerDeathHookId = _core.GameEvent.HookPost<EventPlayerDeath>(EventPlayerDeath);
    }

    public void Unregister()
    {
        Unhook(ref _weaponFireHookId);
        Unhook(ref _playerHurtHookId);
        Unhook(ref _playerDeathHookId);
    }

    private void MakeRebel(IJBPlayer player)
    {
        player.SetRebel(true);
        _players.SendMessage(MessageType.Alert, "became_rebel_alert", false, args: player.Player.Name);
        _players.SendMessage(MessageType.Chat, "became_rebel_chat", true, args: player.Player.Name);
    }

    private HookResult EventWeaponFire(EventWeaponFire e)
    {
        if (_specialDayManager.IsSpecialDayActive || _lastRequestManager.IsLastRequestActive)
            return HookResult.Continue;

        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(e.UserIdPlayer);

        if (player == null || player.IsRebel || player.Team != JBTeam.Prisoner)
            return HookResult.Continue;
        
        var weapon = e.Weapon;

        if (weapon.Contains("knife"))   // ignore knife fire
            return HookResult.Continue;

        MakeRebel(player);
        return HookResult.Continue;
    }

    private HookResult EventPlayerHurt(EventPlayerHurt e)
    {
        if (_specialDayManager.IsSpecialDayActive || _lastRequestManager.IsLastRequestActive)
            return HookResult.Continue;

        if (e.UserIdPlayer == null || e.AttackerPlayer == null)
            return HookResult.Continue;

        var victim   = _players.SyncPlayer(e.UserIdPlayer);
        var attacker = _players.SyncPlayer(e.AttackerPlayer);

        if (victim == null || attacker == null || attacker.IsRebel)
            return HookResult.Continue;

        if (attacker.Team == JBTeam.Prisoner && victim.Team == JBTeam.Guard)
            MakeRebel(attacker);

        return HookResult.Continue;
    }

    private HookResult EventPlayerDeath(EventPlayerDeath e)
    {
        if (_specialDayManager.IsSpecialDayActive 
            || _lastRequestManager.IsLastRequestActive 
            || _lastRequestManager.WasLastRequestActiveThisFrame)
            return HookResult.Continue;

        if (e.UserIdPlayer == null || e.AttackerPlayer == null)
            return HookResult.Continue;

        var victim   = _players.SyncPlayer(e.UserIdPlayer);
        var attacker = _players.SyncPlayer(e.AttackerPlayer);

        if (victim == null || attacker == null || attacker == victim)
            return HookResult.Continue;

        if (attacker.Team != JBTeam.Prisoner || victim.Team != JBTeam.Guard)
            return HookResult.Continue;

        if (_utilsConfig.AnnounceGuardsDeath)
        {
            _players.SendMessage(MessageType.Chat, "guard_death", true, 0, victim.Player.Name, attacker.Player.Name);
        }

        if (!attacker.IsRebel)
            MakeRebel(attacker);


        return HookResult.Continue;
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
    }
}
