using UnityEngine;
using UnityEngine.AI;

// =============================================================
// ResourceNode — Node de ressource collectible dans le monde
// Path : Assets/Scripts/World/ResourceNode.cs
// AetherTree GDD v3.1
//
// Flow :
//   Clic → TargetingSystem détecte → BeginCollect si à portée
//   → joueur se tourne vers le node + stop mouvement
//   → ProgressBarUI (collectTime secondes)
//   → si joueur bouge → annulation automatique
//   → fin : InventorySystem.AddItem() + FloatingText
//   → node désactivé + callback SpawnManager (respawn)
// =============================================================

public class ResourceNode : MonoBehaviour
{
    [Header("Data")]
    public ResourceData data;

    [Header("Mode")]
    [Tooltip("True = placé manuellement. False = géré par SpawnManager.")]
    public bool isFixed = false;

    [Tooltip("Fourchette de délai avant respawn de CE node fixe, en secondes — pas sur " +
             "ResourceData : la même ressource peut respawn plus vite ou plus lentement selon la " +
             "map/l'endroit où elle est placée. Min = Max donne un délai fixe. Roulé à chaque " +
             "épuisement. Sans effet si isFixed = false (SpawnManager utilise ResourceData." +
             "respawnDelay pour son propre respawn en zone).")]
    [Min(1f)] public float minRespawnDelay = 300f;
    [Min(1f)] public float maxRespawnDelay = 300f;

    [Header("Remplissage progressif (optionnel — ex: puits)")]
    [Tooltip("Coché : au lieu de disparaître (SetActive false) entre deux récoltes, le node reste " +
             "actif/visible — figé vide (frame 0) pendant tout le délai de respawn roulé, PUIS " +
             "joue l'anim de remplissage à vitesse normale, non récoltable tant qu'elle n'est " +
             "pas terminée. Démarre plein au premier chargement (voir Start()). isFixed " +
             "uniquement (un puits est toujours placé à la main, jamais géré par SpawnManager).")]
    public bool hasFillAnimation = false;

    [Tooltip("Animator qui joue le clip de remplissage. Laisser vide = auto-résolu sur ce " +
             "GameObject au premier Awake().")]
    public Animator animator;

    [Tooltip("Nom du state Animator à jouer pour le remplissage (doit avoir Loop Time DÉCOCHÉ — " +
             "sinon il boucle et repart à vide au lieu de rester figé sur la pose pleine).")]
    public string fillStateName = "Fill";

    [Tooltip("Durée réelle du clip, à sa vitesse normale (Animator.speed = 1), en secondes — " +
             "anime-le à la durée qui te semble naturelle, aucun rapport avec le délai de " +
             "respawn. Sert juste à savoir quand l'anim est terminée : le node reste vide/statique " +
             "pendant tout le délai roulé, PUIS joue le clip à vitesse normale, et devient " +
             "récoltable une fois le clip fini (délai + cette durée, pas juste le délai).")]
    [Min(0.01f)] public float fillClipLength = 2f;

    // ── Runtime ──────────────────────────────────────────────
    private bool          _isExhausted = false;
    private bool          _isCollecting = false;
    private Player        _collectingPlayer;
    private Vector3       _playerPosAtCollectStart;
    private System.Action _onExhaustedCallback;

    // Distance max que le joueur peut bouger avant annulation
    private const float CANCEL_MOVE_THRESHOLD = 0.3f;

    private void Awake()
    {
        if (hasFillAnimation && animator == null) animator = GetComponent<Animator>();
    }

    private void Start()
    {
        // Un puits démarre TOUJOURS plein/prêt (Florian, 2026-09-29 — pas de mémoire d'état
        // entre sessions, sujet serveur pas encore abordé sur aucun système du projet). Force
        // le clip sur sa dernière frame (pose pleine) plutôt que de laisser l'Animator jouer son
        // state par défaut depuis 0 tout seul au chargement.
        if (hasFillAnimation && animator != null)
            animator.Play(fillStateName, 0, 1f);
    }

    // =========================================================
    // INIT PAR SPAWNMANAGER
    // =========================================================

    public void InitFromSpawner(ResourceData resourceData, System.Action onExhausted)
    {
        data                 = resourceData;
        _onExhaustedCallback = onExhausted;
        _isExhausted         = false;
        isFixed              = false;
    }

    // =========================================================
    // UPDATE — vérifie si le joueur a bougé pendant la collecte
    // =========================================================

    private void Update()
    {
        if (!_isCollecting || _collectingPlayer == null) return;

        float moved = Vector3.Distance(
            _collectingPlayer.transform.position,
            _playerPosAtCollectStart);

        if (moved > CANCEL_MOVE_THRESHOLD)
        {
            // Joueur a bougé → annule
            _isCollecting = false;
            ProgressBarUI.Instance?.Cancel();
            Debug.Log("[ResourceNode] Collecte annulée — joueur en mouvement.");
        }
    }

    // =========================================================
    // COLLECTE
    // =========================================================

    public void BeginCollect(Player player)
    {
        if (_isExhausted || data == null || player == null) return;
        if (_isCollecting) return; // déjà en cours

        // Tourne le joueur vers le node
        Vector3 dir = transform.position - player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            player.transform.rotation = Quaternion.LookRotation(dir);

        // Arrête le mouvement
        player.GetComponent<NavMeshAgent>()?.ResetPath();

        // Mémorise la position de départ pour détecter le mouvement
        _isCollecting             = true;
        _collectingPlayer         = player;
        _playerPosAtCollectStart  = player.transform.position;

        ProgressBarUI.Instance?.StartProgress(
            label:        $"Récolte — {data.displayName.Get(LocalizationManager.CurrentLanguage)}",
            duration:     data.collectTime,
            onComplete:   () => OnCollectComplete(player),
            onCancel:     () => _isCollecting = false,
            type:         ProgressBarUI.BarType.Harvest,
            followTarget: player.transform
        );
    }

    private void OnCollectComplete(Player player)
    {
        _isCollecting = false;
        if (_isExhausted) return;

        int qty  = Random.Range(data.minQuantity, data.maxQuantity + 1);
        bool added = InventorySystem.Instance?.AddItem(
            new InventoryItem(data.CreateInstance(qty))) ?? false;

        if (added)
        {
            FloatingText.Spawn(
                $"+{qty} {data.displayName.Get(LocalizationManager.CurrentLanguage)}",
                transform.position + Vector3.up * 1.5f,
                new Color(0.8f, 0.65f, 0.2f));

            GameEventBus.Publish(new ItemEvent
            {
                itemID   = data.itemID,
                itemName = data.displayName.Get(LocalizationManager.CurrentLanguage),
                action   = ItemAction.Pickup,
                quantity = qty,
            });
        }
        else
            FloatingText.Spawn("Inventaire plein !",
                transform.position + Vector3.up * 1.5f, Color.red);

        Exhaust();
        TargetPanel.Instance?.Hide();
    }

    // =========================================================
    // ÉPUISEMENT
    // =========================================================

    private void Exhaust()
    {
        _isExhausted = true;

        if (isFixed && hasFillAnimation)
        {
            // Reste actif/visible — BeginCollect() est déjà bloqué par _isExhausted, pas besoin
            // de désactiver le GameObject. Fige direct sur la frame 0 (vide) à la récolte —
            // sinon la pose PLEINE resterait affichée (figée depuis Start()/le cycle précédent)
            // pendant tout le délai, avant même que l'anim de remplissage démarre. Statique vide
            // pendant le délai, PUIS l'anim se joue à vitesse normale — récoltable seulement une
            // fois le clip terminé (délai + fillClipLength au total, pas juste le délai).
            // speed = 0 : Play() seul relancerait la lecture immédiatement à vitesse normale,
            // ce qui ferait avancer l'anim PENDANT le délai au lieu de rester figée vide.
            if (animator != null)
            {
                animator.Play(fillStateName, 0, 0f);
                animator.speed = 0f;
            }

            float delay = Random.Range(minRespawnDelay, maxRespawnDelay);
            Invoke(nameof(PlayFillAnimation), delay);
            Invoke(nameof(Restore), delay + fillClipLength);
        }
        else if (isFixed)
        {
            gameObject.SetActive(false);
            Invoke(nameof(Restore), Random.Range(minRespawnDelay, maxRespawnDelay));
        }
        else
        {
            gameObject.SetActive(false);
            _onExhaustedCallback?.Invoke();
        }
    }

    private void PlayFillAnimation()
    {
        if (animator == null) return;
        animator.speed = 1f;
        animator.Play(fillStateName, 0, 0f);
    }

    private void Restore()
    {
        _isExhausted = false;
        if (!hasFillAnimation) gameObject.SetActive(true);
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = new Color(0.8f, 0.65f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, data.interactionRadius);
    }
}
