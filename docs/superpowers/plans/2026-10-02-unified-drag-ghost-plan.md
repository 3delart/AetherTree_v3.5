# Fusion du ghost de drag-and-drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer la mécanique de ghost de drag-and-drop dupliquée dans 5 classes
par un seul composant partagé `DragGhost`, sans changer le geste (presser-glisser-
relâcher) ni la logique de compatibilité/action de chaque source/cible.

**Architecture:** Un nouveau composant singleton `UI/Shared/DragGhost.cs` (créé une
fois, jamais détruit, juste activé/désactivé) expose une API statique `Begin/Move/
End`. Les 5 classes sources (`SkillDragSource`, `PassiveDragSource`,
`EquipmentSlotHandler`, `InventoryItemCell`, `StagedItemDragSource`) remplacent
chacune leur bloc `OnBeginDrag`/`OnDrag`/`OnEndDrag` dupliqué par 3 appels à ce
composant.

**Tech Stack:** Unity C#, UGUI (`UnityEngine.UI`, `UnityEngine.EventSystems`).

**Spec:** docs/superpowers/specs/2026-10-02-unified-drag-ghost-design.md

## Global Constraints

- Geste de drag conservé : `IBeginDragHandler`/`IDragHandler`/`IEndDragHandler`
  standard Unity — aucun changement de modèle d'interaction.
- `DragGhost` ne porte AUCUNE logique de "quoi est dragué" ni de compatibilité/
  action — uniquement le transport visuel (icône qui suit la souris).
- `DragGhost` est un singleton **persistant** — créé une fois, jamais
  `Instantiate`/`Destroy` à chaque drag, juste `SetActive(true/false)`.
- Taille du ghost : `SkillDragSource`/`PassiveDragSource` utilisent une constante
  `new Vector2(48f, 48f)` ; `EquipmentSlotHandler`/`InventoryItemCell`/
  `StagedItemDragSource` utilisent la taille réelle du `RectTransform` source
  (`_rect.sizeDelta`) — comportement existant préservé exactement.
- Teinte du ghost : `DragGhost.Begin()` prend la couleur en paramètre explicite
  (pas de calcul interne) — `SkillDragSource`/`PassiveDragSource` passent
  `Color.white` si icône présente sinon `(0.5,0.5,0.5,0.8)` ; les 3 autres
  classes passent toujours `(1,1,1,0.7)` — comportement existant préservé
  exactement, pas uniformisé.
- Aucun test automatisé — pas de framework de test dans ce projet Unity.
  Vérification manuelle Play Mode uniquement (Tâche 6).

## Review Focus

- **Canvas introuvable au premier appel** (`FindObjectOfType<Canvas>()` retourne
  `null`, ex: scène de test sans Canvas) — `DragGhost.Instance` ne doit jamais
  lever de `NullReferenceException`, juste logger une erreur et laisser les
  appelants no-op proprement (`Begin`/`Move`/`End` testent `inst == null`).
- **Drag enchaînés sans pause** (drop puis immédiatement un nouveau
  `OnBeginDrag` sur un autre slot, sans que la souris ne quitte aucune zone
  entre les deux) — le ghost ne doit jamais rester visible à l'ancienne
  position avant que `Begin()` ne le repositionne ; `Begin()` doit toujours
  repositionner AVANT de réafficher, jamais l'inverse.
- **Annulation dans le vide** (relâcher hors de toute cible `IDropHandler`) —
  `OnEndDrag` doit toujours être appelé par Unity même sans drop valide (c'est
  le comportement standard d'`IEndDragHandler`, vérifié dans le code existant
  des 5 classes — chacune appelle déjà `End()`/détruit déjà son ghost dans
  `OnEndDrag` inconditionnellement) — `DragGhost.End()` doit donc toujours
  masquer proprement, aucune condition qui pourrait le laisser affiché.
- **Taille du ghost incohérente par source** (déjà couvert par le Global
  Constraint ci-dessus) — chaque tâche 2-5 doit passer la BONNE taille pour sa
  classe, pas copier bêtement `48×48` partout.
- **Icône nulle** (`skill.icon == null`, `item.Icon == null`) — le code existant
  gère déjà ce cas différemment par classe (teinte grise `0.5,0.5,0.5,0.8` pour
  Skill/Passive quand `icon == null` ; `InventoryItemCell`/`EquipmentSlotHandler`/
  `StagedItemDragSource` ne testent pas ce cas aujourd'hui, ils passent juste le
  sprite null avec une teinte fixe `1,1,1,0.7` peu importe). `DragGhost.Begin()`
  prend la teinte en paramètre (pas de calcul interne, voir Tâche 1) —
  chaque tâche 2-5 calcule/passe exactement la même teinte qu'avant. Couvert par
  un geste manuel dédié en Tâche 6 Step 11 (pas seulement une relecture de code).

---

### Task 1: DragGhost — nouveau composant partagé

**Files:**
- Create: `UI/Shared/DragGhost.cs`

**Interfaces:**
- Produces: `DragGhost.Begin(Sprite icon, Color tint, Vector2 size, PointerEventData e)`,
  `DragGhost.Move(PointerEventData e)`, `DragGhost.End()` — méthodes statiques
  publiques, utilisées par les Tâches 2-5.

- [ ] **Step 1: Créer le fichier avec le composant complet**

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
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity (ou laisser l'IDE compiler) — 0 erreur attendue. Ce fichier est
autonome, ne dépend d'aucune des classes touchées dans les tâches suivantes.

- [ ] **Step 3: Commit**

```bash
git add UI/Shared/DragGhost.cs
git commit -m "feat: add shared DragGhost component for unified drag-and-drop ghost"
```

---

### Task 2: SkillDragDrop.cs — SkillDragSource + PassiveDragSource

**Files:**
- Modify: `UI/Shared/SkillDragDrop.cs:40-194` (classes `SkillDragSource` et
  `PassiveDragSource` — la 3e classe du fichier, `SkillDropTarget`, n'est PAS
  touchée)

**Interfaces:**
- Consumes: `DragGhost.Begin(Sprite, Color, Vector2, PointerEventData)`,
  `DragGhost.Move(PointerEventData)`, `DragGhost.End()` (Task 1).

- [ ] **Step 1: Remplacer le bloc complet de SkillDragSource**

Remplacer tout le contenu de la classe `SkillDragSource` (lignes 40-117 du
fichier actuel) par :

```csharp
public class SkillDragSource : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public SkillData skill;

    private static SkillData _dragging;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (skill == null) return;
        _dragging = skill;
        Color tint = skill.icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        DragGhost.Begin(skill.icon, tint, new Vector2(48f, 48f), eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragGhost.Move(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = null;
        DragGhost.End();
    }

    public static SkillData CurrentDragging => _dragging;
}
```

Ce qui disparaît : `_ghost`, `_rootCanvas`, `FindRootCanvas()`, `MoveGhost()` —
plus nécessaires, `DragGhost` porte tout ça en interne. `CurrentDragging`
(lu par `SkillDropTarget.OnDrop`/`OnPointerEnter`, classe intouchée) est
préservé à l'identique.

- [ ] **Step 2: Remplacer le bloc complet de PassiveDragSource**

Remplacer tout le contenu de la classe `PassiveDragSource` (lignes 123-194 du
fichier actuel) par :

```csharp
public class PassiveDragSource : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public PassiveSkillData passive;

    private static PassiveSkillData _dragging;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (passive == null) return;
        _dragging = passive;
        Color tint = passive.icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        DragGhost.Begin(passive.icon, tint, new Vector2(48f, 48f), eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragGhost.Move(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = null;
        DragGhost.End();
    }

    public static PassiveSkillData CurrentDragging => _dragging;
}
```

Même nettoyage : `_ghost`, `_rootCanvas`, `FindRootCanvas()`, `MoveGhost()`
retirés. `CurrentDragging` préservé.

- [ ] **Step 3: Vérifier que SkillDropTarget (3e classe du fichier) est intacte**

Lire le fichier après édition — la classe `SkillDropTarget` (tout ce qui suit
`PassiveDragSource`, à partir de `// SKILLDROP TARGET`) doit être strictement
identique à avant (aucune modification de cette tâche ne doit l'avoir touchée).

- [ ] **Step 4: Commit**

```bash
git add UI/Shared/SkillDragDrop.cs
git commit -m "refactor: SkillDragSource/PassiveDragSource use shared DragGhost"
```

---

### Task 3: EquipmentSlotHandler.cs

**Files:**
- Modify: `UI/Shared/EquipmentSlotHandler.cs:69-109`

**Interfaces:**
- Consumes: `DragGhost.Begin(Sprite, Color, Vector2, PointerEventData)`,
  `DragGhost.Move(PointerEventData)`, `DragGhost.End()` (Task 1).

- [ ] **Step 1: Remplacer le bloc OnBeginDrag/OnDrag/OnEndDrag**

Remplacer (lignes 69-109 du fichier actuel, de `public void OnBeginDrag` à la
fin d'`OnEndDrag`) :

```csharp
    public void OnBeginDrag(PointerEventData e)
    {
        if (_player == null) return;

        var item = GetEquippedItem();
        if (item == null) return;

        InventoryUI.BeginDragEquipped(item);
        DragGhost.Begin(item.Icon, new Color(1f, 1f, 1f, 0.7f),
            _rect != null ? _rect.sizeDelta : new Vector2(64, 64), e);
    }

    public void OnDrag(PointerEventData e)
    {
        DragGhost.Move(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        InventoryUI.EndDrag();
        DragGhost.End();
    }
```

Ce qui disparaît : `_dragGhost` (champ statique), la création manuelle du
`GameObject`/`Image`/`RectTransform` et le calcul de position dans
`OnBeginDrag`, et le bloc `RectTransformUtility.ScreenPointToLocalPointInRectangle`
dans `OnDrag`. `_rect` reste (toujours utilisé pour la taille du ghost).

- [ ] **Step 2: Nettoyer le champ _canvas devenu inutile**

Vérifié sur le fichier réel : `_canvas` (assigné dans `Start()` via
`GetComponentInParent<Canvas>()`) n'était utilisé QUE dans le bloc ghost retiré
à l'étape précédente — ni `OnPointerClick`, ni `OnDrop`, ni `GetEquippedItem()`
ne le lisent. Retirer la ligne `private Canvas _canvas;` et la ligne
`_canvas = GetComponentInParent<Canvas>();` dans `Start()`. `_player`/`_rect`
restent (toujours utilisés ailleurs).

- [ ] **Step 3: Vérifier que OnPointerClick, OnDrop, GetEquippedItem() sont intacts**

Lire le fichier après édition — ces trois sections ne doivent pas avoir bougé
(hors suppression de la ligne `_canvas = ...` dans `Start()`).

- [ ] **Step 4: Commit**

```bash
git add UI/Shared/EquipmentSlotHandler.cs
git commit -m "refactor: EquipmentSlotHandler uses shared DragGhost"
```

---

### Task 4: InventoryItemCell.cs

**Files:**
- Modify: `UI/Shared/InventoryItemCell.cs:107-143`

**Interfaces:**
- Consumes: `DragGhost.Begin(Sprite, Color, Vector2, PointerEventData)`,
  `DragGhost.Move(PointerEventData)`, `DragGhost.End()` (Task 1).

- [ ] **Step 1: Remplacer le bloc OnBeginDrag/OnDrag/OnEndDrag**

Remplacer (lignes 107-143 du fichier actuel, de `public void OnBeginDrag` à la
fin d'`OnEndDrag`) :

```csharp
    public void OnBeginDrag(PointerEventData e)
    {
        if (Item == null) return;
        InventoryUI.BeginDrag(this);
        DragGhost.Begin(iconImage?.sprite, new Color(1f, 1f, 1f, 0.7f), _rect.sizeDelta, e);
    }

    public void OnDrag(PointerEventData e)
    {
        DragGhost.Move(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        InventoryUI.EndDrag();
        DragGhost.End();
    }
```

Ce qui disparaît : `_dragGhost` (champ statique), la création manuelle du
ghost dans `OnBeginDrag`, le calcul de position dans `OnDrag`. `_rect` reste
(toujours utilisé pour la taille du ghost).

- [ ] **Step 2: Nettoyer le champ _canvas devenu inutile**

Vérifié sur le fichier réel : `_canvas` (assigné dans `Init()` via
`GetComponentInParent<Canvas>()`) n'était utilisé QUE dans le bloc ghost retiré
à l'étape précédente — ni `OnPointerClick` ni `OnDrop` ne le lisent. Retirer la
ligne `private Canvas _canvas;` et la ligne `_canvas = GetComponentInParent<Canvas>();`
dans `Init()`. Les autres champs assignés dans `Init()` (`_ui`, `_rect`) restent
inchangés.

- [ ] **Step 3: Vérifier que OnPointerClick et OnDrop sont intacts**

Lire le fichier après édition — ces deux sections (dont `OnDrop`, qui gère le
swap entre cellules et le retour via `ConsumeReturnToSource()`) ne doivent pas
avoir bougé (hors suppression de la ligne `_canvas = ...` dans `Init()`).

- [ ] **Step 4: Commit**

```bash
git add UI/Shared/InventoryItemCell.cs
git commit -m "refactor: InventoryItemCell uses shared DragGhost"
```

---

### Task 5: StagedItemDragSource.cs

**Files:**
- Modify: `UI/Shared/StagedItemDragSource.cs:39-77`

**Interfaces:**
- Consumes: `DragGhost.Begin(Sprite, Color, Vector2, PointerEventData)`,
  `DragGhost.Move(PointerEventData)`, `DragGhost.End()` (Task 1).

- [ ] **Step 1: Remplacer le bloc OnBeginDrag/OnDrag/OnEndDrag**

Remplacer (lignes 39-77 du fichier actuel, de `public void OnBeginDrag` à la
fin d'`OnEndDrag`) :

```csharp
    public void OnBeginDrag(PointerEventData e)
    {
        var item = _getItem?.Invoke();
        if (item == null) return;

        InventoryUI.BeginDragFromExternalSlot(item, _onReturnedToInventory);
        DragGhost.Begin(item.Icon, new Color(1f, 1f, 1f, 0.7f), _rect.sizeDelta, e);
    }

    public void OnDrag(PointerEventData e)
    {
        DragGhost.Move(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        InventoryUI.EndDrag();
        DragGhost.End();
    }
```

Ce qui disparaît : `_dragGhost`, `_canvas` (n'était utilisé QUE pour le ghost
dans ce fichier — peut être retiré du champ de classe aussi, voir Step 2),
toute la création/positionnement manuels.

- [ ] **Step 2: Nettoyer le champ _canvas devenu inutile**

Contrairement aux Tâches 3/4, `_canvas` dans `StagedItemDragSource` n'est lu
nulle part ailleurs dans le fichier (vérifié : seul `Init()` l'assignait pour
le bloc ghost retiré). Retirer la ligne `private Canvas _canvas;` et la ligne
`_canvas = GetComponentInParent<Canvas>();` dans `Init()`. `_rect` reste
(toujours utilisé pour la taille du ghost).

- [ ] **Step 3: Vérifier que Init() (en dehors du nettoyage ci-dessus) est intact**

Lire le fichier après édition — la signature et le corps de `Init(getItem,
onReturnedToInventory)` (hors suppression de la ligne `_canvas = ...`) ne
doivent pas avoir changé.

- [ ] **Step 4: Commit**

```bash
git add UI/Shared/StagedItemDragSource.cs
git commit -m "refactor: StagedItemDragSource uses shared DragGhost"
```

---

### Task 6: Vérification finale (Play Mode, Florian)

**Files:** aucun — vérification manuelle uniquement.

**Interfaces:** aucune (tâche terminale).

Pas de framework de test automatisé dans ce projet — chaque point ci-dessous est
un geste à faire en Play Mode dans l'éditeur Unity. Cocher au fur et à mesure.

- [ ] **Step 1: SkillBar**

Ouvrir la Bibliothèque de compétences, glisser un skill actif vers un slot de
la SkillBar (presser, glisser, relâcher) — le ghost doit suivre la souris en
continu pendant le glissé, le drop doit équiper le skill normalement (même
comportement qu'avant ce chantier).

- [ ] **Step 2: PassifBar**

Même test avec un skill passif vers un slot de la PassifBar.

- [ ] **Step 3: CharacterPanel → Inventaire**

Glisser une arme équipée (slot du CharacterPanel) vers une cellule libre de
l'inventaire — ghost suit, l'arme se déséquipe et apparaît dans l'inventaire.

- [ ] **Step 4: Inventaire → Inventaire**

Glisser un item d'une cellule à une autre déjà occupée — ghost suit, les deux
items swappent correctement.

- [ ] **Step 5: Inventaire → ConsoBar**

Glisser un consommable vers un slot de la ConsoBar — accepté. Glisser un item
NON consommable vers le même slot — refusé proprement (message console
existant, pas de crash, pas d'item perdu).

- [ ] **Step 6: Inventaire → ShopUI**

Ouvrir le marchand, glisser un item de l'inventaire vers la zone de vente —
ghost suit, la vente se déclenche au drop.

- [ ] **Step 7: Forge / Rareté / Fusion — retour vers l'inventaire**

Dans chacun des 3 panels, poser un item en staging puis le re-glisser hors de
son slot vers l'inventaire (`StagedItemDragSource`) — ghost suit, l'item
revient proprement dans l'inventaire ET le panel d'origine nettoie bien sa
propre référence interne (`_staged`/`_slot1Item`/etc. — re-ouvrir le panel
après coup pour confirmer que le slot est bien vide, pas juste visuellement
vidé).

- [ ] **Step 8: Drags enchaînés**

Glisser un item, le déposer, puis IMMÉDIATEMENT (sans pause, sans sortir du
Play Mode) démarrer un nouveau drag sur un autre item — le ghost ne doit
jamais apparaître brièvement à l'ancienne position avant de se repositionner
sur le nouveau, ni rester affiché entre les deux drags.

- [ ] **Step 9: Annulation dans le vide**

Démarrer un drag puis relâcher le bouton de souris dans une zone sans aucune
cible de drop valide (ex: zone vide de l'écran) — le ghost doit disparaître
proprement, aucun item ne doit être perdu/dupliqué/téléporté.

- [ ] **Step 10: Icône manquante**

Si un skill/item de test sans icône assignée est disponible (ou en assigner un
temporairement à une SkillData de test), le glisser — le ghost doit afficher un
carré gris semi-transparent (pas une icône blanche vide ni une erreur), identique
au comportement d'avant ce chantier pour `SkillDragSource`/`PassiveDragSource`.

- [ ] **Step 11: Commit final (si des ajustements ont été faits pendant la vérification)**

```bash
git add -A
git commit -m "fix: adjustments from manual drag-and-drop verification"
```

(Ne committer que si des corrections ont effectivement été nécessaires suite aux
Steps 1-10 — sinon cette tâche se termine sans commit, les 5 commits des Tâches
1-5 suffisent.)
