using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace Jailbreak;

public sealed class JBPlayer : IJBPlayer
{
    private IPlayer _player;
    private readonly ISwiftlyCore _core;
    private readonly ModelsConfig _modelsConfig;
    private readonly IconManager  _iconManager;

    public IPlayer Player => GetLivePlayer() ?? _player;
    public ulong SteamID { get; }

    public JBTeam Team { get; set; } = JBTeam.None;
    public JBRole Role { get; set; } = JBRole.None;

    public bool IsFreeday => Role == JBRole.Freeday;
    public bool IsRebel => Role == JBRole.Rebel;
    public bool IsDeputy => Role == JBRole.Deputy;
    public bool IsWarden => Role == JBRole.Warden;

    public bool IsCuffed { get; set; }
    public bool CanBecomeWarden { get; set; } = true;
    public bool IsMuted { get; set; }
    public bool WasUnmutedByWarden { get; set; }

    public ILocalizer Localizer => TryGetLivePlayer(out var livePlayer)
        ? _core.Translation.GetPlayerLocalizer(livePlayer)
        : _core.Localizer;

    public JBPlayer(IPlayer player, ISwiftlyCore core, IOptions<ModelsConfig> modelsConfig, IconManager iconManager)
    {
        _player = player;
        SteamID = player.SteamID;
        _core = core;
        _modelsConfig = modelsConfig.Value;
        _iconManager = iconManager;
    }

    internal void RefreshPlayer(IPlayer player)
    {
        _player = player;
    }

    public void SetWarden(bool state, string? offReason = null, string? killerName = null, bool silent = false)
    {
        if (state)
        {
            if (!TryGetLivePlayer(out var livePlayer))
                return;

            SyncTeam();
            if (Team != JBTeam.Guard)
            {
                CanBecomeWarden = false;
                return;
            }

            Role = JBRole.Warden;
            CanBecomeWarden = true;
            _iconManager.SpawnCoin(livePlayer);

            if (!silent)
                _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["new_warden_alert", livePlayer.Name]);

            if (!string.IsNullOrEmpty(_modelsConfig.WardenModel))
                PlayerUtils.SetModel(livePlayer, _modelsConfig.WardenModel, _core.Scheduler);
            else
                PlayerUtils.Color(livePlayer, new Color(0, 0, 255, 255), _core.Scheduler);

            return;
        }

        
        Role = JBRole.None;
        _iconManager.DespawnCoin();

        var playerName = TryGetLivePlayer(out var demotedPlayer) ? demotedPlayer.Name : SteamID.ToString();
        var key = offReason != null ? $"warden_removed.{offReason}" : "warden_removed";
        var args = killerName != null
            ? (object[])[playerName, killerName]
            : (object[])[playerName];

        if (!silent)
        {
            _core.PlayerManager.SendMessage(MessageType.Chat, _core.Localizer["prefix"] + _core.Localizer[key, args]);
            _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["no_warden_alert"]);
        }

        SyncTeam();
        ApplyTeamDefaults();
    }

    public void SetDeputy(bool state, string? offReason = null)
    {
        if (state)
        {
            if (!TryGetLivePlayer(out var livePlayer))
                return;

            SyncTeam();
            if (Team != JBTeam.Guard)
            {
                CanBecomeWarden = false;
                return;
            }

            Role = JBRole.Deputy;
            CanBecomeWarden = true;

            if (!string.IsNullOrEmpty(_modelsConfig.DeputyModel))
                PlayerUtils.SetModel(livePlayer, _modelsConfig.DeputyModel, _core.Scheduler);

            _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["new_deputy_alert", Player.Name]);

            return;
        }

        if (Role == JBRole.Deputy)
            Role = JBRole.None;
        
        _core.PlayerManager.SendMessage(MessageType.Alert, _core.Localizer["no_deputy_alert"]);

        SyncTeam();
        ApplyTeamDefaults();
    }

    public void SetRebel(bool state, string? offReason = null)
    {
        if (state)
        {
            Role = JBRole.Rebel;
            ColorIfLive(new Color(255, 0, 0, 255));
        }
        else
        {
            if (Role == JBRole.Rebel)
                Role = JBRole.None;

            ColorIfLive(new Color(255, 255, 255, 255));
        }
    }

    public void SetFreeday(bool state, string? offReason = null)
    {
        if (state)
        {
            Role = JBRole.Freeday;

            if (!string.IsNullOrEmpty(_modelsConfig.FreedayModel))
                SetModelIfLive(_modelsConfig.FreedayModel);
            else
                ColorIfLive(new Color(0, 255, 0, 255));

            return;
        }

        if (Role == JBRole.Freeday)
            Role = JBRole.None;

        var prisonerModel = PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels);
        if (!string.IsNullOrEmpty(prisonerModel))
            SetModelIfLive(prisonerModel);
        else
            ColorIfLive(new Color(255, 255, 255, 255));
    }

    public void Mute()
    {
        if (!TryGetLivePlayer(out var livePlayer))
            return;

        livePlayer.VoiceFlags = VoiceFlagValue.Muted;
        IsMuted = true;
    }

    public void Unmute()
    {
        if (!TryGetLivePlayer(out var livePlayer))
            return;

        livePlayer.VoiceFlags = VoiceFlagValue.Normal;
        IsMuted = false;
    }

    public void SyncTeam()
    {
        if (!TryGetLivePlayer(out var livePlayer))
        {
            Team = JBTeam.None;
            return;
        }

        Team = livePlayer.Controller.TeamNum switch
        {
            (byte)SwiftlyS2.Shared.Players.Team.T => JBTeam.Prisoner,
            (byte)SwiftlyS2.Shared.Players.Team.CT => JBTeam.Guard,
            _ => JBTeam.None
        };
    }

    public void SendMessage(MessageType type, string key, bool prefix = true, int durationMs = 5000, params object[] args)
    {
        if (!TryGetLivePlayer(out var livePlayer))
            return;

        var localizer = Localizer;
        var message = args.Length > 0 ? localizer[key, args] : localizer[key];

        livePlayer.SendMessage(type, prefix ? localizer["prefix"] + message : message, durationMs);
    }

    private void ApplyTeamDefaults()
    {
        CanBecomeWarden = Team == JBTeam.Guard;

        var model = Team switch
        {
            JBTeam.Guard => PlayerUtils.PickRandomModel(_modelsConfig.GuardModels),
            JBTeam.Prisoner => PlayerUtils.PickRandomModel(_modelsConfig.PrisonerModels),
            _ => null
        };

        if (!string.IsNullOrEmpty(model))
            SetModelIfLive(model);
        else if (Team is JBTeam.Guard or JBTeam.Prisoner)
            ColorIfLive(new Color(255, 255, 255, 255));
    }

    private IPlayer? GetLivePlayer()
    {
        try
        {
            if (_player.IsValid)
                return _player;

            var livePlayer = _core.PlayerManager.GetPlayerFromSteamId(SteamID);
            if (livePlayer is not { IsValid: true })
                return null;

            _player = livePlayer;
            return livePlayer;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private bool TryGetLivePlayer(out IPlayer livePlayer)
    {
        livePlayer = null!;

        var player = GetLivePlayer();
        if (player == null)
            return false;

        livePlayer = player;
        return true;
    }

    private void SetModelIfLive(string model)
    {
        if (TryGetLivePlayer(out var livePlayer))
            PlayerUtils.SetModel(livePlayer, model, _core.Scheduler);
    }

    private void ColorIfLive(Color color)
    {
        if (TryGetLivePlayer(out var livePlayer))
            PlayerUtils.Color(livePlayer, color, _core.Scheduler);
    }
}
