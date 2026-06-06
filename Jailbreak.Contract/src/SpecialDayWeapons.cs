using SwiftlyS2.Shared.Helpers;

namespace Jailbreak.Contract;

public static class SpecialDayWeapons
{
    public static IReadOnlySet<ItemDefinitionIndex> Knives { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Knifegg,
        ItemDefinitionIndex.Knife,
        ItemDefinitionIndex.KnifeT,
        ItemDefinitionIndex.Bayonet,
        ItemDefinitionIndex.KnifeCss,
        ItemDefinitionIndex.KnifeFlip,
        ItemDefinitionIndex.KnifeGut,
        ItemDefinitionIndex.KnifeKarambit,
        ItemDefinitionIndex.KnifeM9Bayonet,
        ItemDefinitionIndex.KnifeTactical,
        ItemDefinitionIndex.KnifeFalchion,
        ItemDefinitionIndex.KnifeSurvivalBowie,
        ItemDefinitionIndex.KnifeButterfly,
        ItemDefinitionIndex.KnifePush,
        ItemDefinitionIndex.KnifeCord,
        ItemDefinitionIndex.KnifeCanis,
        ItemDefinitionIndex.KnifeUrsus,
        ItemDefinitionIndex.KnifeGypsyJackknife,
        ItemDefinitionIndex.KnifeOutdoor,
        ItemDefinitionIndex.KnifeStiletto,
        ItemDefinitionIndex.KnifeWidowmaker,
        ItemDefinitionIndex.KnifeSkeleton,
        ItemDefinitionIndex.KnifeKukri
    };
    public static IReadOnlySet<ItemDefinitionIndex> Rifles { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Ak47,
        ItemDefinitionIndex.Aug,
        ItemDefinitionIndex.Famas,
        ItemDefinitionIndex.Sg556,
        ItemDefinitionIndex.Galilar,
        ItemDefinitionIndex.M4a1,
        ItemDefinitionIndex.M4a1Silencer
    };

    public static IReadOnlySet<ItemDefinitionIndex> Smgs { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Mac10,
        ItemDefinitionIndex.P90,
        ItemDefinitionIndex.Mp5sd,
        ItemDefinitionIndex.Ump45,
        ItemDefinitionIndex.Bizon,
        ItemDefinitionIndex.Mp7,
        ItemDefinitionIndex.Mp9
    };

    public static IReadOnlySet<ItemDefinitionIndex> Snipers { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Awp,
        ItemDefinitionIndex.G3sg1,
        ItemDefinitionIndex.Scar20,
        ItemDefinitionIndex.Ssg08
    };

    public static IReadOnlySet<ItemDefinitionIndex> Heavy { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.M249,
        ItemDefinitionIndex.Xm1014,
        ItemDefinitionIndex.Mag7,
        ItemDefinitionIndex.Negev,
        ItemDefinitionIndex.Sawedoff,
        ItemDefinitionIndex.Nova
    };

    public static IReadOnlySet<ItemDefinitionIndex> Pistols { get; } = new HashSet<ItemDefinitionIndex>
    {
        ItemDefinitionIndex.Deagle,
        ItemDefinitionIndex.Elite,
        ItemDefinitionIndex.FiveSeven,
        ItemDefinitionIndex.Glock,
        ItemDefinitionIndex.Tec9,
        ItemDefinitionIndex.HkP2000,
        ItemDefinitionIndex.UspSilencer,
        ItemDefinitionIndex.Cz75a,
        ItemDefinitionIndex.Revolver
    };

    public static IReadOnlySet<ItemDefinitionIndex> PrimaryWeapons { get; } = Rifles
        .Concat(Smgs)
        .Concat(Snipers)
        .Concat(Heavy)
        .ToHashSet();

    public static IReadOnlySet<ItemDefinitionIndex> SecondaryWeapons => Pistols;

    public static IReadOnlySet<ItemDefinitionIndex> GunsMenuWeapons { get; } = PrimaryWeapons
        .Concat(SecondaryWeapons)
        .ToHashSet();

    public static IReadOnlySet<ItemDefinitionIndex> AllKnives => Knives;
}
