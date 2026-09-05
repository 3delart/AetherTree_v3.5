using UnityEngine;
using System.Collections.Generic;

// =============================================================
// RECIPEDATA — ScriptableObject de recette de craft
// Path : Assets/Scripts/Data/Craft/RecipeData.cs
// AetherTree GDD v3.6 — §13.2 (Types d'Ateliers)
//
// 1 recette = 1 résultat (SO d'item déjà existant, ex: ConsumableData,
// ResourceData, WeaponData...) + une liste d'ingrédients (SO déjà
// existants + quantité). Déterministe — pas de taux d'échec comme
// UpgradeSystem, les ingrédients sont toujours consommés contre le
// résultat garanti.
//
// station route la recette vers le bon onglet PNJ (voir PNJType.HasShop()) :
//   CraftWeaponArmor  → Forgeron, onglet Craft Équipement
//   CraftFoodPotion   → Cuisinier, onglet Cuisiner (nourriture + potions)
//   CraftDecor        → Bricoleur, onglet Bricoler (déco housing)
//   CraftHelmet       → Tailleur, onglet Craft
//   CraftIntermediate → Station de Craft, onglet Craft (ressources de base
//                       → ressources de craft, tous domaines confondus)
//   CraftGlovesBoots  → Cordonnier, onglet Craft (gants/bottes S0 de base —
//                       distinct de Fusion, qui monte S0→S6 sur un exemplaire déjà possédé)
//   CraftJewelry      → Bijoutier, onglet Craft (bijoux de base —
//                       distinct de Gemmes, qui pose une gemme sur un bijou déjà possédé)
//
// Identification (Antiquaire), Upgrade et Pari (Forgeron) ne sont PAS des
// recettes — mécaniques dédiées existantes (UpgradeSystem, RaritySystem),
// pas de RecipeData. Fusion (Cordonnier) et Gemmes (Bijoutier) non plus —
// elles agissent sur un item déjà fabriqué, alors que CraftGlovesBoots/
// CraftJewelry ci-dessus fabriquent l'item de base.
//
// Déblocage : PAS de champ ici — une recette "Base" est accordée automatiquement
// à Player.unlockedRecipes par CraftSystem.GrantBaseRecipes() au démarrage, une
// recette "Unlocked" arrive via ConditionReward.rewardRecipe (RecipeCraftChecker
// compte les crafts), comme les skills/passifs — voir Systems/CraftSystem.cs.
//
// Assets > Create > AetherTree > Craft > RecipeData
// =============================================================

public enum CraftStationType
{
    CraftWeaponArmor,   // Forgeron
    CraftFoodPotion,    // Cuisinier (nourriture + potions)
    CraftDecor,         // Bricoleur (déco housing)
    CraftHelmet,        // Tailleur
    CraftIntermediate,  // Station de Craft (ressources de base → ressources de craft)

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety, voir note-nomenclature-id.md).
    CraftGlovesBoots,   // Cordonnier (craft de base — distinct de sa Fusion S0→S6)
    CraftJewelry,       // Bijoutier (craft de base — distinct de sa pose de Gemmes)
}

// Catégorie de résultat — utilisée par CraftJournalUI pour trier (pas par station, le
// journal s'ouvre n'importe où, contrairement à CraftPanelUI qui est à un PNJ précis).
// Calculée depuis result, jamais un champ à part — pas de risque de désync.
public enum RecipeCategory
{
    Equipment,   // WeaponData, ArmorData, HelmetData, GlovesData, BootsData, JewelryData,
                 // SpiritData, CosmeticDataHead/Body, TalismanData
    Consumable,  // ConsumableData
    Resource,    // ResourceData
}

// GDD §13.3/13.4/13.5 — Base = accordée automatiquement (CraftSystem.GrantBaseRecipes),
// Unlocked = débloquée via ConditionReward.rewardRecipe (voir le commentaire "Déblocage"
// en tête de ce fichier). Une recette Unlocked créée sans reward configuré ailleurs
// restera simplement inaccessible — pas de garde-fou automatique côté RecipeData.
public enum RecipeTier
{
    Base,        // §13.3 — disponible dès le départ
    Unlocked,    // §13.4 — débloquée via une condition (ex: craft 100 Base → recette suivante)
}

[CreateAssetMenu(fileName = "rcp_", menuName = "AetherTree/Craft/RecipeData")]
public class RecipeData : ScriptableObject
{
    [Tooltip("Identifiant unique — snake_case, immuable après création. Clé de persistance.")]
    public string recipeID;

    [Header("Résultat")]
    [Tooltip("SO déjà existant obtenu à la fabrication — ConsumableData, ResourceData, " +
             "WeaponData, ArmorData, HelmetData... tout ce qui hérite de ItemData.")]
    public ItemData result;
    [Min(1)]
    public int resultQuantity = 1;

    [Header("Ingrédients")]
    [Tooltip("SO déjà existants consommés à la fabrication, avec leur quantité.")]
    public List<RecipeIngredient> ingredients = new List<RecipeIngredient>();

    [Header("Station")]
    [Tooltip("Onglet PNJ où cette recette apparaît — voir le mapping en tête de fichier.")]
    public CraftStationType station = CraftStationType.CraftIntermediate;

    [Header("Craft")]
    [Tooltip("Durée du channeling en secondes — réutilise ProgressBarUI.BarType.Craft " +
             "(même pattern que ForgeUI/RarityUI).")]
    [Min(0f)]
    public float craftTimeSeconds = 2f;

    [Header("Palier")]
    [Tooltip("Base = accordée automatiquement au démarrage. Unlocked = débloquée via un\n" +
             "ConditionReward.rewardRecipe pointant vers cette recette (GDD §13.3-13.5).")]
    public RecipeTier tier = RecipeTier.Base;

    /// <summary>Catégorie dérivée du type C# de result — voir RecipeCategory. Retourne
    /// Resource par défaut si result est vide ou d'un type non reconnu (SO sans doute
    /// encore assigné en cours d'édition).</summary>
    public RecipeCategory GetCategory() => result switch
    {
        WeaponData or ArmorData or HelmetData or GlovesData or BootsData or JewelryData
            or SpiritData or CosmeticDataHead or CosmeticDataBody or TalismanData => RecipeCategory.Equipment,
        ConsumableData => RecipeCategory.Consumable,
        ResourceData   => RecipeCategory.Resource,
        _              => RecipeCategory.Resource,
    };

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(recipeID))
            recipeID = name;
    }
#endif
}

[System.Serializable]
public class RecipeIngredient
{
    [Tooltip("SO déjà existant requis — ResourceData, ConsumableData...")]
    public ItemData item;
    [Min(1)]
    public int quantity = 1;
}
