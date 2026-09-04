using HudText.Contract;
using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using System.Globalization;
using System.Text;

namespace Jailbreak;

public sealed class CurrencyHudManager
{
    private readonly ISwiftlyCore _core;
    private readonly IJBShop _shop;
    private readonly IJBPlayerManagement _players;
    private readonly HudConfig _hudConfig;
    private readonly ILogger<CurrencyHudManager> _log;
    private readonly Dictionary<int, HudTextHandle> _huds = [];
    private readonly Dictionary<int, Dictionary<string, decimal>> _playerBalances = [];

    private IHudTextService? _hudText;
    private bool _registered;
    private Guid? _spawnHookId;
    private bool _refreshQueued;

    public CurrencyHudManager(
        ISwiftlyCore core,
        IJBShop shop,
        IJBPlayerManagement players,
        IOptions<HudConfig> hudConfig,
        ILogger<CurrencyHudManager> log)
    {
        _core = core;
        _shop = shop;
        _players = players;
        _hudConfig = hudConfig.Value;
        _log = log;
    }

    public void Register()
    {
        if (_registered)
            return;

        _registered = true;
        _shop.PlayerCurrencyChanged += OnPlayerCurrencyChanged;
        _core.Event.OnClientDisconnected += OnClientDisconnected;
        _core.Event.OnClientPutInServer += OnClientPutInServer;
        _spawnHookId = _core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        if (_shop is ShopManager shopManager)
            shopManager.CurrencyAvailabilityChanged += QueueRefresh;
        QueueRefresh();
    }

    public void Unregister()
    {
        _registered = false;
        if (_spawnHookId is { } hookId)
        {
            _core.GameEvent.Unhook(hookId);
            _spawnHookId = null;
        }
        if (_shop is ShopManager shopManager)
            shopManager.CurrencyAvailabilityChanged -= QueueRefresh;
        _shop.PlayerCurrencyChanged -= OnPlayerCurrencyChanged;
        _core.Event.OnClientDisconnected -= OnClientDisconnected;
        _core.Event.OnClientPutInServer -= OnClientPutInServer;

        if (_hudText != null)
        {
            foreach (var hud in _huds.Values)
                RemoveHudSafely(hud);
        }

        _huds.Clear();
        _playerBalances.Clear();
        _hudText = null;
    }

    public void SetHudTextService(IHudTextService? hudText)
    {
        if (ReferenceEquals(_hudText, hudText))
            return;

        if (_hudText != null)
        {
            foreach (var hud in _huds.Values)
                RemoveHudSafely(hud);
        }

        _huds.Clear();
        _hudText = hudText;

        if (_hudText == null)
            return;

        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!_registered || _refreshQueued)
            return;

        _refreshQueued = true;
        _core.Scheduler.NextWorldUpdate(() =>
        {
            _refreshQueued = false;
            if (!_registered || _hudText == null)
                return;

            foreach (var player in _players.GetAllPlayers())
                RefreshAllCurrencies(player);
        });
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        if (@event.UserIdPlayer is { IsValid: true } player)
            QueuePlayerRefresh(player);

        return HookResult.Continue;
    }

    private void OnPlayerCurrencyChanged(IJBPlayer player, string currency, decimal balance)
    {
        if (!player.Player.IsValid || string.IsNullOrWhiteSpace(currency))
            return;

        var playerId = player.Player.PlayerID;

        if (!_playerBalances.TryGetValue(playerId, out var balances))
        {
            balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            _playerBalances[playerId] = balances;
        }

        balances[currency] = balance;
        RenderHud(player, balances);
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent @event)
    {
        if (_hudText is null)
            return;

        var playerId = @event.PlayerId;
        var joiningPlayer = _core.PlayerManager.GetPlayer(playerId);
        if (joiningPlayer is not { IsValid: true })
            return;

        QueuePlayerRefresh(joiningPlayer);
    }

    private void QueuePlayerRefresh(IPlayer joiningPlayer)
    {
        var playerId = joiningPlayer.PlayerID;
        var sessionId = joiningPlayer.SessionId;
        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (!_registered || _hudText == null)
                return;

            var currentPlayer = _core.PlayerManager.GetPlayer(playerId);
            if (currentPlayer is not { IsValid: true } || currentPlayer.SessionId != sessionId)
                return;

            var player = _players.SyncPlayer(currentPlayer);
            if (player != null)
                RefreshAllCurrencies(player);
        });
    }

    private void RefreshAllCurrencies(IJBPlayer player)
    {
        if (!player.Player.IsValid || player.Player.IsFakeClient)
            return;

        var playerId = player.Player.PlayerID;
        var balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var currency in _shop.Currencies)
            balances[currency] = _shop.GetBalance(player, currency);

        _playerBalances[playerId] = balances;
        RenderHud(player, balances);
    }

    private void RenderHud(IJBPlayer player, Dictionary<string, decimal> balances)
    {
        try
        {
            RenderHudCore(player, balances);
        }
        catch (Exception ex)
        {
            // Optional presentation must not interrupt the shop after a balance change.
            _log.LogError(ex, "Failed to render currency HUD for player {SteamId}.", player.SteamID);
        }
    }

    private void RenderHudCore(IJBPlayer player, Dictionary<string, decimal> balances)
    {
        if (_hudText is null || !player.Player.IsValid)
            return;

        var playerId = player.Player.PlayerID;
        var message = FormatMessage(player, balances);

        if (_huds.TryGetValue(playerId, out var hud))
        {
            try
            {
                _hudText.UpdateHud(hud, message);
                _hudText.ShowHud(hud);
                return;
            }
            catch (ArgumentException)
            {
                _huds.Remove(playerId);
            }
        }

        _huds[playerId] = _hudText.CreateHud(playerId, message, ToHudTextOptions(_hudConfig.CurrencyHud));
    }

    private string FormatMessage(IJBPlayer player, Dictionary<string, decimal> balances)
    {
        var sb = new StringBuilder();
        var localizer = player.Localizer;
        sb.Append(localizer["currency_hud_header"]);

        foreach (var currency in _shop.Currencies)
        {
            if (string.IsNullOrWhiteSpace(currency))
                continue;

            var amount = balances.GetValueOrDefault(currency, 0);

            var formattedCurrency =
                char.ToUpper(currency[0]) + currency.Substring(1).ToLower();
            var formattedAmount = amount.ToString("0.##", CultureInfo.InvariantCulture);

            sb.Append('\n');
            sb.Append(localizer[
                "currency_hud_line",
                formattedCurrency,
                formattedAmount
            ]);
        }

        return sb.ToString();
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        _playerBalances.Remove(@event.PlayerId);

        if (_hudText != null && _huds.Remove(@event.PlayerId, out var hud))
            RemoveHudSafely(hud);
        else
            _huds.Remove(@event.PlayerId);
    }

    private void RemoveHudSafely(HudTextHandle hud)
    {
        try
        {
            _hudText!.RemoveHud(hud);
        }
        catch (ArgumentException)
        {
            // Already discarded by HudText during a map change or disconnect.
        }
    }

    private static HudTextOptions ToHudTextOptions(HudTextSettings style) => new()
    {
        Position = style.Position,
        Color = style.Color,
        Size = style.Size,
        Background = style.Background,
        BackgroundOpacity = style.BackgroundOpacity,
        DropShadow = style.DropShadow,
        OutlineColor = style.OutlineColor,
        Font = style.Font,
        FontWeight = style.FontWeight,
        TextAlignment = style.TextAlignment,
        MarginBottom = style.MarginBottom,
        MarginTop = style.MarginTop,
        MarginLeft = style.MarginLeft,
        MarginRight = style.MarginRight,
        VerticalAlignment = style.VerticalAlignment,
    };
}
