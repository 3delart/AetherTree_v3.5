using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// =============================================================
// DRAGGHOST.CS — Ghost de drag-and-drop partagé, toutes sources confondues
// Path : Assets/Scripts/UI/Shared/DragGhost.cs
//
// Remplace la mécanique de ghost dupliquée dans 5 classes (SkillDragSource,
// PassiveDragSource, EquipmentSlotHandler, InventoryItemCell,
// StagedItemDragSource) — un seul GameObject créé une fois (jamais détruit),
// juste activé/désactivé à chaque drag. Ne porte AUCUNE logique de "qu'est-ce
// qui est dragué" ni de compatibilité/action — ça reste entièrement à la charge
// de chaque source/cible, comme avant. Ce composant ne fait QUE le transport
// visuel (icône qui suit la souris).
//
// Geste conservé : presser-glisser-relâcher standard Unity (IBeginDragHandler/
// IDragHandler/IEndDragHandler) — PAS le modèle "clic-ramasser/clic-poser"
// d'AnyRPG, décision explicite de Florian (2026-10-02).
// =============================================================
public class DragGhost : MonoBehaviour
{
    private static DragGhost _instance;

    private static DragGhost Instance
    {
        get
        {
            if (_instance == null)
            {
                // FindObjectOfType<Canvas> seul peut remonter N'IMPORTE QUEL Canvas actif —
                // notamment un Canvas World Space temporaire (ex: ProgressBarPrefab.prefab,
                // instancié en jeu par ProgressBarUI) si un est vivant au moment du premier
                // drag, ce qui parenterait le ghost dans la scène 3D au lieu de l'UI (invisible
                // à l'écran). Filtre sur isRootCanvas + renderMode != WorldSpace, garde celui au
                // sortingOrder le plus élevé s'il y en a plusieurs — trouvé en revue de code,
                // 2026-10-02.
                Canvas canvas = null;
                foreach (Canvas c in FindObjectsOfType<Canvas>())
                {
                    if (c.renderMode == RenderMode.WorldSpace) continue;
                    if (!c.isRootCanvas) continue;
                    if (canvas == null || c.sortingOrder > canvas.sortingOrder) canvas = c;
                }
                if (canvas == null)
                {
                    Debug.LogError("[DragGhost] Aucun Canvas UI (non World Space) trouvé dans " +
                                    "la scène — impossible d'afficher le ghost de drag.");
                    return null;
                }

                var go = new GameObject("DragGhost (auto)");
                go.transform.SetParent(canvas.transform, false);

                _instance = go.AddComponent<DragGhost>();
                _instance.Setup(canvas);
            }
            return _instance;
        }
    }

    private Canvas         _canvas;
    private RectTransform  _rect;
    private Image          _image;

    private void Setup(Canvas canvas)
    {
        _canvas = canvas;
        _rect   = gameObject.AddComponent<RectTransform>();
        _image  = gameObject.AddComponent<Image>();

        _image.raycastTarget = false; // ne bloque jamais les événements sous le ghost
        _rect.pivot          = new Vector2(0.5f, 0.5f);
        // Ancre centrée (0.5,0.5), PAS (0,0) — ScreenPointToLocalPointInRectangle() renvoie un
        // point relatif au PIVOT du Canvas parent (centre, pour un Canvas standard), alors que
        // anchoredPosition avec une ancre à (0,0) se mesure depuis le coin bas-gauche du parent.
        // Avec l'ancre à (0,0) (reprise par erreur du code d'origine de SkillDragSource/
        // PassiveDragSource — bug déjà présent avant ce chantier, jamais remarqué), le ghost
        // apparaissait décalé du curseur d'environ la moitié de la taille du Canvas. Trouvé en
        // revue de code, 2026-10-02 — affectait les 5 sources une fois unifiées (avant, seules
        // Skill/Passive avaient ce bug ; les 3 autres utilisaient localPosition, qui n'a pas ce
        // problème, mais ce chantier les a fait passer par anchoredPosition via DragGhost).
        _rect.anchorMin       = _rect.anchorMax = new Vector2(0.5f, 0.5f);

        gameObject.SetActive(false);
    }

    /// <summary>Démarre/relance l'affichage du ghost — appelé depuis OnBeginDrag de
    /// chaque source. tint est fourni par l'appelant (pas calculé ici) — les 5
    /// sources d'origine utilisaient des teintes différentes (SkillDragSource/
    /// PassiveDragSource : blanc opaque si icône présente, gris 0.8 alpha sinon ;
    /// les 3 autres : blanc à 0.7 alpha toujours) — préserver ces différences
    /// exactement plutôt que les uniformiser silencieusement. Remonte
    /// systématiquement au-dessus de tout (SetAsLastSibling) pour ne jamais
    /// apparaître sous un panel ouvert après le dernier drag.</summary>
    public static void Begin(Sprite icon, Color tint, Vector2 size, PointerEventData e)
    {
        var inst = Instance;
        if (inst == null) return;

        inst._image.sprite = icon;
        inst._image.color  = tint;
        inst._rect.sizeDelta = size;
        inst.transform.SetAsLastSibling();
        inst.gameObject.SetActive(true);
        inst.MoveTo(e);
    }

    /// <summary>Repositionne le ghost — appelé depuis OnDrag. No-op si rien n'est en
    /// cours de drag (évite un déplacement fantôme si appelé hors séquence).</summary>
    public static void Move(PointerEventData e)
    {
        if (_instance == null || !_instance.gameObject.activeSelf) return;
        _instance.MoveTo(e);
    }

    /// <summary>Masque le ghost — appelé depuis OnEndDrag. Ne détruit JAMAIS le
    /// GameObject, juste SetActive(false) — réutilisé tel quel au prochain drag.</summary>
    public static void End()
    {
        if (_instance == null) return;
        _instance.gameObject.SetActive(false);
    }

    private void MoveTo(PointerEventData e)
    {
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform,
            e.position, cam, out Vector2 localPoint);

        _rect.anchoredPosition = localPoint;
    }
}
