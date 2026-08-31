// =============================================================
// RARITYTIER.CS — Noms et couleurs affichés de la rareté (r-2 à r+7)
// Path : Assets/Scripts/Data/Equipment/RarityTier.cs
// GDD v3.6 — §5.13
//
// Source unique pour WeaponInstance/ArmorInstance/TooltipSystem — le rang
// numérique (rarityRank) reste la clé de calcul (RarityBonus = rank*10%,
// GDD §5.13) mais n'est JAMAIS affiché au joueur : seuls le nom et la
// couleur le sont, le rang reste une référence de travail (tooltip/logs).
//
// Aethernelle n'est PAS un rang numéroté — c'est un sceau posé sur une
// pièce déjà r+7, obtenu via une Pierre dédiée (condition, pas de RNG).
// Aucun bonus supplémentaire : uniquement cosmétique/prestige. Le champ
// runtime (WeaponInstance/ArmorInstance) et le système d'obtention (Pierre,
// PNJ Forge) restent à construire — seuls le nom/couleur sont posés ici.
// =============================================================

public static class RarityTier
{
    public const string AethernelleName = "Aethernelle";
    public const string AethernelleColorHex = "#FFD24D"; // dégradé or->rose->argent en UI web, teinte de base plus vive ici

    // Couleurs volontairement saturées/lumineuses — le texte les porte seul en jeu
    // (TMP <color=#hex> plat, pas de glow/vignette comme dans le mockup web).
    private static readonly (string Name, string ColorHex)[] Tiers =
    {
        ("Cabossée",   "#A38B52"), // r-2
        ("Rafistolée", "#D6C4A8"), // r-1
        ("Commune",    "#F2F0EA"), // r0
        ("Simple",     "#3DE0C4"), // r+1
        ("Renforcée",  "#4CDB57"), // r+2
        ("Raffiné",    "#4D94FF"), // r+3
        ("Prodigieux", "#B583FF"), // r+4
        ("Sacrée",     "#FFD24D"), // r+5
        ("Ancestrale", "#E0455F"), // r+6
        ("Onirique",   "#F26DC4"), // r+7 — provisoire (plat, en attendant un vrai dégradé TMP)
    };

    private const int MinRank = -2;
    private const int MaxRank = 7;

    public static string GetName(int rarityRank)
    {
        int i = UnityEngine.Mathf.Clamp(rarityRank, MinRank, MaxRank) - MinRank;
        return Tiers[i].Name;
    }

    public static string GetColorHex(int rarityRank)
    {
        int i = UnityEngine.Mathf.Clamp(rarityRank, MinRank, MaxRank) - MinRank;
        return Tiers[i].ColorHex;
    }
}
