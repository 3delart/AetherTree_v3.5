// =============================================================
// ANIMATIONTYPE — catégorie de rig pour le preset d'animations locomotion
// Path : Assets/Scripts/Data/Mobs/AnimationType.cs
//
// Utilisé par MobData ET PNJData (voir MobAnimationPresets) pour pré-remplir
// idleClip/idleClipVariants/walkClip/chaseClip/deathClip selon le squelette.
// Humanoid retargete librement (Avatar Mecanim) — un preset Humanoid marche
// sur n'importe quel mob/PNJ humanoid quel que soit son mesh. Quadruped/Autre
// n'ont PAS ce luxe (rig Generic, pas de retargeting) : leur preset ne vaudra
// que pour des mobs partageant EXACTEMENT le même squelette source — à
// enrichir au cas par cas quand Florian aura ses propres rigs quadruped.
//
// Ordinal safety : ajouter toute nouvelle catégorie en fin de liste.
// =============================================================

public enum AnimationType
{
    Humanoid,
    Quadruped,
    Autre,

    // Objet/mob fixe qui ne doit JAMAIS recevoir de clip (ex: mannequin d'entraînement — pas de
    // déplacement, pas de chase, pas de ciblage, rien à animer) — distinct de Autre (catégorie en
    // attente de contenu). MobData/PNJData.OnValidate() n'auto-fill RIEN pour ce type, et ne
    // relève pas non plus l'absence de clips comme un oubli.
    None,
}
