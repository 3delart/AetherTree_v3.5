using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// MOBANIMATIONPRESETS — ScriptableObject global (asset unique)
// Path : Assets/Scripts/Data/Mobs/MobAnimationPresets.cs
//
// Un preset de clips locomotion (idle/variantes/walk/chase/death) par
// AnimationType. Consommé UNIQUEMENT par MobData.OnValidate()/PNJData.
// OnValidate() en Editor — aucun code gameplay n'y touche à l'exécution,
// donc pas de singleton runtime : juste une recherche AssetDatabase, mise
// en cache, voir FindAsset() ci-dessous.
//
// Setup : un seul asset dans le projet (Assets > Create > AetherTree >
// Config > MobAnimationPresets), rempli au fur et à mesure que de nouveaux
// rigs existent. Quadruped/Autre peuvent rester vides tant qu'aucun mob de
// ce type n'existe — GetPreset() retourne alors null, auto-fill no-op.
// =============================================================

[CreateAssetMenu(fileName = "MobAnimationPresets", menuName = "AetherTree/Config/MobAnimationPresets")]
public class MobAnimationPresets : ScriptableObject
{
    [System.Serializable]
    public class Preset
    {
        public AnimationType animationType;

        [Tooltip("Anim jouée à l'arrêt hors combat.")]
        public AnimationClip idleClip;
        [Tooltip("Variantes supplémentaires d'idleClip — copiées (pas partagées) dans le mob/PNJ " +
                 "à l'auto-fill, pour que chacun garde sa propre liste modifiable indépendamment.")]
        public List<AnimationClip> idleClipVariants = new List<AnimationClip>();
        [Tooltip("Anim de déplacement en Patrol (déambulation).")]
        public AnimationClip walkClip;
        [Tooltip("Anim de déplacement en Engage (poursuite/combat rapproché).")]
        public AnimationClip chaseClip;
        [Tooltip("Anim de mort.")]
        public AnimationClip deathClip;
    }

    [Tooltip("Un preset par AnimationType. Pas de doublon de type dans la liste (le premier " +
             "trouvé gagne si jamais il y en a deux).")]
    public List<Preset> presets = new List<Preset>();

    public Preset GetPreset(AnimationType type)
    {
        foreach (var preset in presets)
            if (preset.animationType == type)
                return preset;
        return null;
    }

#if UNITY_EDITOR
    private static MobAnimationPresets _cached;

    /// <summary>Recherche l'unique asset MobAnimationPresets du projet, mis en cache après le
    /// premier appel. Editor-only — jamais de dépendance runtime/singleton ici, voir
    /// commentaire de classe.</summary>
    public static MobAnimationPresets FindAsset()
    {
        if (_cached != null) return _cached;

        string[] guids = AssetDatabase.FindAssets("t:MobAnimationPresets");
        if (guids.Length == 0) return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        _cached = AssetDatabase.LoadAssetAtPath<MobAnimationPresets>(path);
        return _cached;
    }
#endif
}
