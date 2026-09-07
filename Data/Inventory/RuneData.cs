using UnityEngine;
using System.Collections.Generic;

// =============================================================
// RuneData — ScriptableObject template de rune
// Path : Assets/Scripts/Data/Inventory/Equipment/RuneData.cs
// AetherTree GDD v3.5 — §5.7
//
// Règles GDD §5.7 :
//   - 1 slot rune sur l'arme, 1 slot sur l'armure corps
//   - Rune Weapon → arme uniquement | Rune Armor → armure uniquement
//   - Identification obligatoire via l'Antiquaire avant insertion
//   - Insertion irréversible — extractible uniquement via item spécial (Antiquaire)
//   - runeLevel : aléatoire, minimum lv50
//   - weaponLevel (défini sur le SO arme/armure) = niveau maximum de rune équipable
//     Ex : arme lv63 → rune max lv63 (runeLevel ≤ weaponLevel)
//   - Rareté : r0 minimum → r+7 maximum
//   - Nombre de lignes déterminé par la combinaison runeLevel × rareté (tableau §5.7) :
//
//     Niveau \ Rareté   r0   r+2   r+4   r+6   r+7
//     lv50              1    2     3     5     6
//     lv65              2    3     4     6     7
//     lv80              3    4     5     7     8
//     lv95              4    5     6     8     10
//
//   - Valeurs de chaque ligne aléatoires dans une fourchette définie par niveau + rareté
//   - Maximum : 10 lignes (lv95 r+7)
//
// Assets > Create > AetherTree > Equipment > RuneData
// =============================================================

// ── Catégorie de rune ─────────────────────────────────────────
public enum RuneCategory { Weapon = 0, Armor = 1 }

// ── RuneStatEntry — une stat autorisée avec sa propre range ───
[System.Serializable]
public class RuneStatEntry
{
    [Tooltip("Type de stat apportée par cette ligne de rune.")]
    public StatType statType;

    [Tooltip("Valeur minimale rollée pour cette stat.")]
    public float valueMin = 5f;

    [Tooltip("Valeur maximale rollée pour cette stat.")]
    public float valueMax = 15f;
}

// =============================================================
// RuneData SO
// Hors scope actuel — refonte prévue, CreateAssetMenu retiré volontairement
// pour ne pas en créer par accident en attendant (voir note-rune-gem-hors-scope.md).
// =============================================================
public class RuneData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"rune_\"\n" +
             "(ex: \"rune_weapon_t2\"). Ne JAMAIS afficher au joueur — voir runeName pour l'affichage.")]
    public string       runeID;
    public string       runeName = "Rune";
    public Sprite       icon;

    [Header("Catégorie")]
    [Tooltip("Weapon = s'insère dans l'arme | Armor = s'insère dans l'armure corps.")]
    public RuneCategory runeCategory = RuneCategory.Weapon;

    [Header("Effets de statut (optionnels — appliqués à chaque attaque)")]
    [Tooltip("Effets appliqués à chaque attaque selon leur probabilité.\n" +
             "Glisse un DebuffData ou BuffData + règle la chance.\n" +
             "Ex: Freeze 2s à 3% | Poison 5s à 5%")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("Stats autorisées (tirées aléatoirement au drop)")]
    [Tooltip("Chaque entrée définit un StatType possible avec sa propre range de valeur.\n" +
             "Les lignes sont tirées aléatoirement (sans remise) parmi cette liste.\n" +
             "Doit contenir au moins 10 entrées pour couvrir le cas lv95 r+7.")]

    [Header("Description")]
    [TextArea]
    public string       description  = "";
    public List<RuneStatEntry> allowedStats = new List<RuneStatEntry>();

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>
    /// Roll le niveau de rune au drop — minimum lv50, maximum lv100.
    /// Distribution uniforme sur les tranches disponibles.
    /// §5.7 : "Niveau aléatoire — minimum lv50"
    /// </summary>
    public static int RollRuneLevel()
    {
        // Tranches : 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 → 11 valeurs
        int steps = Random.Range(0, 11);
        return 50 + steps * 5;
    }

    /// <summary>
    /// Roll la rareté au drop — r0 minimum, r+7 maximum.
    /// §5.7 : "Chaque rune a une rareté (r0 → r+7)"
    /// Distribution : r0(25%) r+1(22%) r+2(18%) r+3(14%) r+4(10%) r+5(6%) r+6(4%) r+7(1%)
    /// </summary>
    public static int RollRarity()
    {
        float roll = Random.value * 100f;
        if (roll < 25f)  return 0;
        if (roll < 47f)  return 1;
        if (roll < 65f)  return 2;
        if (roll < 79f)  return 3;
        if (roll < 89f)  return 4;
        if (roll < 95f)  return 5;
        if (roll < 99f)  return 6;
        return 7;
    }

    /// <summary>
    /// Nombre de lignes de stats selon le tableau GDD §5.7 (runeLevel × rarityRank).
    /// Interpolation linéaire entre les 4 paliers de niveau (50/65/80/95).
    /// Rareté interpolée entre r0, r+2, r+4, r+6, r+7.
    /// </summary>
    public static int GetLineCount(int runeLevel, int rarityRank)
    {
        // Tableau GDD §5.7 — [niveau][rareté]
        // Lignes : niveaux lv50/lv65/lv80/lv95 × raretés r0/r+2/r+4/r+6/r+7
        int[,] table = {
            { 1, 2, 3, 5,  6  }, // lv50
            { 2, 3, 4, 6,  7  }, // lv65
            { 3, 4, 5, 7,  8  }, // lv80
            { 4, 5, 6, 8,  10 }, // lv95
        };
        int[] levelKeys   = { 50, 65, 80, 95 };
        int[] rarityKeys  = { 0,  2,  4,  6,  7 };

        // Cherche l'index de niveau le plus proche (arrondi vers le bas)
        int levelIdx = 0;
        for (int i = 0; i < levelKeys.Length; i++)
            if (runeLevel >= levelKeys[i]) levelIdx = i;

        // Cherche l'index de rareté le plus proche (arrondi vers le bas)
        int rarityIdx = 0;
        for (int i = 0; i < rarityKeys.Length; i++)
            if (rarityRank >= rarityKeys[i]) rarityIdx = i;

        return table[levelIdx, rarityIdx];
    }

    /// <summary>
    /// Crée une instance droppée :
    ///   - Roll le niveau (tranche de 5, lv50 min)
    ///   - Roll la rareté (r0 min, r+7 max)
    ///   - Détermine le nombre de lignes via GetLineCount (tableau §6.7)
    ///   - Tire les StatTypes aléatoirement (sans remise) et rolle leurs valeurs
    ///   - isIdentified = false par défaut
    /// </summary>
    public RuneInstance CreateDropInstance()
    {
        if (allowedStats == null || allowedStats.Count == 0)
        {
            Debug.LogWarning($"[RuneData] {runeName} : aucun StatType autorisé défini !");
            return null;
        }

        int rolledLevel    = RollRuneLevel();
        int rolledRarity   = RollRarity();
        int lineCount      = GetLineCount(rolledLevel, rolledRarity);
        lineCount          = Mathf.Min(lineCount, allowedStats.Count);

        // Tirage sans remise
        List<RuneStatEntry> pool          = new List<RuneStatEntry>(allowedStats);
        List<StatBonus>     rolledBonuses = new List<StatBonus>();

        for (int i = 0; i < lineCount; i++)
        {
            int           idx   = Random.Range(0, pool.Count);
            RuneStatEntry entry = pool[idx];
            pool.RemoveAt(idx);

            rolledBonuses.Add(new StatBonus
            {
                statType = entry.statType,
                value    = Random.Range(entry.valueMin, entry.valueMax)
            });
        }

        return new RuneInstance(this, rolledLevel, rolledRarity, rolledBonuses);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(runeID))
            runeID = name;
    }
#endif
}

// =============================================================
// RuneInstance — données runtime d'une rune droppée
//
// runeLevel    → rollé au drop (lv50 min, tranche de 5)
// rarityRank   → rollé au drop (r0 min, r+7 max)
// bonuses      → lignes rollées au drop, immuables
// isIdentified → false jusqu'à identification chez l'Antiquaire
// =============================================================
[System.Serializable]
public class RuneInstance
{
    public RuneData data;

    // ── Stats rollées au drop (immuables) ─────────────────────
    public int             runeLevel  = 50;
    public int             rarityRank = 0;
    public List<StatBonus> bonuses    = new List<StatBonus>();

    // ── Identification ────────────────────────────────────────
    /// <summary>
    /// False jusqu'à identification chez l'Antiquaire (§6.7).
    /// L'UI affiche "???" pour chaque ligne si false.
    /// Passe à true via Identify().
    /// </summary>
    public bool isIdentified = false;

    // ── Constructeur ─────────────────────────────────────────
    public RuneInstance(RuneData source, int level, int rarity, List<StatBonus> rolledBonuses)
    {
        data       = source;
        runeLevel  = level;
        rarityRank = rarity;
        bonuses    = rolledBonuses;
    }

    // ── Identification ────────────────────────────────────────
    /// <summary>Identifie la rune chez l'Antiquaire — révèle toutes les lignes de stats.</summary>
    public void Identify()
    {
        if (isIdentified)
        {
            Debug.LogWarning($"[RuneInstance] {RuneName} déjà identifiée.");
            return;
        }
        isIdentified = true;
        Debug.Log($"[RuneInstance] Rune identifiée : {Label}");
    }

    // ── Accesseurs ────────────────────────────────────────────
    public string       RuneName     => data?.runeName    ?? "Rune";
    public Sprite       Icon         => data?.icon;
    public RuneCategory Category     => data?.runeCategory ?? RuneCategory.Weapon;
    public string       RarityLabel  => rarityRank > 0 ? $"r+{rarityRank}" : "r0";
    public string       LevelLabel   => $"lv{runeLevel}";
    public List<StatusEffectEntry> StatusEffects => data?.statusEffects;

    /// <summary>
    /// Affiche la rune sous forme lisible.
    /// Si non identifiée : "Rune lv65 r+3 — ???"
    /// Si identifiée     : "Rune lv65 r+3 — [liste des stats]"
    /// </summary>
    public string Label
    {
        get
        {
            if (!isIdentified) return $"{RuneName} {LevelLabel} {RarityLabel} — ???";

            var lines = new System.Text.StringBuilder();
            foreach (StatBonus b in bonuses)
                lines.Append($"{b.statType} +{b.value:F2}  ");
            return $"{RuneName} {LevelLabel} {RarityLabel} — {lines.ToString().TrimEnd()}";
        }
    }

    /// <summary>
    /// True si cette rune peut être insérée dans un équipement de niveau donné.
    /// §6.7 : runeLevel ≤ weaponLevel (tranche de 5 inférieure ou égale).
    /// Ex : arme lv63 → tranche max lv60 → rune lv65 refusée.
    /// </summary>
    public bool CanInsertInto(int equipmentLevel)
    {
        // GDD §5.7 : runeLevel ≤ weaponLevel (comparaison directe — pas d'arrondi par tranche).
        // Ex : arme lv63 → rune lv63 acceptée, rune lv64 refusée.
        return runeLevel <= equipmentLevel;
    }
}
