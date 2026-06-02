using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace Jailbreak;

public sealed class JBPlayer : IJBPlayer
{
    // ── Identity ─────────────────────────────────────────────────────────────
    public IPlayer Player  { get; }
    public ulong   SteamID { get; }

    // ── Jailbreak state ───────────────────────────────────────────────────────
    public JBTeam Team { get; set; } = JBTeam.None;
    public JBRole Role { get; set; } = JBRole.None;

    public bool IsFreeday       => Role == JBRole.Freeday;
    public bool IsRebel         => Role == JBRole.Rebel;
    public bool IsDeputy        => Role == JBRole.Deputy;
    public bool IsWarden        => Role == JBRole.Warden;

    public bool IsCuffed           { get; set; } = false;
    public bool CanBecomeWarden    { get; set; } = true;
    public bool IsMuted            { get; set; } = false;
    public bool WasUnmutedByWarden { get; set; } = false;

    // ── Localizer (falls back to server locale when player is no longer valid) ─
    public ILocalizer Localizer => Player.IsValid
        ? _core.Translation.GetPlayerLocalizer(Player)
        : _core.Localizer;

    // ── Private deps ──────────────────────────────────────────────────────────
    private readonly ISwiftlyCore  _core;
    private readonly ModelsConfig  _modelsConfig;
    private readonly IconManager   _iconManager;

    public JBPlayer(IPlayer player, ISwiftlyCore core, IOptions<ModelsConfig> modelsConfig, IconManager iconManager)
    {
        Player        = player;
        SteamID       = player.SteamID;
        _core         = core;
        _modelsConfig = modelsConfig.Value;
        _iconManager  = iconManager;
    }

    // ── Role transitions ──────────────────────────────────────────────────────

    public void SetWarden(bool state, string? offReason = null, string? killerName = null, bool silent = false)
    {
        if (state)
        {
            Role = JBRole.Warden;
            _iconManager.SpawnCoin(Player);
            if (!silent)
                _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["new_warden_alert", Player.Name]);

            if (!string.IsNullOrEmpty(_modelsConfig.WardenModel))
                PlayerUtils.SetModel(Player, _modelsConfig.WardenModel, _core.Scheduler);
            else
            {
                // color
                PlayerUtils.Color(Player, new Color(0, 0, 255, 255), _core.Scheduler);
            }
        }
        else
        {
            Role = JBRole.None;
            _iconManager.DespawnCoin();

            var key  = offReason != null ? $"warden_removed.{offReason}" : "warden_removed";
            var args = killerName != null
                ? (object[])[Player.Name, killerName]
                : (object[])[Player.Name];
             if (!silent)
             {
                _core.PlayerManager.SendMessage(MessageType.Chat, _core.Localizer["prefix"] + _core.Localizer[key, args]);
                _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["no_warden_alert"]);
            }
            SyncTeam();
            if (Team == JBTeam.Guard)
            {
                CanBecomeWarden = true;
                var guardModel = PlayerUtils.PickRandomModel(_modelsConfig.GuardModels);
                if (!string.IsNullOrEmpty(guardModel))
                    PlayerUtils.SetModel(Player, guardModel, _core.Scheduler);
                else
                {
                    // color
                    PlayerUtils.Color(Player, new Color(255, 255, 255, 255), _core.Scheduler);
                }
            }
            else if (Team == JBTeam.Prisoner)
            {
                CanBecomeWarden = false;
                var prisonerModel = PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels);
                if (!string.IsNullOrEmpty(prisonerModel))
                    PlayerUtils.SetModel(Player, prisonerModel, _core.Scheduler);
                else
                {
                    // color
                    PlayerUtils.Color(Player, new Color(255, 255, 255, 255), _core.Scheduler);
                }
            }
        }
    }

    public void SetDeputy(bool state, string? offReason = null)
    {
        if (state)
        {
            Role = JBRole.Deputy;

            if (!string.IsNullOrEmpty(_modelsConfig.DeputyModel))
                PlayerUtils.SetModel(Player, _modelsConfig.DeputyModel, _core.Scheduler);
        }
        else
        {
            if (Role == JBRole.Deputy) Role = JBRole.None;

            SyncTeam();
            if (Team == JBTeam.Guard)
            {
                CanBecomeWarden = true;
                var guardModel = PlayerUtils.PickRandomModel(_modelsConfig.GuardModels);
                if (!string.IsNullOrEmpty(guardModel))
                    PlayerUtils.SetModel(Player, guardModel, _core.Scheduler);
            }
            else if (Team == JBTeam.Prisoner)
            {
                CanBecomeWarden = false;
                var prisonerModel = PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels);
                if (!string.IsNullOrEmpty(prisonerModel))
                    PlayerUtils.SetModel(Player, prisonerModel, _core.Scheduler);
            }
        }
    }

    public void SetRebel(bool state, string? offReason = null)
    {
        if (state)
        {
            Role = JBRole.Rebel;
            PlayerUtils.Color(Player, new Color(255, 0, 0, 255), _core.Scheduler);
        }
        else
        {
            if (Role == JBRole.Rebel) Role = JBRole.None;
            PlayerUtils.Color(Player, new Color(255, 255, 255, 255), _core.Scheduler);
        }
    }

    public void SetFreeday(bool state, string? offReason = null)
    {
        if (state)
        {
            Role = JBRole.Freeday;

            if (!string.IsNullOrEmpty(_modelsConfig.FreedayModel))
                PlayerUtils.SetModel(Player, _modelsConfig.FreedayModel, _core.Scheduler);
            else
            {
                // color
                PlayerUtils.Color(Player, new Color(0, 255, 0, 255), _core.Scheduler);
            }
        }
        else
        {
            if (Role == JBRole.Freeday) Role = JBRole.None;

            if (_modelsConfig.PrisonerModels.Any())
            {
                var prisonerModel = PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels);
                if (!string.IsNullOrEmpty(prisonerModel))
                    PlayerUtils.SetModel(Player, prisonerModel, _core.Scheduler);
                else
                {
                    // color
                    PlayerUtils.Color(Player, new Color(255, 255, 255, 255), _core.Scheduler);
                }
            }
            else
            {
                // color
                PlayerUtils.Color(Player, new Color(255, 255, 255, 255), _core.Scheduler);
            }
        }
    }

    public void Mute()
    {
        Player.VoiceFlags = VoiceFlagValue.Muted;
        IsMuted = true;
    }
    public void Unmute()
    {
        Player.VoiceFlags = VoiceFlagValue.Normal;
        IsMuted = false;
    }

    // ── Team sync ─────────────────────────────────────────────────────────────

    public void SyncTeam()
    {
        if (!Player.IsValid)
        {
            Team = JBTeam.None;
            return;
        }

        Team = Player.Controller.TeamNum switch
        {
            2 => JBTeam.Prisoner,
            3 => JBTeam.Guard,
            _ => JBTeam.None
        };
    }

    // ── Messaging ─────────────────────────────────────────────────────────────

    public void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args)
    {
        if (!Player.IsValid)
            return;

        var localizer = Localizer;
        var message   = args.Length > 0 ? localizer[key, args] : localizer[key];

        if (prefix)
            Player.SendMessage(type, localizer["prefix"] + message, durationMs);
        else
            Player.SendMessage(type, message, durationMs);
    }
}
