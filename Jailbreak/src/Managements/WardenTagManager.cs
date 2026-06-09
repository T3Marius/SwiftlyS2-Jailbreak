using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace Jailbreak;

public sealed class WardenTagManager
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly WardenConfig _config;
    private readonly Dictionary<ulong, string> _originalClans = [];

    private static int _scoreRefreshScheduled;

    private Guid? _sayTextHookId;
    private DateTime _nextScoreboardRefreshUtc = DateTime.MinValue;

    public WardenTagManager(ISwiftlyCore core, IJBPlayerManagement players, IOptions<WardenConfig> config)
    {
        _core = core;
        _players = players;
        _config = config.Value;
    }

    public void Register()
    {
        if (!_config.Tag.Enable)
            return;

        _sayTextHookId = _core.NetMessage.HookServerMessage<CUserMessageSayText2>(OnSayText2);
        _core.Event.OnTick += OnTick;

        _core.Scheduler.NextWorldUpdate(RefreshScoreboardTags);
    }

    public void Unregister()
    {
        if (_sayTextHookId.HasValue)
        {
            _core.NetMessage.Unhook(_sayTextHookId.Value);
            _sayTextHookId = null;
        }

        _core.Event.OnTick -= OnTick;
        RestoreTrackedClans();
    }

    public void RefreshNow()
    {
        if (!_config.Tag.Enable)
            return;

        _core.Scheduler.NextWorldUpdate(RefreshScoreboardTags);
    }

    private HookResult OnSayText2(CUserMessageSayText2 msg)
    {
        if (!_config.Tag.Enable || string.IsNullOrEmpty(msg.Param1))
            return HookResult.Continue;

        var rawPlayer = _core.PlayerManager.GetPlayer(msg.Entityindex - 1);
        if (rawPlayer == null)
            return HookResult.Continue;

        var player = _players.SyncPlayer(rawPlayer);
        if (player == null || !player.IsWarden)
            return HookResult.Continue;

        var chatTag = _config.Tag.Chat.Colored();
        if (string.IsNullOrWhiteSpace(chatTag) || msg.Messagename.Contains(chatTag, StringComparison.Ordinal))
            return HookResult.Continue;

        var channelTag = GetChatChannelTag(msg);
        var nameColor = _config.Tag.NameColor.Colored();
        msg.Messagename = $" \x01{channelTag} {chatTag}{nameColor}{msg.Param1}\x01: {msg.Param2}";
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!_config.Tag.Enable)
            return;

        var now = DateTime.UtcNow;
        if (now < _nextScoreboardRefreshUtc)
            return;

        _nextScoreboardRefreshUtc = now.AddSeconds(Math.Max(0.1f, _config.Tag.ScoreboardRefreshSeconds));
        RefreshScoreboardTags();
    }

    private void RefreshScoreboardTags()
    {
        if (!_config.Tag.Enable)
            return;

        var liveKeys = new HashSet<ulong>();
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid)
                continue;

            var playerKey = PlayerIdentity.GetKey(player.Player);
            liveKeys.Add(playerKey);

            if (player.IsWarden)
            {
                ApplyWardenClan(player, playerKey);
                continue;
            }

            RestoreClanIfTracked(player, playerKey);
        }

        foreach (var staleKey in _originalClans.Keys.Where(key => !liveKeys.Contains(key)).ToList())
            _originalClans.Remove(staleKey);
    }

    private void ApplyWardenClan(IJBPlayer player, ulong playerKey)
    {
        var controller = player.Player.Controller;
        if (controller == null)
            return;

        if (!_originalClans.ContainsKey(playerKey))
            _originalClans[playerKey] = controller.Clan ?? string.Empty;

        var scoreboardTag = _config.Tag.Scoreboard;
        if (controller.Clan == scoreboardTag)
            return;

        SetScoreTag(player.Player, scoreboardTag);
    }

    private void RestoreClanIfTracked(IJBPlayer player, ulong playerKey)
    {
        if (!_originalClans.TryGetValue(playerKey, out var originalClan))
            return;

        var controller = player.Player.Controller;
        if (controller == null)
        {
            _originalClans.Remove(playerKey);
            return;
        }

        if (controller.Clan != originalClan)
        {
            SetScoreTag(player.Player, originalClan);
        }

        _originalClans.Remove(playerKey);
    }

    private void RestoreTrackedClans()
    {
        foreach (var player in _players.GetAllPlayers())
        {
            if (!player.Player.IsValid)
                continue;

            RestoreClanIfTracked(player, PlayerIdentity.GetKey(player.Player));
        }

        _originalClans.Clear();
    }

    private void SetScoreTag(IPlayer player, string? tag)
    {
        if (!player.IsValid)
            return;

        var normalized = tag ?? string.Empty;
        if (player.Controller.Clan != normalized)
            player.Controller.Clan = normalized;

        player.Controller.ClanUpdated();
        FireScoreTagRefreshEvent();
    }

    private void FireScoreTagRefreshEvent()
    {
        if (Interlocked.Exchange(ref _scoreRefreshScheduled, 1) == 1)
            return;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            Interlocked.Exchange(ref _scoreRefreshScheduled, 0);
            _core.GameEvent.Fire<EventNextlevelChanged>();
        });
    }

    private static string GetChatChannelTag(CUserMessageSayText2 msg)
    {
        return msg.Messagename.Contains("All", StringComparison.OrdinalIgnoreCase)
            ? "[ALL]"
            : "[CT]";
    }

}
