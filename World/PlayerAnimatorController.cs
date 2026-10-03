using UnityEngine;
using UnityEngine.AI;

// =============================================================
// PLAYERANIMATORCONTROLLER.CS — Pilotage de l'Animator du joueur
// Path : Assets/Scripts/World/PlayerAnimatorController.cs
//
// Locomotion : paramètre "Speed" (float) piloté par NavMeshAgent.velocity —
// l'Animator Controller définit les états Idle/running (désarmé) et "idle
// with weapon"/"running with weapon" (armé, transition depuis Any State dès
// qu'une arme est équipée — pas de logique ici, c'est le graphe qui décide).
// "InCombat" (bool, piloté par Player.CombatActive) reste pour compat avec
// un éventuel usage futur dans le graphe, mais ne pilote plus aucun swap de
// clip côté script — idle "en combat" et "idle with weapon" sont le même
// concept depuis la refonte du graphe (demande Florian), voir
// RefreshWeaponAnimation() pour la variante par arme.
//
// Attaque : un seul état "Attack" réutilisable dans l'Animator Controller
// (Motion placeholder au départ) — le clip réel est injecté à la volée via
// AnimatorOverrideController selon SkillData.animationClip, pour ne pas
// avoir à ajouter un state par skill à la main (des dizaines/centaines de
// skills à terme — voir a implémenter, décision prise avec l'utilisateur).
// =============================================================

[RequireComponent(typeof(Animator))]
public class PlayerAnimatorController : MonoBehaviour
{
    private const string SpeedParam       = "Speed";
    private const string InCombatParam    = "InCombat";
    private const string AttackState      = "Attack";
    // Death est un state SÉPARÉ d'Attack — pas de transition de sortie automatique, il doit
    // tenir la pose indéfiniment jusqu'à ce que le joueur soit revive (contrairement à Attack qui
    // retourne seul à la locomotion). Configuré manuellement dans l'Animator Controller par
    // Florian, même graphe que Attack.
    private const string DeathState       = "Death";
    private const string CancelActionTrigger = "CancelAction";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans le\n" +
             "Animator Controller (un placeholder, ex: un clip vide dédié — pas Happy Idle\n" +
             "réutilisé, pour éviter de confondre avec le vrai state Idle). AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE du state, pas par nom de state — sans cette\n" +
             "référence exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    [Header("Idle hors combat (variantes aléatoires)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Happy Idle\" (InCombat=false)\n" +
             "dans l'Animator Controller — même principe que attackPlaceholderClip, sert de clé\n" +
             "pour l'indexeur AnimatorOverrideController. Si non assigné, idleClips ci-dessous ne\n" +
             "pourra jamais s'échanger (le state garde son clip d'origine).")]
    [SerializeField] private AnimationClip idlePlaceholderClip;
    [Tooltip("Pool de clips Idle hors combat — une variante est tirée au hasard à chaque retour au\n" +
             "repos (Speed repasse à 0). Optionnel : vide ou null = pas de variation.")]
    [SerializeField] private AnimationClip[] idleClips;

    [Header("Idle/Running armé (par type d'arme)")]
    [Tooltip("Asset listant, par famille de WeaponType, les clips \"idle with weapon\"/\"running " +
             "with weapon\" — voir PlayerWeaponAnimationPresets. Référence directe (pas de " +
             "singleton, un seul consommateur).")]
    [SerializeField] private PlayerWeaponAnimationPresets weaponAnimationPresets;
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"idle with weapon\" dans le\n" +
             "Animator Controller — sert de clé pour l'indexeur AnimatorOverrideController, même\n" +
             "principe qu'attackPlaceholderClip.")]
    [SerializeField] private AnimationClip idleWithWeaponPlaceholderClip;
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"running with weapon\" dans le\n" +
             "Animator Controller — même principe.")]
    [SerializeField] private AnimationClip runningWithWeaponPlaceholderClip;

    [Header("Death (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Death\" dans le\n" +
             "Animator Controller (placeholder dédié, même principe que attackPlaceholderClip\n" +
             "ci-dessus — sert de clé pour l'indexeur AnimatorOverrideController).")]
    [SerializeField] private AnimationClip deathPlaceholderClip;
    [Tooltip("Clip de mort réel du joueur — UN SEUL clip pour tout le personnage (contrairement à\n" +
             "Attack qui varie par skill), joué via PlayDeath() sans paramètre. Optionnel : si\n" +
             "null, PlayDeath() ne fait rien (pas d'anim de mort tant qu'il n'est pas assigné).")]
    [SerializeField] private AnimationClip deathClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private Player                      _player;

    // true au départ — fait déclencher le tirage d'une variante Idle dès le premier Update() si
    // le joueur démarre déjà à l'arrêt, sans dupliquer la logique de tirage à part (même patron
    // que CombatEntityAnimatorController._wasMoving).
    private bool _wasMoving = true;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _player   = GetComponent<Player>();

        // Enveloppe le Controller de base dans un Override — permet d'échanger le clip
        // du state "Attack" à la volée sans toucher au graphe. Si aucun Controller n'est
        // encore assigné (mannequin pas encore équipé de son Animator Controller), on
        // n'enveloppe rien — PlayAttack() ne fera rien tant que ce sera le cas.
        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[PlayerAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;

        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);
        _animator.SetBool(InCombatParam, _player != null && _player.CombatActive);

        // Tirage d'une variante Idle au moment précis où on repasse à l'arrêt (front descendant
        // Speed>0 → Speed≈0) — pas à chaque frame immobile, pas de Play() forcé non plus : juste
        // remplacer le contenu du slot AVANT que l'Animator ne transite lui-même vers Idle, pour
        // ne pas casser le blend Walk→Idle existant avec un Play(0,0f) qui coupe net.
        bool isMoving = speed > 0.05f;
        if (_wasMoving && !isMoving) SwapIdleClip();
        _wasMoving = isMoving;
    }

    /// <summary>Tire une variante Idle au hasard et la place dans le slot placeholder "Idle" (state
    /// désarmé uniquement — "idle with weapon" n'a pas de variantes, il suit l'arme équipée via
    /// RefreshWeaponAnimation, voir plus bas : "idle en combat" et "idle with weapon" sont le même
    /// concept depuis la refonte du graphe, demande Florian) — sans relancer le state (contrairement
    /// à PlayOverrideClip), pour laisser l'Animator transiter naturellement vers Idle avec son
    /// propre blend. No-op si le placeholder ou le pool ne sont pas configurés.</summary>
    private void SwapIdleClip()
    {
        if (_overrideController == null) return;
        if (idlePlaceholderClip == null || idleClips == null || idleClips.Length == 0) return;

        AnimationClip chosen = idleClips[Random.Range(0, idleClips.Length)];
        if (chosen != null) _overrideController[idlePlaceholderClip] = chosen;
    }

    /// <summary>Échange le clip d'un slot placeholder puis relance le state associé depuis le
    /// début — généralisé pour servir Attack (placeholder/state réutilisés à chaque cast) ET
    /// Death (placeholder/state séparés, un seul clip fixe, joués une seule fois à la mort).
    /// placeholderSlotKey DOIT être le clip D'ORIGINE du slot (clé de l'indexeur
    /// AnimatorOverrideController, pas le clip de destination).</summary>
    private void PlayOverrideClip(AnimationClip clip, AnimationClip placeholderSlotKey, string stateName)
    {
        if (clip == null || _overrideController == null || placeholderSlotKey == null) return;

        // Indexation par référence au clip D'ORIGINE du slot, pas par nom de state — voir les
        // commentaires sur attackPlaceholderClip/deathPlaceholderClip ci-dessus.
        _overrideController[placeholderSlotKey] = clip;

        // Reset du trigger CancelAction avant de rejouer le state — sinon un
        // trigger posé par CancelChannel() qui n'a jamais trouvé de transition à
        // consommer (ex: l'Animator était déjà revenu en locomotion) resterait en
        // attente et se déclencherait au prochain re-entré dans Attack, coupant net
        // un skill qui n'a rien à voir avec l'interrupt précédent.
        _animator.ResetTrigger(CancelActionTrigger);
        _animator.Play(stateName, 0, 0f);
    }

    /// <summary>
    /// Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Player.UseSkill().
    /// Ne fait rien si le skill n'a pas d'animationClip assignée (ex: buff pur) ou si
    /// le Controller n'est pas encore prêt.
    /// </summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip, attackPlaceholderClip, AttackState);

    /// <summary>Échange les clips "idle with weapon"/"running with weapon" selon la famille de
    /// l'arme équipée — PAS de relance de state (contrairement à PlayOverrideClip) : la
    /// locomotion ne doit jamais couper net, l'Animator transite vers ces states tout seul selon
    /// Speed/InCombat, exactement comme SwapIdleClip(). Appelé par Player.EquipWeapon()/
    /// UnequipWeapon() — PAS à chaque frame, seulement quand l'arme change. weaponType =
    /// WeaponType.UnArmed si désarmé (GetPreset retourne alors le preset UnArmed s'il existe,
    /// ou null → no-op, les states gardent leur dernier clip armé, inoffensif tant qu'ils ne sont
    /// pas joués côté désarmé — voir le graphe Animator pour la transition réelle).</summary>
    public void RefreshWeaponAnimation(WeaponType weaponType)
    {
        if (_overrideController == null || weaponAnimationPresets == null) return;

        var preset = weaponAnimationPresets.GetPreset(weaponType);
        if (preset == null) return;

        if (idleWithWeaponPlaceholderClip != null && preset.idleWithWeaponClip != null)
            _overrideController[idleWithWeaponPlaceholderClip] = preset.idleWithWeaponClip;
        if (runningWithWeaponPlaceholderClip != null && preset.runningWithWeaponClip != null)
            _overrideController[runningWithWeaponPlaceholderClip] = preset.runningWithWeaponClip;
    }

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (override du state "Attack" réutilisable). Appelé par
    /// SkillBar.StartChannel().</summary>
    public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip, attackPlaceholderClip, AttackState);

    /// <summary>Joue l'animation de mort du joueur — échange le clip du state "Death" séparé
    /// (jamais réutilisé par Attack/Channel) avec le deathClip fixe assigné en Inspector, puis le
    /// lance depuis le début. Sans paramètre (contrairement à PlayAttack/PlayChannel) : un seul
    /// clip pour tout le personnage, pas un clip par skill — voir le commentaire sur deathClip.
    /// No-op si deathClip n'est pas assigné. Appelé par Player.Die() (branches donjon ET open
    /// world).</summary>
    public void PlayDeath() => PlayOverrideClip(deathClip, deathPlaceholderClip, DeathState);

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion, ne laisse jamais le clip jouer jusqu'au bout après un interrupt.
    /// Appelé par SkillBar.InterruptChannel().</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de
    /// lecture (state "Attack"). hitIndex : 0 par défaut (skills à un seul coup — Normal,
    /// chaque step de Combo), ou l'index du hit pour un MultiHit (0 = coup de base, 1..N =
    /// hitSteps). Relais pur — toute la logique de résolution vit dans SkillBar, qui possède
    /// déjà tout l'état de lancement (slot, skill, target, verrous).</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        SkillBar.Instance?.OnAnimationHitEvent(hitIndex);
    }

    // Hooks vides pour les Animation Events "FootStep"/"PlayFootStep" déjà posés aux bons frames
    // sur les clips de marche/course repris d'AnyRPG — sans ces méthodes, Unity logue juste un
    // warning "no receiver" (l'anim joue quand même, inoffensif). Prêts pour un futur système de
    // son de pas, pas de logique pour l'instant.
    public void FootStep()     { }
    public void PlayFootStep() { }

    // Même principe pour "AnimationPrefabCreate"/"StartAudio"/"AnimationPrefabDestroy" — baked
    // par AnyRPG sur ses clips de skill (ex: AnyRPGShootBowAndArrow, flèche/son/cleanup de VFX
    // propres à leur système). Aucun paramètre posé sur ces events dans nos clips copiés (tous
    // à 0/null) — hooks vides, prêts si on branche un jour nos propres VFX/SFX dessus.
    public void AnimationPrefabCreate()  { }
    public void StartAudio()             { }
    public void AnimationPrefabDestroy() { }
}
