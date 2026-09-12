using UnityEngine;

// =============================================================
// PLAYALLANIMATIONCLIPS.CS — Joue TOUS les clips d'un composant Animation
// (legacy) en même temps au lieu d'un seul.
// Path : Assets/Scripts/Utils/PlayAllAnimationClips.cs
//
// Cas d'usage : un prefab VFX importé d'un FBX où plusieurs objets ont
// chacun leur propre Action Blender (donc plusieurs AnimationClip côté
// Unity) — ex: 3 anneaux d'un pentacle qui tournent chacun à sa vitesse/
// son sens (Circle_2 en sens inverse des deux autres). "Play Automatically"
// sur le composant Animation ne joue QUE le clip du champ "Animation"
// (le premier), les autres restent immobiles.
//
// Assigne un layer distinct à chaque clip trouvé — Play() n'arrête que les
// clips du MÊME layer, donc les mettre sur des layers différents les fait
// jouer tous en parallèle sans interférence, peu importe ce qu'ils animent
// chacun.
//
// Ne force AUCUN wrapMode — chaque clip garde celui réglé à l'import (case
// "Loop Time" par clip, onglet Animation du FBX). Un effet ponctuel (ex:
// pentacle qui s'ouvre → tourne → se referme) doit rester non-bouclé pour
// avoir une vraie fin — voir DestroySelfWhenAnimationFinished.cs pour le
// détruire proprement à ce moment-là plutôt qu'après une durée devinée.
// =============================================================

[RequireComponent(typeof(Animation))]
public class PlayAllAnimationClips : MonoBehaviour
{
    private void Start()
    {
        Animation anim = GetComponent<Animation>();
        int layer = 0;

        foreach (AnimationState state in anim)
        {
            state.layer = layer++;
            anim.Play(state.name);
        }
    }
}
