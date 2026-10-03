#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// AURADEBUFFBUILDER.CS — Crée les 4 DebuffData de palier Aura
// Path : Assets/Scripts/Editor/AuraDebuffBuilder.cs
//
// Menu : AetherTree > StatusEffects > Créer DebuffData Aura
//
// Crée (si absents — réutilise s'ils existent déjà, ne recrée jamais) les 4 DebuffData de
// palier Aura (voir docs/superpowers/specs/2026-09-29-prestige-aura-design.md §2.3, palier
// Éteint retiré 2026-09-29 — 6 paliers Aura au total désormais, Normal/Terni n'ont jamais de
// debuff), tous debuffType = Stats + debuffStatType = AllDefense (Percent), escalade par palier.
// Valeurs stat SEULEMENT posées À LA CRÉATION (jamais réécrites sur un asset déjà existant —
// Florian, 2026-09-29 : "arrête de changer l'editor et change direct le data si nécessaire" —
// une fois créés, ces 4 assets se retouchent directement dans l'Inspector/YAML, plus par ce
// script, pour ne jamais écraser un ajustement fait à la main). + immuneToPurify = true (§2.3 :
// ne doit jamais être retirable par un Purify, seulement par le franchissement de seuil Aura).
// Câble aussi blockedSkills = [skl_capture] sur les 3 paliers Corrompu→Déchu (capture bloquée à
// partir de Corrompu, pas Voilé — décalé d'un cran suite au retrait d'Éteint, qui portait ce
// seuil avant), en cherchant skl_capture par nom dans le projet (pas de chemin en dur — si
// Florian le déplace, ce script continue de le trouver).
// =============================================================
public static class AuraDebuffBuilder
{
    private const string Folder = "Assets/Content/StatusEffect/Debuffs/Aura";

    // malusPercent : réduction AllDefense (Percent) — escalade avec la sévérité du palier,
    // même logique de progression que priceMalus sur PrestigeAuraData (10/20/30/40/50%).
    private static readonly (string fileName, string displayName, bool blocksCapture, float malusPercent)[] Tiers =
    {
        ("dbf_aura_voilee",    "Voilé",    false, 0.05f),
        ("dbf_aura_corrompue", "Corrompu", true,  0.10f),
        ("dbf_aura_maudite",   "Maudit",   true,  0.15f),
        ("dbf_aura_dechue",    "Déchu",    true,  0.25f),
    };

    [MenuItem("AetherTree/StatusEffects/Créer DebuffData Aura")]
    public static void Build()
    {
        var captureSkill = FindSkillByName("skl_capture");
        if (captureSkill == null)
            Debug.LogWarning("[AuraDebuffBuilder] skl_capture introuvable dans le projet — " +
                "les DebuffData seront créés sans blockedSkills, à compléter à la main.");

        if (!AssetDatabase.IsValidFolder(Folder))
            Debug.LogWarning($"[AuraDebuffBuilder] Dossier '{Folder}' introuvable — attendu " +
                "(dbf_aura_xxx.asset déjà dedans). Vérifie le chemin.");

        int created = 0, reused = 0, wired = 0;

        foreach (var (fileName, displayName, blocksCapture, malusPercent) in Tiers)
        {
            string path = $"{Folder}/{fileName}.asset";
            var debuff = AssetDatabase.LoadAssetAtPath<DebuffData>(path);

            if (debuff == null)
            {
                debuff = ScriptableObject.CreateInstance<DebuffData>();
                debuff.effectID = fileName;
                debuff.effectName = new LocalizedText { fr = $"Aura {displayName}", en = $"Aura {displayName}" };
                debuff.debuffType     = DebuffType.Stats;
                debuff.debuffStatType = StatModifierType.AllDefense;
                debuff.debuffModifier = ModifierType.Percent;
                debuff.debuffValue    = malusPercent;
                AssetDatabase.CreateAsset(debuff, path);
                created++;
            }
            else
            {
                reused++;
            }

            Undo.RecordObject(debuff, "Configurer DebuffData Aura");
            debuff.immuneToPurify = true;

            // duration N'A PAS de notion "permanent" dans StatusEffectData (juste un compte à
            // rebours, IsExpired quand remainingTime <= 0) — une valeur normale (le template
            // dbf_aura_xxx a 3s) ferait disparaître le malus tout seul après quelques secondes
            // même si l'Aura reste mauvaise. Valeur énorme = permanent en pratique ; le futur
            // code de changement de palier Aura devra RETIRER le debuff explicitement (jamais
            // compter sur l'expiration naturelle) quand le joueur remonte de palier.
            debuff.duration = 999999f;

            if (blocksCapture && captureSkill != null)
            {
                if (debuff.blockedSkills == null) debuff.blockedSkills = new System.Collections.Generic.List<SkillData>();
                if (!debuff.blockedSkills.Contains(captureSkill))
                {
                    debuff.blockedSkills.Add(captureSkill);
                    wired++;
                }
            }

            // Même 3 paliers que le blocage capture (Corrompu→Déchu) — "le familier a honte de
            // lui", pas encore consommé par aucun code (système d'équipement Familier pas
            // construit), même statut que blockedSkills avant que skl_capture existe.
            if (blocksCapture) debuff.blocksFamiliarEquip = true;

            EditorUtility.SetDirty(debuff);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[AuraDebuffBuilder] {created} créé(s), {reused} déjà existant(s) réutilisé(s), " +
                   $"{wired} palier(s) câblé(s) avec blockedSkills=[skl_capture] + " +
                   "blocksFamiliarEquip=true (Corrompu→Déchu). " +
                   "Stats (debuffStatType/debuffValue) posées uniquement sur les assets NOUVEAUX — " +
                   "un asset réutilisé garde ses valeurs actuelles, à retoucher directement dedans si besoin.");

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<DebuffData>($"{Folder}/{Tiers[0].fileName}.asset");
    }

    private static SkillData FindSkillByName(string assetName)
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:SkillData {assetName}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(path);
            if (skill != null && skill.name == assetName) return skill;
        }
        return null;
    }
}
#endif
