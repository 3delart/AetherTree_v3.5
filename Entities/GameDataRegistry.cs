using UnityEngine;

// =============================================================
// GAMEDATAREGISTRY — Référence centrale des ScriptableObjects globaux
// Path : Assets/Scripts/Core/GameDataRegistry.cs
// AetherTree GDD v3.5
//
// À attacher sur le GameObject _Managers dans la scène.
// Initialise les instances statiques de tous les SO globaux
// au démarrage, avant que les autres systèmes en aient besoin.
//
// Pour ajouter un nouveau SO global :
//   1. Ajouter un champ public ici
//   2. L'assigner dans l'Inspector sur _Managers
//   3. Le SO expose son Instance statique via OnEnable()
// =============================================================

public class GameDataRegistry : MonoBehaviour
{
    public static GameDataRegistry Instance { get; private set; }

    [Header("Armes")]
    [Tooltip("Registry des skills d'attaque de base par type d'arme.")]
    public WeaponTypeRegistry weaponTypeRegistry;

    // Ajouter ici les futurs SO globaux :
    // public SkillRegistry       skillRegistry;
    // public StatusEffectLibrary statusEffectLibrary;
    // public ElementalConfig     elementalConfig;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;

        InitRegistries();
    }

    private void InitRegistries()
    {
        if (weaponTypeRegistry != null)
            WeaponTypeRegistry.Instance = weaponTypeRegistry;
        else
            Debug.LogError("[GameDataRegistry] weaponTypeRegistry non assigné sur _Managers !");

        // Ajouter ici l'init des futurs registries
    }
}
