using SwiftlyS2.Shared.Helpers;

namespace Jailbreak.Contract;

public static class LastRequestWeapons
{
    public static IReadOnlySet<ItemDefinitionIndex> Knives => SpecialDayWeapons.Knives;
    public static IReadOnlySet<ItemDefinitionIndex> Pistols => SpecialDayWeapons.Pistols;
    public static IReadOnlySet<ItemDefinitionIndex> Rifles => SpecialDayWeapons.Rifles;
    public static IReadOnlySet<ItemDefinitionIndex> Smgs => SpecialDayWeapons.Smgs;
    public static IReadOnlySet<ItemDefinitionIndex> Snipers => SpecialDayWeapons.Snipers;
    public static IReadOnlySet<ItemDefinitionIndex> Heavy => SpecialDayWeapons.Heavy;
    public static IReadOnlySet<ItemDefinitionIndex> PrimaryWeapons => SpecialDayWeapons.PrimaryWeapons;
    public static IReadOnlySet<ItemDefinitionIndex> SecondaryWeapons => SpecialDayWeapons.SecondaryWeapons;
    public static IReadOnlySet<ItemDefinitionIndex> GunsMenuWeapons => SpecialDayWeapons.GunsMenuWeapons;
    public static IReadOnlySet<ItemDefinitionIndex> AllKnives => SpecialDayWeapons.AllKnives;
}
