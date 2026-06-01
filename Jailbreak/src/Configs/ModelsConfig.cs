namespace Jailbreak;

public sealed class ModelsConfig
{
    public string FreedayModel { get; set; } = string.Empty;
    public string DeputyModel { get; set; } = string.Empty;
    public string WardenModel { get; set; } = string.Empty;
    public List<string> GuardModels { get; set; } = [];
    public List<string> PrisonerModels { get; set; } = [];
}