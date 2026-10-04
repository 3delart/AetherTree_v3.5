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
// Brancher sur prefab (un prefab PAR ZONE depuis le 2026-10-04 — plus de ZoneData séparé) :
//   1. Ajouter un SphereCollider (ou BoxCollider) sur le prefab
//   2. Cocher "Is Trigger"
//   3. Ajouter ce composant
//   4. Remplir zoneID/isOutdoor/isDungeon/isPvP directement sur CE prefab
// =============================================================

[RequireComponent(typeof(Collider))]
public class ZoneTrigger : MonoBehaviour
{
    // Remplace l'ancien ZoneData (ScriptableObject séparé, retiré le 2026-10-04 — Florian :
    // "pas un ZoneData mais un prefab") — chaque zone est désormais SON PROPRE prefab (variante de
    // Prefab_ZoneTrigger), ces 4 champs configurés directement dessus. ConditionData/ZoneChecker
    // et QuestObjective référencent ce composant SUR LE PREFAB directement (zonePrefab), pas
    // besoin d'instancier pour lire zoneID — évite tout string tapé à la main en double (zone
    // posée en scène ET zone référencée dans une condition/quête = EXACTEMENT le même asset).
    [Header("Zone")]
    [Tooltip("ID unique de CETTE zone (ce prefab). Ex : 'zone_sous_arbre', 'zone_fontaine_nord'.")]
    public string zoneID = "";
    public bool   isOutdoor = true;
    public bool   isDungeon = false;
    [Tooltip("True = zone PvP activé.")]
    public bool   isPvP     = false;

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

    // Borne le delta utilisé pour les compteurs de zone — évite qu'un alt-tab/veille (deltaTime
    // énorme sur la frame de reprise) ne valide instantanément une condition de continuité.
    private const float MaxZoneDeltaTime = 1f;

    // ─────────────────────────────────────────────────────────

    private void OnEnable()
    {
        GameEventBus.OnPlayerDeath += HandlePlayerDeath;
    }

    private void OnDisable()
    {
        GameEventBus.OnPlayerDeath -= HandlePlayerDeath;
    }

    private void HandlePlayerDeath(PlayerDeathEvent e) => OnPlayerDied();

    /// <summary>
    /// Flush la progression de continuité en cours — appelé par SaveSystem juste avant Save()
    /// au quit, PAS via un OnApplicationQuit local ici : l'ordre d'appel d'OnApplicationQuit
    /// entre composants différents n'est pas garanti par Unity, donc rien ne garantirait que ce
    /// flush arrive avant que SaveSystem lise/écrive la progression des conditions. Sans ça, un
    /// ZoneChecker.onlyFinalExit ne pourrait jamais se valider si la session se termine pendant
    /// que le joueur est encore dans la zone.
    /// </summary>
    public void FlushOnQuit()
    {
        if (_playerInZone) ExitZone();
    }

    private void Update()
    {
        if (!_playerInZone || _player == null) return;
        if (string.IsNullOrEmpty(zoneID))      return;

        float dt = Mathf.Min(Time.deltaTime, MaxZoneDeltaTime);
        _timeInZone    += dt;
        _timeSinceTick += dt;

        bool afk   = _player.IsAFK;
        bool night = DayNightCycle.Instance?.IsNight ?? false;

        _continuousAFKTime      = afk           ? _continuousAFKTime      + dt : 0f;
        _continuousNightTime    = night         ? _continuousNightTime    + dt : 0f;
        _continuousAFKNightTime = (afk && night) ? _continuousAFKNightTime + dt : 0f;

        if (tickIntervalSeconds <= 0f) return;

        if (_timeSinceTick >= tickIntervalSeconds)
        {
            _timeSinceTick = 0f;
            PublishZoneEvent(isFinalExit: false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponent<Player>();
        if (p == null) return;

        _player                 = p;
        _playerInZone           = true;
        _timeInZone             = 0f;
        _timeSinceTick          = 0f;
        _continuousAFKTime      = 0f;
        _continuousNightTime    = 0f;
        _continuousAFKNightTime = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<Player>() != _player) return;
        ExitZone();
    }

    // Appelé via GameEventBus.OnPlayerDeath (voir HandlePlayerDeath) quand le joueur meurt
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
        if (string.IsNullOrEmpty(zoneID) || _player == null) return;

        var e = new ZoneEvent
        {
            zoneID                       = zoneID,
            timeSpentSeconds             = _timeInZone,
            totalTimeSeconds             = isFinalExit ? _timeInZone : 0f,
            continuousAFKSeconds         = _continuousAFKTime,
            continuousNightSeconds       = _continuousNightTime,
            continuousAFKAndNightSeconds = _continuousAFKNightTime,
            isAFK              = _player.IsAFK,
            isDungeon          = isDungeon,
            dungeonSolo        = false,   // géré par DungeonManager si besoin
            dungeonNoHit       = false,
            dungeonTimeSeconds = 0f,
            isFinalExit        = isFinalExit,
        };

        GameEventBus.Publish(e);
    }
}
