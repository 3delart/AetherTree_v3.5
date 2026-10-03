using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// =============================================================
// ACTIVATABLE.CS — Levier/interrupteur générique (on/off), donjon ou monde ouvert
// Path : Assets/Scripts/Events/Activatable.cs
//
// "Chose à actionner" (levier, mécanisme, statue...), utilisable partout — salle de
// donjon comme monde ouvert. Implémente IInteractableBuilding
// (Combat/TargetingSystem.cs), même patron d'interaction que Forge/Puits —
// clic + approche auto, rien de nouveau côté input.
//
// activationTime > 0 : jauge de canalisation (ProgressBarUI, même barre que la récolte),
// annulée si le joueur bouge ou reçoit des dégâts — l'état ne bascule qu'à la fin de la jauge.
//
// Rejouable (toggle) — re-cliquer bascule on/off indéfiniment. Un Portal qui
// dépend de ce levier (Portal.requiredActivatables) référence directement
// CETTE instance (glissée depuis la Hierarchy, même scène) et lit IsOn lui-
// même chaque frame — aucun ID à faire matcher à la main, aucun passage par
// InstanceSession. Un levier repassé Off peut donc redevenir bloquant pour
// un portail qui exigeait On, et inversement (puzzle multi-levier).
// =============================================================
public class Activatable : MonoBehaviour, IInteractableBuilding
{
    [Header("Identité")]
    [Tooltip("Purement cosmétique (tooltip d'interaction affiché au joueur) — plus utilisé pour " +
             "un quelconque matching, voir Portal.requiredActivatables (référence directe).")]
    public string activatableID = "";

    [Header("État initial")]
    public bool startsOn = false;

    [Header("Timer (optionnel)")]
    [Tooltip("0 = reste dans l'état actionné indéfiniment. >0 = repasse Off tout seul après ce " +
             "délai (secondes) — ex: 30 pour un levier qui ne reste actif que 30s.")]
    public float autoOffDelay = 0f;

    [Header("Comportement")]
    [Tooltip("Coché = un seul actionnement possible, l'activable reste On définitivement (mécanisme " +
             "à usage unique). Décoché = toggle rejouable. Sans effet sur autoOffDelay si coché.")]
    public bool oneShot = false;

    [Tooltip("Durée d'activation en secondes (jauge, même barre que la récolte). 0 = instantané. " +
             "Annulée si le joueur bouge ou reçoit des dégâts pendant la jauge — l'état ne change pas.")]
    public float activationTime = 0f;

    [Header("Animator")]
    [Tooltip("Animator.Play() direct, comme Portail/Death — aucune transition à câbler dans le " +
             "Controller.")]
    public Animator animator;
    [Tooltip("State joué à l'activation (ex: clip Blender frames 1→120).")]
    public string onStateName  = "On";
    [Tooltip("State joué à la désactivation. VIDE = pas de clip Off dédié : l'état Off est le " +
             "frame 0 du state On, figé (Animator.speed = 0) — cas d'un mesh statique qui ne " +
             "s'anime qu'une fois activé, sans clip Off séparé à créer.")]
    public string offStateName = "";

    [Header("VFX (optionnel)")]
    [Tooltip("GameObject enfant de la scène (ParticleSystem, VFX Graph...) activé à l'activation, " +
             "désactivé à la désactivation. Laisser désactivé (SetActive false) dans la Hierarchy " +
             "— ce script l'active/désactive lui-même.")]
    public GameObject vfxOnActivate;

    [Header("Interaction — IInteractableBuilding")]
    public float interactionRadius = 2f;
    public float InteractionRadius => interactionRadius;
    public string BuildingName     => activatableID;

    /// <summary>Lu directement par Portal.UnmetCondition() — état courant, pas cumulatif.</summary>
    public bool IsOn => _isOn;

    private bool       _isOn;
    private Coroutine  _autoOffRoutine;

    private bool     _isChanneling;
    private Player   _channelPlayer;
    private Vector3  _channelStartPos;

    // Même seuil que ResourceNode — au-delà, le joueur a réellement bougé.
    private const float CANCEL_MOVE_THRESHOLD = 0.3f;

    private void Start()
    {
        _isOn = startsOn;
        ApplyVisuals(_isOn);
    }

    private void OnDisable()
    {
        if (_isChanneling) CancelChannel();
    }

    private void Update()
    {
        if (!_isChanneling || _channelPlayer == null) return;

        if (Vector3.Distance(_channelPlayer.transform.position, _channelStartPos) > CANCEL_MOVE_THRESHOLD)
            CancelChannel();
    }

    public void Interact(Player player)
    {
        if (_isChanneling) return;
        if (oneShot && _isOn) return;

        if (activationTime <= 0f)
        {
            Toggle();
            return;
        }

        BeginChannel(player);
    }

    private void BeginChannel(Player player)
    {
        if (ProgressBarUI.Instance == null || player == null)
        {
            Toggle();
            return;
        }

        Vector3 dir = transform.position - player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            player.transform.rotation = Quaternion.LookRotation(dir);

        player.GetComponent<NavMeshAgent>()?.ResetPath();

        _isChanneling    = true;
        _channelPlayer   = player;
        _channelStartPos = player.transform.position;
        GameEventBus.OnDamageDealt += OnDamageDealt;

        string label = string.IsNullOrEmpty(activatableID) ? "Activation" : $"Activation — {activatableID}";
        ProgressBarUI.Instance.StartProgress(
            label:        label,
            duration:     activationTime,
            onComplete:   OnChannelComplete,
            onCancel:     EndChannelState,
            type:         ProgressBarUI.BarType.Harvest,
            followTarget: player.transform
        );
    }

    private void OnChannelComplete()
    {
        EndChannelState();
        Toggle();
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_isChanneling && e.target == _channelPlayer && e.amount > 0f)
            CancelChannel();
    }

    /// <summary>Annule la jauge en cours — ProgressBarUI.Cancel() rappelle onCancel
    /// (EndChannelState), pas besoin de nettoyer ici.</summary>
    private void CancelChannel()
    {
        ProgressBarUI.Instance?.Cancel();
        EndChannelState(); // filet si Cancel() était sans effet (barre déjà terminée/volée)
    }

    private void EndChannelState()
    {
        _isChanneling  = false;
        _channelPlayer = null;
        GameEventBus.OnDamageDealt -= OnDamageDealt;
    }

    private void Toggle()
    {
        SetState(!_isOn);

        if (_isOn && !oneShot && autoOffDelay > 0f)
            _autoOffRoutine = StartCoroutine(AutoOffAfterDelay());
    }

    private void SetState(bool isOn)
    {
        // Annonce de groupe — transition OFF→ON pendant un run actif uniquement (voir Florian,
        // spec annonces 2026-09-29 : "Le mécanisme a été activé"). Comparé AVANT d'écraser
        // _isOn — un Activatable hors donjon (il y en a partout dans le monde ouvert) ne doit
        // jamais spammer ce message, d'où la garde CurrentInstance != null.
        if (isOn && !_isOn && InstanceSession.Exists && InstanceSession.Instance.CurrentInstance != null)
            AnnoncePanel.Instance?.Announce("Le mécanisme a été activé");

        _isOn = isOn;
        ApplyVisuals(_isOn);

        if (_autoOffRoutine != null)
        {
            StopCoroutine(_autoOffRoutine);
            _autoOffRoutine = null;
        }
    }

    private void ApplyVisuals(bool isOn)
    {
        if (vfxOnActivate != null) vfxOnActivate.SetActive(isOn);

        if (animator == null) return;

        bool hasOffClip = !string.IsNullOrEmpty(offStateName);

        if (isOn)
        {
            animator.speed = 1f;
            animator.Play(onStateName, 0, 0f);
        }
        else if (hasOffClip)
        {
            animator.speed = 1f;
            animator.Play(offStateName, 0, 0f);
        }
        else
        {
            // Pas de clip Off — frame 0 du state On, figé.
            animator.Play(onStateName, 0, 0f);
            animator.Update(0f);
            animator.speed = 0f;
        }
    }

    private IEnumerator AutoOffAfterDelay()
    {
        yield return new WaitForSeconds(autoOffDelay);
        SetState(false);
    }
}
