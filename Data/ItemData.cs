using UnityEngine;
using UnityEngine.Serialization;

// =============================================================
// ItemData — ScriptableObject racine commun à tous les items
// Path : Assets/Scripts/Data/ItemData.cs
// AetherTree GDD v3.6 — §5.1
//
// Base commune à tout ce qui peut être détenu par un joueur —
// équipement porté, ressource, consommable. Les systèmes génériques
// (inventaire, HdV, loot, mail) devraient à terme travailler sur des
// références ItemData plutôt que sur des types concrets.
//
// ItemData ne contient aucune stat de combat (GDD §5.1) — les
// sous-types (WeaponData, ArmorData...) ajoutent leurs champs propres.
//
// RuneData/GemData n'héritent PAS de ItemData pour l'instant — voir
// a implémenter/note-rune-gem-hors-scope.md (refonte dédiée à part,
// alignée sur le GDD §5.11, pas encore planifiée).
// =============================================================

public abstract class ItemData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais, quelle que soit la langue du joueur.\n" +
             "Utilisée pour les logs, la sauvegarde, les comparaisons internes.\n" +
             "Convention : snake_case, unique par item (ex: \"sword_iron_01\").\n" +
             "Ne JAMAIS afficher cette valeur au joueur — utiliser displayName pour l'affichage.")]
    public string itemID;

    [Tooltip("Nom affiché au joueur (fr/en). Purement cosmétique — n'utiliser displayName\n" +
             "ni dans un log, ni dans une sauvegarde, ni comme clé de comparaison\n" +
             "(le texte change avec la langue). Voir itemID pour l'identifiant stable.")]
    public LocalizedText displayName = new LocalizedText();

    [Tooltip("Description affichée au joueur (fr/en) — tooltip, fiche objet.")]
    public LocalizedText description = new LocalizedText();

    public Sprite icon;

    [Header("Inventaire / Économie")]
    [Tooltip("true = non échangeable, non vendable au HdV. Items de quête, drops de\n" +
             "condition cachée, cosmétiques premium.")]
    public bool isBound = false;

    [Tooltip("Autorise l'empilement dans l'inventaire.")]
    public bool isStackable = false;

    [Tooltip("Taille max d'une pile — ignoré si isStackable = false.")]
    [FormerlySerializedAs("maxStack")] // ResourceData/ConsumableData avant migration ItemData — no-op pour les autres types
    public int stackSize = 99;

    [Tooltip("Niveau minimum du joueur pour équiper ou utiliser cet item.")]
    [Min(1)] public int requiredLevel = 1;

    [Tooltip("Prix de vente au PNJ marchand, en Aeris. 0 = non vendable au PNJ.")]
    [FormerlySerializedAs("sellPrice")] // ResourceData avant migration ItemData — no-op pour les autres types
    public int vendorPrice = 0;

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (string.IsNullOrEmpty(itemID))
            itemID = name;
    }
#endif
}
