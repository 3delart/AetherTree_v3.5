using UnityEngine;
using TMPro;
using System.Collections;

// =============================================================
// ANNONCEPANEL.CS — Annonce globale, un seul TMP
// Path : Assets/Scripts/UI/PanelFixe/AnnoncePanel.cs
//
// Flux unique pour TOUT type d'annonce (mort d'allié, objectif de donjon,
// portail ouvert, réussite de donjon, countdown d'event, message serveur...)
// — même style/couleur pour tout, seul le texte change (Florian, 2026-09-29).
// Un nouvel appel à Announce() ÉCRASE l'affichage en cours et relance le
// timer, pas de file d'attente. Désactivé par défaut, fondu in/out via un
// CanvasGroup ajouté automatiquement sur le GameObject du texte.
//
// Portée : le panel lui-même ne distingue jamais "annonce de groupe donjon"
// vs "annonce globale à tous les joueurs" — en solo ça n'a aucun sens
// d'incarner cette différence côté client (il n'y a jamais qu'un seul
// joueur qui regarde son propre écran). Cette distinction est un futur
// souci RÉSEAU (qui reçoit le message), pas un souci d'affichage.
// =============================================================

public class AnnoncePanel : MonoBehaviour
{
    public static AnnoncePanel Instance { get; private set; }

    [Tooltip("Le GameObject visuel à afficher/cacher (fond + texte) — DOIT être un parent du " +
             "texte, jamais le texte lui-même : sinon un éventuel fond (Image) posé sur ce " +
             "parent resterait visible en permanence, seul le texte se cacherait.")]
    public GameObject panelRoot;
    public TextMeshProUGUI text;
    public float displayDuration = 5f;
    public float fadeDuration    = 0.3f;

    private CanvasGroup _canvasGroup;
    private float       _hideAt;
    private Coroutine   _routine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panelRoot != null)
        {
            _canvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = panelRoot.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            panelRoot.SetActive(false);
        }
    }

    public void Announce(string message)
    {
        if (text == null || string.IsNullOrEmpty(message)) return;

        text.text = message;
        _hideAt   = Time.unscaledTime + displayDuration;

        // Un nouvel appel ÉCRASE l'affichage en cours (pas de file) — coupe la routine en cours
        // (fondu out inclus) et repart d'où l'alpha en est, pour ne jamais sauter brutalement à 0
        // si une annonce en interrompt une autre en plein fondu.
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        yield return FadeTo(1f, fadeDuration);

        while (Time.unscaledTime < _hideAt)
            yield return null;

        yield return FadeTo(0f, fadeDuration);
        if (panelRoot != null) panelRoot.SetActive(false);
        _routine = null;
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        if (_canvasGroup == null) yield break;
        float start = _canvasGroup.alpha;
        if (duration <= 0f) { _canvasGroup.alpha = target; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        _canvasGroup.alpha = target;
    }
}
