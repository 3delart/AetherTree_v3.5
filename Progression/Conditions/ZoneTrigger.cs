using UnityEngine;

// =============================================================
// ZONETRIGGER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Zone/ZoneTrigger.cs
//
// Composant à accrocher sur n'importe quel prefab (arbre, fontaine,
// statue...). Nécessite un Collider en mode Trigger sur le même GO
// ou un enfant.
//
// Comportement :
//   - Quand un joueur entre dans le collider, le timer démarre.
//   - Tick périodique : publie un ZoneEvent toutes les tickIntervalSeconds
//     pendant que le joueur est dans la zone (et que les conditions
//     AFK/nuit sont remplies si checkAFK/checkNight sont actifs).
//   - A la sortie (ou mort du joueur) : publie un ZoneEvent final
//     avec le temps total passé dans la zone.
//
// Brancher sur prefab :
//   1. Ajouter un SphereCollider (ou BoxCollider) sur le prefab
//   2. Cocher "Is Trigger"
//   3. Ajouter ce composant
//   4. Assigner le ZoneData correspondant
// =============================================================

[RequireComponent(typeof(Collider))]
public class ZoneTrigger : MonoBehaviour
{
    [Header("Zone")]
    public ZoneData zoneData;

    [Header("Tick")]
    [Tooltip("Intervalle en secondes entre chaque ZoneEvent de tick. 0 = pas de tick.")]
    public float tickIntervalSeconds = 60f;

    [Header("Filtres de tick")]
    [Tooltip("Si true, le tick ne compte que si le joueur est AFK")]
    public bool requireAFK   = false;
    [Tooltip("Si true, le tick ne compte que si c'est la nuit")]
    public bool requireNight = false;

    // ── État interne ──────────────────────────────────────────
    private Player  _player;
    private bool    _playerInZone  = false;
    private float   _timeInZone    = 0f;     // secondes passées dans la zone
    private float   _timeSinceTick = 0f;

    // ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_playerInZone || _player == null) return;
        if (zoneData == null)                  return;

        _timeInZone    += Time.deltaTime;
        _timeSinceTick += Time.deltaTime;

        if (tickIntervalSeconds <= 0f) return;

        bool tickConditionsMet = true;
        if (requireAFK   && !_player.IsAFK)                              tickConditionsMet = false;
        if (requireNight && !(DayNightCycle.Instance?.IsNight ?? false)) tickConditionsMet = false;

        // Si les conditions ne sont pas remplies, remet les compteurs à zéro
        // pour que la durée soit comptée d'affilée (ex: 30s AFK sans interruption)
        if (!tickConditionsMet)
        {
            _timeInZone    = 0f;
            _timeSinceTick = 0f;
            return;
        }

        if (_timeSinceTick >= tickIntervalSeconds)
        {
            _timeSinceTick = 0f;
            Debug.Log($"[ZONE] Tick publié — zone={zoneData.zoneID} timeInZone={_timeInZone} isAFK={_player.IsAFK}");
            PublishZoneEvent(isFinalExit: false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponent<Player>();
        if (p == null) return;

        _player        = p;
        _playerInZone  = true;
        _timeInZone    = 0f;
        _timeSinceTick = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<Player>() != _player) return;
        ExitZone();
    }

    // Appelé par Player.Die() si le joueur meurt dans la zone
    public void OnPlayerDied()
    {
        if (_playerInZone) ExitZone();
    }

    private void ExitZone()
    {
        _playerInZone = false;

        // Event de sortie avec durée totale
        if (_timeInZone > 0f)
            PublishZoneEvent(isFinalExit: true);

        _player        = null;
        _timeInZone    = 0f;
        _timeSinceTick = 0f;
    }

    private void PublishZoneEvent(bool isFinalExit)
    {
        if (zoneData == null || _player == null) return;

        var e = new ZoneEvent
        {
            zoneID             = zoneData.zoneID,
            timeSpentSeconds   = _timeInZone,
            totalTimeSeconds   = isFinalExit ? _timeInZone : 0f,
            isAFK              = _player.IsAFK,
            isDungeon          = zoneData.isDungeon,
            dungeonSolo        = false,   // géré par DungeonManager si besoin
            dungeonNoHit       = false,
            dungeonTimeSeconds = 0f,
            isFinalExit        = isFinalExit,
        };

        GameEventBus.Publish(e);
    }
}
