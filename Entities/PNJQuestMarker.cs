using UnityEngine;

// =============================================================
// PNJQUESTMARKER — icône !/? au-dessus de la tête d'un PNJ selon ses quêtes
// Path : Assets/Scripts/Entities/PNJQuestMarker.cs
// AetherTree GDD v31 — §25
//
// Composant séparé de PNJ.cs (déjà volumineux) — à ajouter sur le même GameObject que PNJ.
// Florian construit l'icône world-space (billboard au-dessus de la tête) et assigne les 2
// GameObjects ci-dessous en Inspector.
//
// Priorité d'affichage (une seule icône visible à la fois) :
//   une quête Completed (à rendre)                                  → ? (turnInIcon)
//   sinon une quête CanAccept (disponible, incl. Secret débloquée)   → ! (availableIcon)
//   sinon                                                            → rien
//
// Une quête Secret encore verrouillée (CanAccept faux) ne déclenche PAS le ! — reste invisible
// tant que son vrai prérequis n'est pas rempli (même règle que PNJQuestBoardUI).
//
// Scène-local (pas un singleton) — Subscribe/Unsubscribe via OnEnable/OnDisable, PAS via le
// pattern Resubscribe()+GameEventBus.Reset() des UI persistantes : ce composant est détruit et
// recréé à chaque changement de scène avec le PNJ lui-même, OnEnable suffit.
// =============================================================

public class PNJQuestMarker : MonoBehaviour
{
    [Header("Icônes (world-space, au-dessus de la tête)")]
    public GameObject availableIcon;
    public GameObject turnInIcon;

    private PNJ    _pnj;
    private Player _player;
    private bool   _dirty = true;

    private void Awake()
    {
        _pnj = GetComponent<PNJ>();
    }

    private void OnEnable()
    {
        GameEventBus.OnQuestAction += OnQuestAction;
        GameEventBus.OnMobKilled   += OnMobKilled;
        _dirty = true;
    }

    private void OnDisable()
    {
        GameEventBus.OnQuestAction -= OnQuestAction;
        GameEventBus.OnMobKilled   -= OnMobKilled;
    }

    private void OnQuestAction(QuestEvent e) => _dirty = true;
    private void OnMobKilled(MobKilledEvent e) => _dirty = true;

    private void Update()
    {
        if (!_dirty) return;
        _dirty = false;
        Refresh();
    }

    private void Refresh()
    {
        if (availableIcon == null && turnInIcon == null) return;
        if (_player == null) _player = FindObjectOfType<Player>();

        bool showTurnIn = false, showAvailable = false;

        if (_pnj?.data?.availableQuests != null && QuestSystem.Instance != null && _player != null)
        {
            foreach (var quest in _pnj.data.availableQuests)
            {
                if (quest == null) continue;
                var state = QuestSystem.Instance.GetEffectiveState(quest, _player);

                if (state == QuestState.Completed) { showTurnIn = true; break; }
                if (state == QuestState.None && QuestSystem.Instance.CanAccept(quest, _player))
                    showAvailable = true;
            }
        }

        turnInIcon?.SetActive(showTurnIn);
        availableIcon?.SetActive(!showTurnIn && showAvailable);
    }
}
