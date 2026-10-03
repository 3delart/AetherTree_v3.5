#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// =============================================================
// PRESTIGEAURADATABUILDER.CS — Remplit l'asset PrestigeAuraData
// Path : Assets/Scripts/Editor/PrestigeAuraDataBuilder.cs
//
// Menu : AetherTree > Progression > Créer/Remplir PrestigeAuraData
//
// Crée (si absent) l'asset unique Assets/Content/Progression/PrestigeAuraData.asset et
// remplit les 10 paliers Prestige + 6 paliers Aura : labels, seuils/planchers, malus de prix,
// coûts de Purification (Aeris — voir spec §2.5). Réutilise l'asset s'il existe déjà, écrase
// seulement les champs listés ci-dessous — n'importe quel debuff/icône déjà glissé à la main
// est PRÉSERVÉ (jamais réinitialisé par un second passage de ce script).
//
// Tente aussi de lier automatiquement les 4 DebuffData Aura (dbf_aura_voilee/corrompue/
// maudite/dechue, voir Editor/AuraDebuffBuilder.cs) SI ILS EXISTENT DÉJÀ dans le
// projet — ordre d'exécution des deux builders indifférent, celui-ci cherche par nom, ne
// crée rien lui-même. Les sprites (prestigeTiers[i].icon / auraTiers[i].icon) restent à
// glisser à la main — aucune source d'image fiable à assigner automatiquement.
//
// ⚠️ Coûts Purification et malus de prix = valeurs DRAFT, jamais calibrées avec Florian —
// à ajuster directement dans l'Inspector de l'asset, ce script ne fait que poser un point de
// départ raisonnable (progression x2/x2.5 par palier).
// =============================================================
public static class PrestigeAuraDataBuilder
{
    private const string Folder = "Assets/Content/Progression";
    private const string AssetPath = Folder + "/PrestigeAuraData.asset";

    private static readonly (string label, int threshold)[] PrestigeTiers =
    {
        ("Novice",    0),
        ("Apprenti",  500),
        ("Éveillé",   2000),
        ("Aguerri",   10000),
        ("Chevronné", 50000),
        ("Illustre",  170000),
        ("Maître",    450000),
        ("Élite",     1000000),
        // Héroïque inséré 2026-09-29 (Florian) — comble le trou Élite→Légende (x5, seul palier
        // qui cassait la décélération x4/x5/x5/x3.4/x2.65/x2.22 du reste de la table) sans
        // toucher au seuil final de Légende, qu'il voulait garder à 5M+. Ratios résultants :
        // 1M→2.5M = x2.5, 2.5M→5M = x2.
        ("Héroïque",  2500000),
        ("Légende",   5000000),
    };

    // floor / priceMalus / coût Purification pour MONTER à CE palier depuis le précédent
    // (index 0 = Normal, purificationAerisCost jamais lu depuis ce palier — rien à racheter).
    // 6 paliers (Éteint retiré, Florian 2026-09-29 — calé sur les 5 vrais malus NosTale) +
    // Aura démarre à +100 (Player.aura), pas 0 : 5 morts "gratuites" avant Terni (-20/mort),
    // puis 10 morts par palier ensuite — Terni@6, Voilé@16, Corrompu@26, Maudit@36, Déchu@46.
    private static readonly (string label, int floor, float priceMalus, int purifyCost)[] AuraTiers =
    {
        ("Normal",   0,             0f,   0),
        ("Terni",    -200,          0.10f, 100),
        ("Voilé",    -400,          0.20f, 300),
        ("Corrompu", -600,          0.30f, 700),
        ("Maudit",   -800,          0.40f, 1500),
        ("Déchu",    int.MinValue,  0.50f, 3000),
    };

    private static readonly string[] DebuffAssetNames =
    {
        null, null, "dbf_aura_voilee", "dbf_aura_corrompue", "dbf_aura_maudite", "dbf_aura_dechue",
    };

    [MenuItem("AetherTree/Progression/Créer-Remplir PrestigeAuraData")]
    public static void Build()
    {
        // Cherche un PrestigeAuraData EXISTANT n'importe où dans le projet avant de créer quoi
        // que ce soit — Florian a le droit de garder/déplacer son asset où il veut (ex :
        // Assets/Scripts/Data/Prestige & Aura/, pas le chemin par défaut ci-dessous) ; sans
        // cette recherche, relancer ce menu créerait un SECOND asset orphelin au lieu de mettre
        // à jour celui réellement dragué sur Player.prestigeAuraData.
        var data = FindExistingAsset();
        bool created = false;
        if (data == null)
        {
            EnsureFolder(Folder);
            data = ScriptableObject.CreateInstance<PrestigeAuraData>();
            AssetDatabase.CreateAsset(data, AssetPath);
            created = true;
        }

        Undo.RecordObject(data, "Remplir PrestigeAuraData");

        // ── Prestige ──────────────────────────────────────────
        if (data.prestigeTiers == null) data.prestigeTiers = new List<PrestigeTierData>();
        while (data.prestigeTiers.Count < PrestigeTiers.Length) data.prestigeTiers.Add(new PrestigeTierData());
        for (int i = 0; i < PrestigeTiers.Length; i++)
        {
            data.prestigeTiers[i].label     = PrestigeTiers[i].label;
            data.prestigeTiers[i].threshold = PrestigeTiers[i].threshold;
            // icon : jamais touché — préserve tout sprite déjà glissé à la main.
        }

        // ── Aura ──────────────────────────────────────────────
        if (data.auraTiers == null) data.auraTiers = new List<AuraTierData>();
        while (data.auraTiers.Count < AuraTiers.Length) data.auraTiers.Add(new AuraTierData());

        int wiredDebuffs = 0;
        for (int i = 0; i < AuraTiers.Length; i++)
        {
            var tier = data.auraTiers[i];
            tier.label                  = AuraTiers[i].label;
            tier.floor                  = AuraTiers[i].floor;
            tier.priceMalus             = AuraTiers[i].priceMalus;
            tier.purificationAerisCost  = AuraTiers[i].purifyCost;
            // icon / purificationResource / purificationResourceQty : jamais touchés.

            if (tier.debuff == null && DebuffAssetNames[i] != null)
            {
                var found = FindDebuffByName(DebuffAssetNames[i]);
                if (found != null) { tier.debuff = found; wiredDebuffs++; }
            }
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PrestigeAuraDataBuilder] Asset {(created ? "créé" : "mis à jour")} à {AssetPath} — " +
            $"10 paliers Prestige + 6 paliers Aura remplis (labels/seuils/planchers/malus/coûts Purification), " +
            $"{wiredDebuffs} debuff(s) Aura lié(s) automatiquement. Restent à glisser à la main : les sprites " +
            "(icon) sur les 16 paliers, la ressource optionnelle de Purification si voulue, et le champ " +
            "Player.prestigeAuraData sur le prefab Player. ⚠️ Coûts Purification et malus de prix sont des " +
            "valeurs DRAFT (progression x2/x2.5), pas calibrées — à ajuster dans l'Inspector.");

        Selection.activeObject = data;
    }

    private static PrestigeAuraData FindExistingAsset()
    {
        var direct = AssetDatabase.LoadAssetAtPath<PrestigeAuraData>(AssetPath);
        if (direct != null) return direct;

        var guids = AssetDatabase.FindAssets("t:PrestigeAuraData");
        if (guids.Length == 0) return null;
        if (guids.Length > 1)
            Debug.LogWarning($"[PrestigeAuraDataBuilder] {guids.Length} assets PrestigeAuraData trouvés dans le " +
                "projet — mise à jour du premier seulement. Supprime les doublons si ce n'est pas voulu.");
        return AssetDatabase.LoadAssetAtPath<PrestigeAuraData>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private static DebuffData FindDebuffByName(string assetName)
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:DebuffData {assetName}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var debuff = AssetDatabase.LoadAssetAtPath<DebuffData>(path);
            if (debuff != null && debuff.name == assetName) return debuff;
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
