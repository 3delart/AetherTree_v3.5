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
//     pendant que le joueur est dans la zone.
//   - A la sortie (ou mort du joueur) : publie un ZoneEvent final
//     avec le temps total passé dans la zone.
//
// AFK/Nuit : PAS de filtre ici — un même ZoneTrigger peut être écouté par
// plusieurs ZoneChecker aux exigences différentes (l'un veut "30s AFK",
// un autre "juste être dans la zone"). Le trigger track 3 compteurs de
// continuité (AFK, nuit, AFK+nuit) en plus du temps brut, et laisse CHAQUE
// ZoneChecker choisir le bon selon ses propres mustBeAFK/atNight — voir
// ZoneChecker.Evaluate().
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

    // ── État interne ──────────────────────────────────────────
    private Player  _player;
    private bool    _playerInZone  = false;
    private float   _timeInZone    = 0f;     // temps brut dans la zone — jamais filtré
    private float   _timeSinceTick = 0f;

    // Continuité — remis à zéro dès que la condition respective casse.
    private float   _continuousAFKTime      = 0f;
    private float   _continuousNightTime    = 0f;
    private float   _continuousAFKNightTime = 0f;

    // ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_playerInZone || _player == null) return;
        if (zoneData == null)                  return;

        _timeInZone    += Time.deltaTime;
        _timeSinceTick += Time.deltaTime;

        bool afk   = _player.IsAFK;
        bool night = DayNightCycle.Instance?.IsNight ?? false;

        _continuousAFKTime      = afk           ? _continuousAFKTime      + Time.deltaTime : 0f;
        _continuousNightTime    = night         ? _continuousNightTime    + Time.deltaTime : 0f;
        _continuousAFKNightTime = (afk && night) ? _continuousAFKNightTime + Time.deltaTime : 0f;

        if (tickIntervalSeconds <= 0f) return;

        if (_timeSinceTick >= tickIntervalSeconds)
        {
            _timeSinceTick = 0f;
            Debug.Log($"[ZONE] Tick publié — zone={zoneData.zoneID} timeInZone={_timeInZone} " +
                      $"continuousAFK={_continuousAFKTime:F0} continuousNight={_continuousNightTime:F0}");
            PublishZoneEvent(isFinalExit: false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ZONE-DEBUG] OnTriggerEnter — other={other.name}, a un Player={other.GetComponent<Player>() != null}");
        var p = other.GetComponent<Player>();
        if (p == null) return;

        _player                 = p;
        _playerInZone           = true;
        _timeInZone             = 0f;
        _timeSinceTick          = 0f;
        _continuousAFKTime      = 0f;
        _continuousNightTime    = 0f;
        _continuousAFKNightTime = 0f;
        Debug.Log($"[ZONE-DEBUG] _playerInZone=true, zoneData={(zoneData != null ? zoneData.zoneID : "NULL")}, tickIntervalSeconds={tickIntervalSeconds}");
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[ZONE-DEBUG] OnTriggerExit — other={other.name}");
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

        _player                 = null;
        _timeInZone             = 0f;
        _timeSinceTick          = 0f;
        _continuousAFKTime      = 0f;
        _continuousNightTime    = 0f;
        _continuousAFKNightTime = 0f;
    }

    private void PublishZoneEvent(bool isFinalExit)
    {
        if (zoneData == null || _player == null) return;

        var e = new ZoneEvent
        {
            zoneID                       = zoneData.zoneID,
            timeSpentSeconds             = _timeInZone,
            totalTimeSeconds             = isFinalExit ? _timeInZone : 0f,
            continuousAFKSeconds         = _continuousAFKTime,
            continuousNightSeconds       = _continuousNightTime,
            continuousAFKAndNightSeconds = _continuousAFKNightTime,
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
