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


    public RebelManager(ISwiftlyCore core, IJBPlayerManagement players, IOptions<UtilsConfig> utilsConfig)
    {
        _core = core;
        _players = players;
        _utilsConfig = utilsConfig.Value;
    }

    private void MakeRebel(IJBPlayer player)
    {
        player.SetRebel(true);
        _players.SendMessage(MessageType.Alert, "became_rebel_alert", false, args: player.Player.Name);
        _players.SendMessage(MessageType.Chat, "became_rebel_chat", true, args: player.Player.Name);
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult EventWeaponFire(EventWeaponFire e)
    {
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

    [GameEventHandler(HookMode.Post)]
    private HookResult EventPlayerHurt(EventPlayerHurt e)
    {
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

    [GameEventHandler(HookMode.Post)]
    private HookResult EventPlayerDeath(EventPlayerDeath e)
    {
        if (e.UserIdPlayer == null || e.AttackerPlayer == null)
            return HookResult.Continue;

        var victim   = _players.SyncPlayer(e.UserIdPlayer);
        var attacker = _players.SyncPlayer(e.AttackerPlayer);

        if (victim == null || attacker == null || attacker == victim)
            return HookResult.Continue;

        if (_utilsConfig.AnnounceGuardsDeath)
        {
            _players.SendMessage(MessageType.Chat, "guard_death", true, 0, victim.Player.Name, attacker.Player.Name);
        }

        if (attacker.Team != JBTeam.Prisoner && victim.Team != JBTeam.Guard)
            return HookResult.Continue;

        if (!attacker.IsRebel)
            MakeRebel(attacker);


        return HookResult.Continue;
    }

}
