using HudText.Contract;
using Jailbreak.Contract;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
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
        _shop.PlayerCurrencyChanged += OnPlayerCurrencyChanged;
        _core.Event.OnClientDisconnected += OnClientDisconnected;
        _core.Event.OnClientPutInServer += OnClientPutInServer;
    }

    public void Unregister()
    {
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

        foreach (var player in _players.GetAllPlayers())
            RefreshAllCurrencies(player);
    }

    private void OnPlayerCurrencyChanged(IJBPlayer player, string currency, decimal balance)
    {
        if (!player.Player.IsValid)
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

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var player = _players.SyncPlayer(_core.PlayerManager.GetPlayer(@event.PlayerId)!);
            if (player != null)
                RefreshAllCurrencies(player);
        });
    }

    private void RefreshAllCurrencies(IJBPlayer player)
    {
        if (!player.Player.IsValid)
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
        sb.Append(player.Localizer["currency_hud_header"]);

        foreach (var currency in _shop.Currencies)
        {
            var amount = balances.GetValueOrDefault(currency, 0);

            var formattedCurrency =
                char.ToUpper(currency[0]) + currency.Substring(1).ToLower();

            sb.Append('\n');
            sb.Append(player.Localizer[
                "currency_hud_line",
                formattedCurrency,
                amount
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
    };
}