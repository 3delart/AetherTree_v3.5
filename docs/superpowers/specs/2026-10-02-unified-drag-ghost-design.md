# Fusion du ghost de drag-and-drop — Design

## Contexte

Florian juge le rendu actuel du drag-and-drop "visuellement bof" et veut un système
unifié partout où du drag-and-drop est utilisé (Inventaire, SkillBar, PassifBar,
ConsoBar, CharacterPanel, ShopUI, Forge, Rareté, Fusion). Audit du code réel
(grep + lecture complète) a montré que le problème n'est pas tant visuel (le ghost
actuel — icône semi-transparente qui suit la souris — n'est pas moins bien que
l'équivalent AnyRPG, voir plus bas) que **structurel** : 5 classes réimplémentent
indépendamment la même mécanique de ghost (création du GameObject, suivi souris via
`RectTransformUtility.ScreenPointToLocalPointInRectangle`, destruction), avec
~25 lignes quasi identiques dupliquées à chaque fois :

- `UI/Shared/SkillDragDrop.cs` — `SkillDragSource` (drag skill actif/basique/ultime)
- `UI/Shared/SkillDragDrop.cs` — `PassiveDragSource` (drag skill passif)
- `UI/Shared/EquipmentSlotHandler.cs` — drag depuis un slot équipé vers l'inventaire
- `UI/Shared/InventoryItemCell.cs` — drag depuis une cellule d'inventaire
- `UI/Shared/StagedItemDragSource.cs` — drag depuis un slot de mise en scène
  (Fusion/Rareté/Forge), générique via `Init(getItem, onReturnedToInventory)`

Les cibles de drop (`SkillDropTarget`, `EquipmentSlotHandler` côté drop,
`InventoryItemCell` côté drop, `ConsoDropSlot`, `ShopSellDropSlot`, `ForgeDropSlot`,
`RarityDropSlot`, `FusionSlotDropTarget`) sont déjà propres — chacune ne porte que
sa propre logique de compatibilité/action, aucune duplication de ghost côté cible.
Seul le **transport visuel** (côté source) est dupliqué ; ce chantier ne touche que
ça.

### Référence étudiée (AnyRPG, package Asset Store)

AnyRPG utilise un modèle d'interaction différent (clic-ramasser → clic-poser, pas de
bouton maintenu) via un `HandScript` singleton partagé. Florian a choisi de **garder
le geste presser-glisser-relâcher actuel** — le modèle d'interaction n'est pas remis
en cause, seule l'idée d'un composant de transport unique est reprise. Vérifié sur
le vrai prefab (`GameManager.prefab`, GameObject "HandIcon") : le rendu lui-même est
minimal (icône + sprite de fond, alpha 0 au repos, suivi 1:1 sans lissage, aucune
animation) — pas plus poussé que l'existant d'AetherTree. Le polish visuel
(lissage, pop d'échelle, highlight de cible, animation de retour si drop invalide)
est explicitement **hors scope** de ce chantier — à traiter séparément si souhaité.

## Décisions de périmètre

- **Geste conservé** : presser-glisser-relâcher (`IBeginDragHandler`/`IDragHandler`/
  `IEndDragHandler` standard Unity), pas de changement de modèle d'interaction.
- **Seul le ghost/transport est mutualisé** — "qu'est-ce qui est en train d'être
  dragué" (`SkillDragSource.CurrentDragging`, `PassiveDragSource.CurrentDragging`,
  `InventoryUI.DraggedItem`/`DraggedCell`, le callback `Init()` de
  `StagedItemDragSource`) reste séparé par domaine, inchangé. Idem pour toute la
  logique de compatibilité/action des cibles.
- **Pas de nouvelle interface générique** (`IDraggable`/`IMoveable` à la AnyRPG)
  dans ce chantier — évoqué comme évolution possible si la duplication d'état
  redevient un problème plus tard, mais pas demandé aujourd'hui (YAGNI).
- **Polish visuel hors scope** — rendu identique à l'actuel (icône semi-transparente,
  pas d'ombre/lissage/animation), juste centralisé dans un seul composant.

## Architecture

Un seul nouveau composant, `UI/Shared/DragGhost.cs` — singleton **persistant**
(créé une fois, jamais détruit/recréé à chaque drag, juste activé/désactivé), même
patron d'auto-création paresseuse que `WorldStateRegistry`/`WorldEventScheduler`
(`Instance` qui crée le GameObject au premier accès si absent). Évite le
Instantiate/Destroy répété de l'approche actuelle (pas de fuite de GameObject si un
`OnEndDrag` ne se déclenche pas pour une raison quelconque — rare mais déjà possible
aujourd'hui avec le patron dupliqué).

Vérifié : `SkillBarUI` (et donc le canvas UI racine) vit dans `Assets/Scenes/
_Persistent.unity`, jamais détruit entre changements de map — un singleton créé une
fois reste valide pour toute la session de jeu, pas de risque de canvas mort après
un changement de scène.

### API statique

```csharp
DragGhost.Begin(Sprite icon, Vector2 size, PointerEventData e);  // OnBeginDrag
DragGhost.Move(PointerEventData e);                               // OnDrag
DragGhost.End();                                                  // OnEndDrag
```

### Composant complet

```csharp
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
                Canvas canvas = FindObjectOfType<Canvas>();
                if (canvas == null)
                {
                    Debug.LogError("[DragGhost] Aucun Canvas trouvé dans la scène — " +
                                    "impossible d'afficher le ghost de drag.");
                    return null;
                }

                var go = new GameObject("DragGhost (auto)");
                go.transform.SetParent(canvas.transform, false);
                DontDestroyOnLoad(go); // vit dans _Persistent.unity de toute façon,
                                        // mais garde-fou si jamais appelé ailleurs

                _instance = go.AddComponent<DragGhost>();
                _instance.Setup(canvas);
            }
            return _instance;
        }
    }

    private Canvas         _canvas;
    private RectTransform  _rect;
    private Image          _image;

    private static readonly Color ValidTint = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color EmptyTint = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    private void Setup(Canvas canvas)
    {
        _canvas = canvas;
        _rect   = gameObject.AddComponent<RectTransform>();
        _image  = gameObject.AddComponent<Image>();

        _image.raycastTarget = false; // ne bloque jamais les événements sous le ghost
        _rect.pivot          = new Vector2(0.5f, 0.5f);
        _rect.anchorMin       = _rect.anchorMax = Vector2.zero;

        gameObject.SetActive(false);
    }

    /// <summary>Démarre/relance l'affichage du ghost — appelé depuis OnBeginDrag de
    /// chaque source. Remonte systématiquement au-dessus de tout (SetAsLastSibling)
    /// pour ne jamais apparaître sous un panel ouvert après le dernier drag.</summary>
    public static void Begin(Sprite icon, Vector2 size, PointerEventData e)
    {
        var inst = Instance;
        if (inst == null) return;

        inst._image.sprite = icon;
        inst._image.color  = icon != null ? ValidTint : EmptyTint;
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
```

## Fichiers touchés

Chaque source remplace son bloc `OnBeginDrag`/`OnDrag`/`OnEndDrag` (création de
GameObject + Image + positionnement + destruction, ~25 lignes) par 3 appels à
`DragGhost`. Les champs `_ghost`/`_dragGhost`/`_rootCanvas`/`FindRootCanvas()`/
`MoveGhost()` propres à chaque classe disparaissent — plus besoin, `DragGhost` les
porte en interne une seule fois.

- **`UI/Shared/SkillDragDrop.cs`** — `SkillDragSource` et `PassiveDragSource` :
  chacune garde son champ statique `_dragging`/`CurrentDragging` (logique de "quoi
  dragué" inchangée), remplace son bloc ghost par `DragGhost.Begin(skill.icon, new
  Vector2(48f, 48f), eventData)` / `DragGhost.Move(eventData)` / `DragGhost.End()`
  (et équivalent pour `passive.icon`).
- **`UI/Shared/EquipmentSlotHandler.cs`** — bloc `OnBeginDrag`/`OnDrag`/`OnEndDrag`
  remplacé, `item.Icon` comme sprite. Logique `GetEquippedItem()`/`InventoryUI.
  BeginDragEquipped()` inchangée.
- **`UI/Shared/InventoryItemCell.cs`** — idem, `iconImage.sprite` comme source
  (pas besoin de recalculer, déjà résolu à `SetItem()`). `InventoryUI.BeginDrag()`
  inchangé.
- **`UI/Shared/StagedItemDragSource.cs`** — idem, `item.Icon` (résolu via
  `_getItem()`). `InventoryUI.BeginDragFromExternalSlot()` inchangé.

Taille du ghost : `SkillDragSource`/`PassiveDragSource` utilisaient une constante
`new Vector2(48f, 48f)` — conservée telle quelle. `EquipmentSlotHandler`/
`InventoryItemCell`/`StagedItemDragSource` utilisaient la taille réelle du
`RectTransform` source (`_rect.sizeDelta`) — comportement préservé en passant
cette valeur au lieu de la constante pour ces trois-là spécifiquement.

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian,
sur CHAQUE panel concerné (régression possible sur n'importe lequel vu que le
composant est partagé) :

1. SkillBar : glisser un skill actif depuis la Bibliothèque vers un slot — ghost
   suit la souris, drop équipe normalement.
2. PassifBar : idem avec un skill passif.
3. CharacterPanel : glisser une arme équipée vers l'inventaire (déséquipe) — ghost
   suit, drop dans une cellule libre fonctionne.
4. Inventaire : glisser un item d'une cellule à une autre (swap) — ghost suit,
   swap correct.
5. Inventaire → ConsoBar : glisser un consommable — accepté ; glisser un
   non-consommable — refusé proprement (pas de crash).
6. Inventaire → ShopUI (zone de vente) — ghost suit, vente déclenchée au drop.
7. Forge/Rareté/Fusion : glisser un item staged hors de son slot pour le remettre
   en inventaire (`StagedItemDragSource`) — ghost suit, retour correct, le panel
   d'origine nettoie bien sa référence `_staged`/`_slotN`.
8. Enchaîner plusieurs drags à la suite SANS relâcher-puis-cliquer-ailleurs
   (drag → drop → drag immédiat d'un autre item) — le ghost ne doit jamais rester
   affiché entre deux drags, ni clignoter/sauter de position au début du second.
9. Annuler un drag en relâchant hors de toute cible valide (dans le vide) — le
   ghost disparaît proprement, aucune action indésirable.
