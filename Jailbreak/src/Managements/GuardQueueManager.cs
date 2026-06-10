using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace Jailbreak;

public sealed class GuardQueueManager
{
    private readonly ISwiftlyCore _core;
    private readonly IJBPlayerManagement _players;
    private readonly TeamManager _teamManager;
    private readonly GuardQueueConfig _config;
    private readonly List<QueueEntry> _queue = [];
    private Guid? _playerTeamHookId;
    private Guid? _playerDisconnectHookId;
    private long _sequence;

    public GuardQueueManager(
        ISwiftlyCore core,
        IJBPlayerManagement players,
        TeamManager teamManager,
        IOptions<GuardQueueConfig> config)
    {
        _core = core;
        _players = players;
        _teamManager = teamManager;
        _config = config.Value;
    }

    public void Register()
    {
        if (!_config.Enable)
            return;

        foreach (var command in _config.Commands.Queue)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, QueueCommand);
        }

        foreach (var command in _config.Commands.Unqueue)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, UnqueueCommand);
        }

        foreach (var command in _config.Commands.QueueList)
        {
            if (!_core.Command.IsCommandRegistered(command))
                _core.Command.RegisterCommand(command, QueueListCommand);
        }

        _playerTeamHookId = _core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam);
        _playerDisconnectHookId = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    public void Unregister()
    {
        foreach (var command in _config.Commands.Queue)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        foreach (var command in _config.Commands.Unqueue)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        foreach (var command in _config.Commands.QueueList)
        {
            if (_core.Command.IsCommandRegistered(command))
                _core.Command.UnregisterCommand(command);
        }

        Unhook(ref _playerTeamHookId);
        Unhook(ref _playerDisconnectHookId);
        _queue.Clear();
    }

    private void QueueCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (player.Team == JBTeam.Guard)
        {
            player.SendMessage(MessageType.Chat, "guard_queue_already_guard", true);
            return;
        }

        if (player.Team != JBTeam.Prisoner)
        {
            player.SendMessage(MessageType.Chat, "guard_queue_only_prisoner", true);
            return;
        }

        if (_teamManager.MoveToGuard(player))
        {
            RemoveFromQueue(player);
            player.SendMessage(MessageType.Chat, "guard_queue_moved_direct", true);
            return;
        }

        if (TryGetEntry(player, out var existingEntry))
        {
            player.SendMessage(MessageType.Chat, "guard_queue_already_queued", true, args: GetPosition(existingEntry));
            return;
        }

        var entry = new QueueEntry(
            PlayerIdentity.GetKey(player.Player),
            player.SteamID,
            player.Player.Name,
            IsPremium(player),
            ++_sequence);

        AddEntry(entry);
        player.SendMessage(MessageType.Chat, "guard_queue_joined", true, args: GetPosition(entry));
    }

    private void UnqueueCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        if (RemoveFromQueue(player))
        {
            player.SendMessage(MessageType.Chat, "guard_queue_left", true);
            return;
        }

        player.SendMessage(MessageType.Chat, "guard_queue_not_queued", true);
    }

    private void QueueListCommand(ICommandContext ctx)
    {
        if (ctx.Sender == null)
            return;

        var player = _players.SyncPlayer(ctx.Sender);
        if (player == null)
            return;

        PruneQueue();

        var targets = GetListTargets().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Contains("chat"))
            SendChatQueueList(player);

        if (targets.Contains("html"))
            SendHtmlQueueList(player);
    }

    private HookResult OnPlayerTeam(EventPlayerTeam e)
    {
        if (e.UserIdPlayer == null)
            return HookResult.Continue;

        var playerKey = PlayerIdentity.GetKey(e.UserIdPlayer);
        if (e.Team != (byte)Team.T)
            RemoveFromQueue(playerKey);

        _core.Scheduler.NextWorldUpdate(TryPromoteNext);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e)
    {
        if (e.UserIdPlayer != null)
            RemoveFromQueue(PlayerIdentity.GetKey(e.UserIdPlayer));

        _core.Scheduler.NextWorldUpdate(TryPromoteNext);
        return HookResult.Continue;
    }

    private void TryPromoteNext()
    {
        PruneQueue();

        foreach (var entry in _queue.ToList())
        {
            var player = FindPlayer(entry);
            if (player == null || player.Team != JBTeam.Prisoner)
            {
                _queue.Remove(entry);
                continue;
            }

            if (!_teamManager.MoveToGuard(player))
                return;

            _queue.Remove(entry);
            player.SendMessage(MessageType.Chat, "guard_queue_promoted", true);
            return;
        }
    }

    private void SendChatQueueList(IJBPlayer player)
    {
        player.SendMessage(MessageType.Chat, "guard_queue_list_chat.header", false);

        if (_queue.Count == 0)
        {
            player.SendMessage(MessageType.Chat, "guard_queue_list_chat.empty", false);
        }
        else
        {
            for (var i = 0; i < _queue.Count; i++)
            {
                var entry = _queue[i];
                var key = entry.Premium
                    ? "guard_queue_list_chat.row_premium"
                    : "guard_queue_list_chat.row";

                player.SendMessage(MessageType.Chat, key, false, args: [i + 1, GetDisplayName(entry)]);
            }
        }

        player.SendMessage(MessageType.Chat, "guard_queue_list_chat.footer", false);
    }

    private void SendHtmlQueueList(IJBPlayer player)
    {
        var lines = new List<string>
        {
            player.Localizer["guard_queue_list_html.header"]
        };

        if (_queue.Count == 0)
        {
            lines.Add(player.Localizer["guard_queue_list_html.empty"]);
        }
        else
        {
            for (var i = 0; i < _queue.Count; i++)
            {
                var entry = _queue[i];
                var key = entry.Premium
                    ? "guard_queue_list_html.row_premium"
                    : "guard_queue_list_html.row";

                lines.Add(player.Localizer[key, i + 1, GetDisplayName(entry)]);
            }
        }

        lines.Add(player.Localizer["guard_queue_list_html.footer"]);
        player.Player.SendCenterHTML(string.Join("<br>", lines), 8000);
    }

    private IEnumerable<string> GetListTargets()
    {
        var configured = _config.ShowListTo
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => _config.ShowListToOptions.Contains(target, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return configured.Count > 0 ? configured : ["html"];
    }

    private void PruneQueue()
    {
        foreach (var entry in _queue.ToList())
        {
            var player = FindPlayer(entry);
            if (player == null || player.Team != JBTeam.Prisoner)
                _queue.Remove(entry);
        }
    }

    private void AddEntry(QueueEntry entry)
    {
        if (!entry.Premium)
        {
            _queue.Add(entry);
            return;
        }

        var index = _queue.FindIndex(existing => !existing.Premium);
        if (index < 0)
            _queue.Add(entry);
        else
            _queue.Insert(index, entry);
    }

    private bool TryGetEntry(IJBPlayer player, out QueueEntry entry)
    {
        var playerKey = PlayerIdentity.GetKey(player.Player);
        entry = _queue.FirstOrDefault(queued => queued.PlayerKey == playerKey)!;
        return entry != null;
    }

    private bool RemoveFromQueue(IJBPlayer player)
    {
        return RemoveFromQueue(PlayerIdentity.GetKey(player.Player));
    }

    private bool RemoveFromQueue(ulong playerKey)
    {
        var removed = false;
        foreach (var entry in _queue.Where(entry => entry.PlayerKey == playerKey).ToList())
            removed |= _queue.Remove(entry);

        return removed;
    }

    private int GetPosition(QueueEntry entry)
    {
        var index = _queue.FindIndex(queued => queued.PlayerKey == entry.PlayerKey);
        return index < 0 ? 0 : index + 1;
    }

    private bool IsPremium(IJBPlayer player)
    {
        return player.SteamID != 0
            && _config.PremiumPermissions.Count > 0
            && _core.Permission.PlayerHasPermissions(player.SteamID, _config.PremiumPermissions);
    }

    private IJBPlayer? FindPlayer(QueueEntry entry)
    {
        return _players.GetAllPlayers().FirstOrDefault(player => PlayerIdentity.GetKey(player.Player) == entry.PlayerKey);
    }

    private string GetDisplayName(QueueEntry entry)
    {
        return FindPlayer(entry)?.Player.Name ?? entry.Name;
    }

    private void Unhook(ref Guid? hookId)
    {
        if (!hookId.HasValue)
            return;

        _core.GameEvent.Unhook(hookId.Value);
        hookId = null;
    }

    private sealed record QueueEntry(ulong PlayerKey, ulong SteamId, string Name, bool Premium, long Sequence);
}
