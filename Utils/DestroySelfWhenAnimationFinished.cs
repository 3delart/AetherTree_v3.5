using UnityEngine;

// =============================================================
// DESTROYSELFWHENANIMATIONFINISHED.CS — Détruit ce GameObject dès que son
// composant Animation (legacy) n'a plus aucun clip en cours de lecture.
// Path : Assets/Scripts/Utils/DestroySelfWhenAnimationFinished.cs
//
// Alternative à DestroyAfterSeconds pour un effet PONCTUEL avec une vraie
// fin (ex: pentacle qui s'ouvre → tourne → se referme) — la durée du VFX
// est celle de l'anim elle-même, pas une valeur devinée à la main. Ne
// fonctionne QUE si AUCUN clip du composant n'est en boucle (WrapMode.Loop)
// — un clip qui boucle ne s'arrête jamais, `isPlaying` ne redevient jamais
// faux, l'objet ne serait donc jamais détruit.
//
// Compatible avec PlayAllAnimationClips (plusieurs clips en parallèle sur
// des layers différents) : Animation.isPlaying ne redevient faux que quand
// TOUS les clips, sur tous les layers, sont terminés.
// =============================================================

[RequireComponent(typeof(Animation))]
public class DestroySelfWhenAnimationFinished : MonoBehaviour
{
    private Animation _anim;

    private void Start()
    {
        _anim = GetComponent<Animation>();
    }

    private void Update()
    {
        if (!_anim.isPlaying)
            Destroy(gameObject);
    }
}
