namespace Jailbreak;

public sealed class ModelsConfig
{
    public string FreedayModel { get; set; } = string.Empty;
    public string DeputyModel { get; set; } = string.Empty;
    public string WardenModel { get; set; } = string.Empty;
    public List<string> GuardModels { get; set; } = [
        "agents/models/sunucukur/guards/g_variant_a/g_variant_a.vmdl",
        "agents/models/sunucukur/guards/g_variant_b/g_variant_b.vmdl",
        "agents/models/sunucukur/guards/g_variant_c/g_variant_c.vmdl",
        "agents/models/sunucukur/guards/g_variant_d/g_variant_d.vmdl",
        "agents/models/sunucukur/guards/g_variant_e/g_variant_e.vmdl",
    ];
    public List<string> PrisonerModels { get; set; } = [
        "agents/models/sunucukur/prisoner/p_variant_a/p_variant_a.vmdl",
        "agents/models/sunucukur/prisoner/p_variant_b/p_variant_b.vmdl",
        "agents/models/sunucukur/prisoner/p_variant_c/p_variant_c.vmdl",
        "agents/models/sunucukur/prisoner/p_variant_d/p_variant_d.vmdl",
        "agents/models/sunucukur/prisoner/p_variant_e/p_variant_e.vmdl",
        "agents/models/sunucukur/prisoner/p_variant_f/p_variant_f.vmdl",
    ];
}