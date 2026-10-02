using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CRAFTSYSTEM.CS — Registre + exécution du craft
// Path : Assets/Scripts/Systems/CraftSystem.cs
// AetherTree GDD v3.6 — §13.2
//
// Registre de toutes les RecipeData du projet (auto-fill éditeur, même
// patron que UnlockManager.allConditions). Accorde les recettes Base au
// joueur au démarrage. Exécute le craft (channeling → consommation →
// production) sur le même patron que ForgeUI.StartUpgradeChannel/
// ResolveUpgrade : la consommation n'a jamais lieu au clic, seulement à
// la fin de la barre — annuler ne consomme rien.
// =============================================================

public class CraftSystem : MonoBehaviour
{
    public static CraftSystem Instance { get; private set; }

    [Tooltip("Clic droit → 'Auto-remplir allRecipes' pour scanner le projet.")]
    public List<RecipeData> allRecipes = new List<RecipeData>();

    [Header("Animations (optionnel)")]
    [Tooltip("Jouée pendant la barre de progression pour une recette dont le résultat est un\n" +
             "ConsumableData de type Potion (voir ConsumableType). Null = pas d'anim spécifique.")]
    public AnimationClip potionCraftAnimation;
    [Tooltip("Jouée pendant la barre de progression pour une recette dont le résultat est un\n" +
             "ConsumableData de type Food. Null = pas d'anim spécifique.")]
    public AnimationClip foodCraftAnimation;

    private Player _player;
    private bool   _channeling;
    public  bool   IsChanneling => _channeling;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
#if UNITY_EDITOR
        EditorAutoFill();
#endif
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        GrantBaseRecipes();
    }

    private void GrantBaseRecipes()
    {
        if (_player == null) return;
        foreach (var recipe in allRecipes)
            if (recipe != null && recipe.tier == RecipeTier.Base)
                _player.UnlockRecipe(recipe);
    }

    // =========================================================
    // EXÉCUTION
    // =========================================================

    public bool CanCraft(RecipeData recipe, int quantity)
    {
        if (recipe == null || quantity <= 0) return false;
        foreach (var ing in recipe.ingredients)
            if (InventorySystem.Instance.GetItemCount(ing.item) < ing.quantity * quantity)
                return false;
        return true;
    }

    /// <summary>Quantité maximum fabricable d'un coup avec le stock d'ingrédients actuel —
    /// le plus petit ratio have/needed sur tous les ingrédients, plafonné à 99 (même plafond
    /// absolu que ConfirmationUI). Utilisé par CraftRecipeUI pour borner son slider.</summary>
    public int GetMaxCraftable(RecipeData recipe)
    {
        if (recipe == null) return 0;
        if (recipe.ingredients.Count == 0) return 99;

        int max = int.MaxValue;
        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null || ing.quantity <= 0) continue;
            int have     = InventorySystem.Instance?.GetItemCount(ing.item) ?? 0;
            int possible = have / ing.quantity;
            if (possible < max) max = possible;
        }
        return Mathf.Clamp(max == int.MaxValue ? 0 : max, 0, 99);
    }

    public void StartCraft(RecipeData recipe, int quantity, System.Action onComplete, System.Action onCancel)
    {
        if (_channeling || !CanCraft(recipe, quantity)) return;
        _channeling = true;

        // Anim de canalisation selon le type du résultat — null pour tout le reste
        // (équipement/ressource) tant qu'aucune anim dédiée n'est fournie. PlayChannel()
        // no-op déjà proprement si le clip est null (voir PlayerAnimatorController).
        AnimationClip craftClip = null;
        if (recipe.result is ConsumableData cd)
        {
            if (cd.consumableType == ConsumableType.Potion) craftClip = potionCraftAnimation;
            else if (cd.consumableType == ConsumableType.Food) craftClip = foodCraftAnimation;
        }
        _player.AnimatorController?.PlayChannel(craftClip);

        ProgressBarUI.Instance?.StartProgress(
            label:        "Craft en cours...",
            duration:     recipe.craftTimeSeconds,
            onComplete:   () => ResolveCraft(recipe, quantity, onComplete),
            onCancel:     () => { _channeling = false; _player.AnimatorController?.CancelChannel(); onCancel?.Invoke(); },
            type:         ProgressBarUI.BarType.Craft,
            followTarget: _player.transform
        );
    }

    private void ResolveCraft(RecipeData recipe, int quantity, System.Action onComplete)
    {
        _channeling = false;
        // Coupe l'anim de canalisation pile à la fin de la barre — sa durée réelle (celle du
        // clip) ne correspond pas forcément à recipe.craftTimeSeconds, jamais de pose figée
        // qui dépasse ou un retour en locomotion en avance.
        _player.AnimatorController?.CancelChannel();
        foreach (var ing in recipe.ingredients)
            InventorySystem.Instance.ConsumeItem(ing.item, ing.quantity * quantity);

        // Production du résultat — dispatch par sous-type, pas de ItemData.CreateInstance
        // générique. Resource/Consumable = stackables, resultQuantity par craft ; équipement
        // (Weapon/Armor/Helmet/Gloves/Boots/Jewelry) = 1 exemplaire par itération de la boucle,
        // resultQuantity ignoré (une recette d'équipement en donne toujours 1 à la fois — GDD §13.2).
        for (int i = 0; i < quantity; i++)
        {
            InventoryItem item = recipe.result switch
            {
                ResourceData rd   => new InventoryItem(rd.CreateInstance(recipe.resultQuantity)),
                ConsumableData cd => new InventoryItem(cd.CreateInstance(recipe.resultQuantity)),
                WeaponData wd     => new InventoryItem(wd.CreateDropInstance()),
                ArmorData ad      => new InventoryItem(ad.CreateDropInstance()),
                HelmetData hd     => new InventoryItem(hd.CreateInstance()),
                GlovesData gd     => new InventoryItem(gd.CreateInstance()),
                BootsData bd      => new InventoryItem(bd.CreateInstance()),
                JewelryData jd    => new InventoryItem(jd.CreateInstance()),
                _                 => null,
            };
            if (item != null) InventorySystem.Instance?.AddItem(item);

            // 1 event PAR unité fabriquée — UnlockManager.IncrementCounter compte toujours
            // +1 par event reçu (même contrat que MobKilledEvent/SkillUsedEvent...), jamais
            // une quantité interne à l'event. Publier un seul event avec quantity=N ici
            // ferait compter "craft 5x" comme 1 seul craft pour les conditions de déblocage.
            GameEventBus.Publish(new RecipeCraftedEvent { recipe = recipe, player = _player, quantity = 1 });
        }

        CharacterPanelUI.Instance?.Refresh();
        InventoryUI.Instance?.RefreshGrid();
        onComplete?.Invoke();
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-remplir allRecipes")]
    private void EditorAutoFill()
    {
        allRecipes.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:RecipeData");
        foreach (string guid in guids)
        {
            var r = UnityEditor.AssetDatabase.LoadAssetAtPath<RecipeData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (r != null) allRecipes.Add(r);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
