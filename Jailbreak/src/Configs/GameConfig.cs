using System.Text;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;

namespace Jailbreak;

public sealed class GameConfig
{
    private const string FileName = "jailbreak.cfg";

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

    public GameConfig(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Register(bool hotReload)
    {
        EnsureConfigFile();
        _core.Event.OnMapLoad += OnMapLoad;

        if (hotReload)
            Apply();
    }

    public void Unregister()
    {
        _core.Event.OnMapLoad -= OnMapLoad;
    }

    public void Apply()
    {
        _core.Engine.ExecuteCommand("sv_cheats 1");
        foreach (var command in ServerCommands)
            Execute(command);

        foreach (var command in MovementCommands)
            Execute(command);
        _core.Engine.ExecuteCommand("sv_cheats 1");
    }

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        Apply();
    }

    private void EnsureConfigFile()
    {
        Directory.CreateDirectory(_core.PluginPath);

        var path = Path.Combine(_core.PluginPath, FileName);
        if (File.Exists(path))
            return;

        File.WriteAllText(path, BuildConfigText());
    }

    private static string BuildConfigText()
    {
        var builder = new StringBuilder();

        foreach (var command in ServerCommands)
            builder.AppendLine(command.ToString());

        builder.AppendLine();
        builder.AppendLine("// Movement config");

        foreach (var command in MovementCommands)
            builder.AppendLine(command.ToString());

        return builder.ToString();
    }

    private void Execute(GameConfigEntry command)
    {
        _core.Engine.ExecuteCommand(command.ToString());
    }

    private sealed record GameConfigEntry(string Key, string Value)
    {
        public override string ToString()
        {
            return $"{Key} {Value}";
        }
    }
}
