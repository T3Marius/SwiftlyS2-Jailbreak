using Jailbreak.Contract;
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


    public RebelManager(ISwiftlyCore core, IJBPlayerManagement players)
    {
        _core = core;
        _players = players;

        _core.Registrator.Register(this);
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

        var player = _players.GetOrCreatePlayer(e.UserIdPlayer);
        if (player == null || player.IsRebel || player.Team != JBTeam.Prisoner)
            return HookResult.Continue;

        MakeRebel(player);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult EventPlayerHurt(EventPlayerHurt e)
    {
        if (e.UserIdPlayer == null || e.AttackerPlayer == null)
            return HookResult.Continue;

        var victim = _players.GetOrCreatePlayer(e.UserIdPlayer);
        var attacker = _players.GetOrCreatePlayer(e.AttackerPlayer);
        if (victim == null || attacker == null || attacker.IsRebel)
            return HookResult.Continue;

        if (attacker.Team == JBTeam.Prisoner && victim.Team == JBTeam.Guard)
            MakeRebel(attacker);

        return HookResult.Continue;
    }

}