using System.Text;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;

namespace Jailbreak;

public sealed class GameConfig
{
    private const string FileName = "jailbreak.cfg";

    private static readonly GameConfigEntry CheatsCommand = new("sv_cheats", "1");

    private static readonly GameConfigEntry[] ServerCommands =
    [
        new("mp_timelimit", "25"),
        new("mp_roundtime", "8"),
        new("mp_autoteambalance", "0"),
        new("mp_t_default_primary", "\"\""),
        new("mp_t_default_secondary", "\"\""),
        new("mp_t_default_melee", "weapon_knife"),
        new("player_ping_token_cooldown", "0"),
        new("mp_solid_teammates", "2"),
        new("mp_autokick", "0"),
        new("mp_warmuptime", "0"),
        new("mp_maxmoney", "0"),
        new("mp_teamcashawards", "0"),
        new("mp_playercashawards", "0"),
        new("sv_disable_radar", "1"),
        new("sv_ignoregrenaderadio", "1"),
        new("sv_deadtalk", "1"),
        new("sv_alltalk", "1")
    ];

    private static readonly GameConfigEntry[] MovementCommands =
    [
        new("sv_airaccelerate", "280"),
        new("sv_staminamax", "0"),
        new("sv_staminalandcost", "0"),
        new("sv_staminajumpcost", "0")
    ];

    private readonly ISwiftlyCore _core;
    private readonly ILogger<GameConfig> _log;

    public GameConfig(ISwiftlyCore core, ILogger<GameConfig> log)
    {
        _core = core;
        _log = log;
    }

    public void Register(bool hotReload)
    {
        EnsureConfigFile();
        _core.Event.OnMapLoad += OnMapLoad;
        _core.Event.OnClientConnected += OnClientConnected;

        _log.LogInformation("Game config registered. HotReload={HotReload}", hotReload);

        if (hotReload)
            Apply("hot reload");
    }

    public void Unregister()
    {
        _core.Event.OnMapLoad -= OnMapLoad;
        _core.Event.OnClientConnected -= OnClientConnected;
        _log.LogInformation("Game config unregistered.");
    }

    public void Apply(string reason = "manual")
    {
        _log.LogInformation("Applying jailbreak game config. Reason={Reason}", reason);

        Execute(CheatsCommand);

        foreach (var command in ServerCommands)
            Execute(command);

        foreach (var command in MovementCommands)
            Execute(command);

        Execute(CheatsCommand);

        _log.LogInformation("Applied jailbreak game config. Reason={Reason}, Commands={CommandCount}", reason, ServerCommands.Length + MovementCommands.Length + 2);
    }

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        _log.LogInformation("Map load detected. Map={MapName}", @event.MapName);
        Apply($"map load {@event.MapName}");
    }

    private void OnClientConnected(IOnClientConnectedEvent @event)
    {
        _log.LogInformation("Client connected. PlayerId={PlayerId}. Re-applying jailbreak game config.", @event.PlayerId);
        Apply($"client connect {@event.PlayerId}");
    }

    private void EnsureConfigFile()
    {
        Directory.CreateDirectory(_core.PluginPath);

        var path = Path.Combine(_core.PluginPath, FileName);
        var configText = BuildConfigText();
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) == configText)
            {
                _log.LogInformation("Game config file already exists. Path={Path}", path);
                return;
            }

            File.WriteAllText(path, configText);
            _log.LogInformation("Updated game config file. Path={Path}", path);
            return;
        }

        File.WriteAllText(path, configText);
        _log.LogInformation("Created game config file. Path={Path}", path);
    }

    private static string BuildConfigText()
    {
        var builder = new StringBuilder();

        builder.AppendLine(CheatsCommand.ToString());
        builder.AppendLine();

        foreach (var command in ServerCommands)
            builder.AppendLine(command.ToString());

        builder.AppendLine();
        builder.AppendLine("// Movement config");

        foreach (var command in MovementCommands)
            builder.AppendLine(command.ToString());

        builder.AppendLine(CheatsCommand.ToString());

        return builder.ToString();
    }

    private void Execute(GameConfigEntry command)
    {
        var commandText = command.ToString();
        _log.LogInformation("Executing game config command: {Command}", commandText);
        _core.Engine.ExecuteCommand(commandText);
    }

    private sealed record GameConfigEntry(string Key, string Value)
    {
        public override string ToString()
        {
            return $"{Key} {Value}";
        }
    }
}
