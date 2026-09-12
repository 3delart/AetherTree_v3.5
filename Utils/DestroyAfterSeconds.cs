using UnityEngine;

// =============================================================
// DESTROYAFTERSECONDS.CS — Détruit ce GameObject après une durée fixe.
// Path : Assets/Scripts/Utils/DestroyAfterSeconds.cs
//
// Cas d'usage : les VFX ponctuels (SkillData.vfxCast, SkillData.vfxImpact,
// HitStep.vfxPrefab...) ne sont JAMAIS détruits par le code — leur durée de
// vie est entièrement déléguée au prefab. Un Particle System avec
// "Stop Action = Destroy" se gère déjà tout seul ; un mesh animé importé
// (FBX/Blender, Animation legacy) n'a rien d'équivalent — sans ce
// composant, il reste affiché indéfiniment si son clip boucle.
//
// Ajoute ce composant sur le prefab, règle `duration` à la durée voulue
// (ex: la longueur de l'anim de cast, ou juste "assez long pour être vu").
// Fonctionne peu importe que l'anim boucle ou non.
// =============================================================

public class DestroyAfterSeconds : MonoBehaviour
{
    [Tooltip("Durée en secondes avant que ce GameObject soit détruit.")]
    public float duration = 2f;

    private void Start()
    {
        Destroy(gameObject, duration);
    }
}
