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

    // ── Runtime ──────────────────────────────────────────────
    private bool          _isExhausted = false;
    private bool          _isCollecting = false;
    private Player        _collectingPlayer;
    private Vector3       _playerPosAtCollectStart;
    private System.Action _onExhaustedCallback;

    // Distance max que le joueur peut bouger avant annulation
    private const float CANCEL_MOVE_THRESHOLD = 0.3f;

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
            FloatingText.Spawn(
                $"+{qty} {data.displayName.Get(LocalizationManager.CurrentLanguage)}",
                transform.position + Vector3.up * 1.5f,
                new Color(0.8f, 0.65f, 0.2f));
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

        if (isFixed)
        {
            gameObject.SetActive(false);
            Invoke(nameof(Restore), data?.respawnDelay ?? 30f);
        }
        else
        {
            gameObject.SetActive(false);
            _onExhaustedCallback?.Invoke();
        }
    }

    private void Restore()
    {
        _isExhausted = false;
        gameObject.SetActive(true);
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
