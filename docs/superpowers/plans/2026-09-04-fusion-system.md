# Système de Fusion (Cordonnier, Gants/Bottes S0→S6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Brancher le système de Fusion (Cordonnier, Gants/Bottes S0→S6) — formule `a+b+1`,
taux de réussite par palier calqués sur NosTale, échec = destruction des deux pièces — et
corriger le bug de résistance >100% dans `CombatSystem.CalculateDamage` que la Fusion rend
facilement atteignable.

**Architecture:** Nouveau `FusionTableData` (SO, calque `UpgradeTableData`/
`RarityGambleTableData`) porte les 6 taux de réussite. Nouveau `FusionSystem` (singleton,
calque `RaritySystem` — jet + coût consommé même en échec, destruction gérée par l'appelant)
fait le calcul pur, ne touche jamais l'inventaire. `GlovesInstance.Fuse()`/`BootsInstance.Fuse()`
(déjà existants, jamais appelés) corrigés pour la vraie formule. Nouveau `FusionUI` (3 slots :
2 entrées + 1 preview live) fait le drag-drop, la validation, et orchestre
`InventorySystem.RemoveItem`/`AddItem` autour du résultat de `FusionSystem`. Branché au
Cordonnier via le dispatch `DialogueAction.OpenFusionUI` déjà présent (stub `Debug.Log`) dans
`PNJ.HandleDialogueAction()`.

**Tech Stack:** Unity 2022+, C#, MonoBehaviour/ScriptableObject. Pas de framework de test
automatisé dans ce projet — vérification par compilation + test manuel en Play Mode (solo-dev,
Florian teste tout lui-même dans l'éditeur).

**Spec:** `docs/superpowers/specs/2026-09-04-fusion-system-design.md`

## Global Constraints

- Formule de palier : `target = slot1.fusionLevel + slot2.fusionLevel + 1`, refusé si
  `target > 6` (jamais de `Mathf.Min` qui clampe silencieusement).
- Taux de réussite indexés par **palier d'arrivée** (1..6) : `100/100/80/70/50/20`.
- Échec = **slot1 ET slot2 détruits**, coût Aeris perdu quand même (jamais juste slot1).
- Résultat = **nouvelle 3ᵉ instance**, jamais de mutation en place de slot1/slot2 (risque
  d'aliasing si l'objet est référencé ailleurs — équipé, autre liste).
- Identité + défenses rollées héritées de **slot2** ; résistances élémentaires **additionnées**
  slot1+slot2.
- Validation obligatoire : `slot1 != slot2` (même référence d'instance) rejeté avant tout calcul.
- Réutilise `ProgressBarUI.BarType.Craft` — **aucun nouveau `BarType`**.
- Le clamp de résistance (`Mathf.Clamp01`) va UNIQUEMENT dans le calcul de dégâts
  (`CombatSystem.CalculateDamage`) — le stat affiché/sauvegardé reste sans plafond.
- Jamais de destruction/réordonnancement d'un enum déjà sérialisé — non applicable ici (aucun
  enum existant n'est touché par ce plan), mais tout nouvel enum (`FusionResult`) est neuf et
  n'a donc aucune contrainte d'ordinal à respecter pour l'instant.

---

### Task 1: Fix CombatSystem — clamp résistance élémentaire

**Files:**
- Modify: `Assets/Scripts/Combat/CombatSystem.cs:153`

**Interfaces:**
- Consumes: rien (changement local à une ligne).
- Produces: rien consommé par une tâche suivante — indépendant du reste du plan.

- [ ] **Step 1: Lire la ligne actuelle pour confirmer le contexte exact**

Le fichier contient actuellement (méthode `CalculateDamage`, section "④b. Résistance
élémentaire + pénétration rang 5") :
```csharp
                elemDamage *= (1f - elemResist);
```

- [ ] **Step 2: Appliquer le clamp**

Remplacer par :
```csharp
                // Clamp local au calcul — une résistance élémentaire peut dépasser 100%
                // (Fusion sans plafond), mais le facteur de réduction ne doit jamais
                // repasser négatif (inverserait le signe des dégâts élémentaires, pouvant
                // annuler la part physique d'un skill mixte phys+élém). Le stat brut
                // affiché/sauvegardé n'est PAS touché, seul ce calcul l'est.
                elemDamage *= (1f - Mathf.Clamp01(elemResist));
```

- [ ] **Step 3: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 4: Test manuel (optionnel, si un mob/joueur sur-résisté est disponible)**

Si tu as déjà un moyen d'atteindre >100% de résistance à un élément (sinon, ce test devient
pertinent seulement une fois la Fusion branchée en Task 5) : lancer un skill mixte
phys+élément de cet élément contre la cible sur-résistée, confirmer que les dégâts totaux ne
descendent jamais sous la part physique attendue à 0% de résistance élémentaire (i.e. la part
élémentaire négative ne "mange" plus la part physique).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Combat/CombatSystem.cs
git commit -m "fix: clamp elemental resist in damage calc to prevent sign flip past 100%"
```

---

### Task 2: FusionTableData — table des taux de réussite

**Files:**
- Create: `Assets/Scripts/Data/Equipment/FusionTableData.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `FusionTableData` (classe SO), nested `FusionTableData.FusionTier { string label;
  float successRate; int aerisCost; }`, méthode `public FusionTier GetTier(int
  targetFusionLevel)` — retourne `null` si `targetFusionLevel` hors `[1,6]`. Consommé par
  `FusionSystem` (Task 4).

- [ ] **Step 1: Créer le fichier**

```csharp
using UnityEngine;

// =============================================================
// FUSIONTABLEDATA.CS — Table des taux de réussite de Fusion (S0 → S6)
// Path : Assets/Scripts/Data/Equipment/FusionTableData.cs
// AetherTree GDD — Fusion Gants/Bottes, Cordonnier
//
// Un seul asset pour tout le jeu, référencé par FusionSystem. Taux indexés par le
// PALIER D'ARRIVÉE (1 = S1 ... 6 = S6) — toute paire (slot1.fusionLevel, slot2.fusionLevel)
// visant le même palier utilise le même taux, peu importe la paire exacte utilisée.
// Valeurs calquées sur la référence NosTale (voir spec 2026-09-04-fusion-system-design.md).
// aerisCost à calibrer plus tard — non bloquant, comme UpgradeTableData/RarityGambleTableData.
// =============================================================

[CreateAssetMenu(fileName = "FusionTable", menuName = "AetherTree/Config/FusionTableData")]
public class FusionTableData : ScriptableObject
{
    [System.Serializable]
    public class FusionTier
    {
        [Tooltip("Palier affiché pour référence — pas utilisé en code, juste lisible dans l'Inspector.")]
        public string label;

        [Range(0f, 1f)]
        public float successRate;

        [Tooltip("Coût en Aeris — à calibrer selon économie complète, 0 par défaut.")]
        public int aerisCost = 0;
    }

    [Tooltip("Durée de la canalisation (secondes) — même barre que Forge/Rareté/Craft, résolution (jet+consommation) à la fin.")]
    public float channelDuration = 2.5f;

    [Tooltip("6 paliers, index 0 = cible S1 ... index 5 = cible S6. Taux calqués sur NosTale.")]
    public FusionTier[] tiers = new FusionTier[]
    {
        new FusionTier { label = "→ S1", successRate = 1.00f },
        new FusionTier { label = "→ S2", successRate = 1.00f },
        new FusionTier { label = "→ S3", successRate = 0.80f },
        new FusionTier { label = "→ S4", successRate = 0.70f },
        new FusionTier { label = "→ S5", successRate = 0.50f },
        new FusionTier { label = "→ S6", successRate = 0.20f },
    };

    /// <summary>Retourne le palier correspondant au niveau de fusion CIBLE (1..6).
    /// null si hors plage (0 = pas de fusion, >6 = combo invalide, jamais atteint en pratique
    /// car FusionSystem.CanFuse() refuse déjà target > 6 avant d'appeler GetTier).</summary>
    public FusionTier GetTier(int targetFusionLevel)
        => (targetFusionLevel >= 1 && targetFusionLevel <= 6) ? tiers[targetFusionLevel - 1] : null;
}
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 3: Créer l'asset dans l'éditeur (manuel, hors code)**

Clic droit dans le Project window → `Create > AetherTree > Config > FusionTableData` — un seul
asset pour tout le jeu (comme `UpgradeTable`/`RarityGambleTable`). Nom suggéré : `FusionTable`.
Les 6 taux par défaut sont déjà corrects (100/100/80/70/50/20), rien à modifier sauf si tu
veux ajuster `aerisCost` par palier.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Data/Equipment/FusionTableData.cs
git commit -m "feat: add FusionTableData with NosTale-matched success rate curve"
```

---

### Task 3: Corriger la formule de palier dans GlovesInstance/BootsInstance.Fuse()

**Files:**
- Modify: `Assets/Scripts/Data/Equipment/GlovesData.cs`
- Modify: `Assets/Scripts/Data/Equipment/BootsData.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `GlovesInstance.Fuse(GlovesInstance slot1, GlovesInstance slot2)` et
  `BootsInstance.Fuse(BootsInstance slot1, BootsInstance slot2)` — signatures inchangées,
  seule la formule interne change. Consommé par `FusionSystem` (Task 4).

- [ ] **Step 1: Modifier GlovesData.cs — formule + commentaires**

Dans `GlovesInstance.Fuse()`, remplacer :
```csharp
            fusionLevel     = Mathf.Min(6, Mathf.Max(slot1.fusionLevel, slot2.fusionLevel) + 1),
```
par :
```csharp
            fusionLevel     = slot1.fusionLevel + slot2.fusionLevel + 1,
```
Et remplacer le commentaire de la méthode juste au-dessus :
```csharp
    /// <summary>
    /// Fusionne deux paires de gants. GDD §5.6.
    /// Slot1 (sacrifiée) est détruite — ses résistances s'additionnent à Slot2.
    /// fusionLevel = max(slot1, slot2) + 1 — plafonné à S6.
    /// Résistances sans plafond — un joueur peut dépasser 100%.
    /// Les défenses rollées (ratio) de Slot2 sont conservées telles quelles —
    /// la Fusion ne touche que les résistances élémentaires.
    /// </summary>
```
par :
```csharp
    /// <summary>
    /// Fusionne deux paires de gants. GDD §5.6 (révisé 2026 — voir
    /// docs/superpowers/specs/2026-09-04-fusion-system-design.md).
    /// Slot1 ET Slot2 sont détruites par l'appelant (FusionUI) — cette méthode calcule
    /// seulement le résultat, ne touche jamais l'inventaire ni les instances passées en
    /// paramètre (jamais de mutation en place, évite l'aliasing si slot2 est référencé
    /// ailleurs, ex: équipé).
    /// fusionLevel = slot1.fusionLevel + slot2.fusionLevel + 1 — PAS de clamp ici, c'est
    /// FusionSystem.CanFuse() qui refuse l'action en amont si le résultat dépasserait S6.
    /// Résistances sans plafond — un joueur peut dépasser 100% (voir CombatSystem.cs:153
    /// pour le clamp appliqué uniquement au calcul de dégâts, pas au stat lui-même).
    /// Identité + défenses rollées héritées de Slot2, résistances additionnées Slot1+Slot2.
    /// </summary>
```

- [ ] **Step 2: Modifier BootsData.cs — même changement**

Dans `BootsInstance.Fuse()`, appliquer exactement le même remplacement de
`fusionLevel = Mathf.Min(6, Mathf.Max(slot1.fusionLevel, slot2.fusionLevel) + 1),` →
`fusionLevel = slot1.fusionLevel + slot2.fusionLevel + 1,` et le même commentaire (adapter
"gants" → "bottes" dans le texte).

- [ ] **Step 3: Corriger les commentaires d'en-tête obsolètes (les deux fichiers)**

Dans `GlovesData.cs`, remplacer le bloc d'en-tête :
```
// Fusion (GDD §5.6) :
//   Slot 1 (sacrifiée) + Slot 2 (conservée) → Slot 2 + résists Slot 1
//   fusionLevel = max(slot1, slot2) + 1 — plafonné à S6
//   Résistances plafonnées à 75% par élément — GDD §3.2
//   Irréversible — Slot 1 détruite définitivement
```
par :
```
// Fusion (révisé 2026 — voir docs/superpowers/specs/2026-09-04-fusion-system-design.md) :
//   Slot 1 + Slot 2 → nouvelle 3e instance (identité/def de Slot 2 + résists Slot1+Slot2)
//   fusionLevel = slot1.fusionLevel + slot2.fusionLevel + 1 — refusé si > S6 (FusionSystem.CanFuse)
//   Résistances SANS plafond — un joueur peut dépasser 100% (voir CombatSystem.cs:153)
//   Irréversible — Slot 1 ET Slot 2 détruites, même en cas d'ÉCHEC (voir FusionSystem)
```
Dans `BootsData.cs`, même remplacement adapté (le bloc équivalent dit "identique aux gants").

- [ ] **Step 4: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Data/Equipment/GlovesData.cs Assets/Scripts/Data/Equipment/BootsData.cs
git commit -m "fix: fusion tier formula is slot1+slot2+1, not max(slot1,slot2)+1"
```

---

### Task 4: FusionSystem — jet de réussite + consommation

**Files:**
- Create: `Assets/Scripts/Systems/FusionSystem.cs`

**Interfaces:**
- Consumes: `FusionTableData` (Task 2) — champ public `table`, méthode `GetTier(int)`.
  `GlovesInstance.Fuse(slot1, slot2)` / `BootsInstance.Fuse(slot1, slot2)` (Task 3, formule
  corrigée). `AerisSystem.Instance.Aeris` (int, lecture), `AerisSystem.Instance.Spend(int)` —
  déjà existants (utilisés identiquement par `UpgradeSystem`/`RaritySystem`).
- Produces: `enum FusionResult { Success, Failure, InvalidCombo, SameItem, MissingResource,
  InvalidTarget }`. `FusionSystem.Instance` (singleton). `bool CanFuse(int fusionLevel1, int
  fusionLevel2, out int target)`. `FusionResult TryFuseGloves(GlovesInstance slot1,
  GlovesInstance slot2, Player player, out GlovesInstance result)`. `FusionResult
  TryFuseBoots(BootsInstance slot1, BootsInstance slot2, Player player, out BootsInstance
  result)`. Tous consommés par `FusionUI` (Task 5).

- [ ] **Step 1: Créer le fichier**

```csharp
using UnityEngine;

// =============================================================
// FUSIONSYSTEM.CS — Fusion de gants/bottes (S0 → S6)
// Path : Assets/Scripts/Systems/FusionSystem.cs
// AetherTree GDD — Fusion, Cordonnier
//
// Calque RaritySystem.cs (Pari de rareté) — jet + consommation même en cas d'échec, la
// destruction/ajout des InventoryItem est gérée par l'appelant (FusionUI), pas ici : ce
// système ne connaît que les instances GlovesInstance/BootsInstance passées en paramètre,
// jamais leur provenance dans l'inventaire.
//
// Formule de palier (voir GlovesInstance.Fuse()/BootsInstance.Fuse()) :
//   target = slot1.fusionLevel + slot2.fusionLevel + 1, refusé si > 6 (CanFuse).
// Taux de réussite : FusionTableData, indexé par palier D'ARRIVÉE (1..6).
// Échec = les DEUX instances doivent être détruites par l'appelant — ce système ne le fait
// jamais lui-même, il retourne juste FusionResult.Failure.
// =============================================================

public enum FusionResult
{
    Success,
    Failure,
    InvalidCombo,     // target > 6
    SameItem,         // slot1 == slot2 (même référence d'instance)
    MissingResource,  // Aeris insuffisant
    InvalidTarget,    // slot1/slot2/player/table null
}

public class FusionSystem : MonoBehaviour
{
    public static FusionSystem Instance { get; private set; }

    [Tooltip("Table des paliers (taux de succès + coût Aeris) — voir FusionTableData.cs.")]
    public FusionTableData table;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Calcule le palier cible et dit si la combinaison est valide (≤ S6).
    /// Utilisé par FusionUI pour activer/désactiver le bouton Fuse AVANT tout jet —
    /// une combinaison invalide n'est jamais tentée, jamais un échec qui coûterait des
    /// ressources.</summary>
    public bool CanFuse(int fusionLevel1, int fusionLevel2, out int target)
    {
        target = fusionLevel1 + fusionLevel2 + 1;
        return target <= 6;
    }

    public FusionResult TryFuseGloves(GlovesInstance slot1, GlovesInstance slot2, Player player, out GlovesInstance result)
    {
        result = null;
        if (slot1 == null || slot2 == null || player == null || table == null) return FusionResult.InvalidTarget;
        if (ReferenceEquals(slot1, slot2)) return FusionResult.SameItem;
        if (!CanFuse(slot1.fusionLevel, slot2.fusionLevel, out int target)) return FusionResult.InvalidCombo;

        var tier = table.GetTier(target);
        if (tier == null) return FusionResult.InvalidCombo;
        if (!TryConsumeCost(tier)) return FusionResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success) result = GlovesInstance.Fuse(slot1, slot2);

        Debug.Log($"[FUSION] Gants {(success ? "RÉUSSIE" : "ÉCHOUÉE")} → cible S{target} (taux {tier.successRate:P0}).");
        return success ? FusionResult.Success : FusionResult.Failure;
    }

    public FusionResult TryFuseBoots(BootsInstance slot1, BootsInstance slot2, Player player, out BootsInstance result)
    {
        result = null;
        if (slot1 == null || slot2 == null || player == null || table == null) return FusionResult.InvalidTarget;
        if (ReferenceEquals(slot1, slot2)) return FusionResult.SameItem;
        if (!CanFuse(slot1.fusionLevel, slot2.fusionLevel, out int target)) return FusionResult.InvalidCombo;

        var tier = table.GetTier(target);
        if (tier == null) return FusionResult.InvalidCombo;
        if (!TryConsumeCost(tier)) return FusionResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success) result = BootsInstance.Fuse(slot1, slot2);

        Debug.Log($"[FUSION] Bottes {(success ? "RÉUSSIE" : "ÉCHOUÉE")} → cible S{target} (taux {tier.successRate:P0}).");
        return success ? FusionResult.Success : FusionResult.Failure;
    }

    /// <summary>Vérifie ET consomme l'Aeris en un seul passage — retourne false (rien
    /// consommé) si l'Aeris manque. Consommé même en cas d'échec du jet, comme
    /// Upgrade/Rarity (appelé AVANT le Random.value dans TryFuseXxx).</summary>
    private bool TryConsumeCost(FusionTableData.FusionTier tier)
    {
        if (tier.aerisCost <= 0) return true;
        if (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < tier.aerisCost) return false;
        AerisSystem.Instance.Spend(tier.aerisCost);
        return true;
    }
}
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 3: Placer le composant dans la scène (manuel, hors code)**

Ajouter un GameObject `FusionSystem` dans la scène persistante (même endroit que
`UpgradeSystem`/`RaritySystem`), lui assigner le composant `FusionSystem`, glisser l'asset
`FusionTable` (créé en Task 2) dans le champ `table`.

- [ ] **Step 4: Test manuel rapide (sans UI, via un script de test temporaire ou le Debugger)**

Optionnel à ce stade (l'UI n'existe pas encore) — peut être vérifié directement une fois
Task 5 terminée à la place. Si tu veux vérifier plus tôt : dans une méthode de test temporaire,
appeler `FusionSystem.Instance.CanFuse(6, 0, out int t)` doit retourner `false` (target=7),
`FusionSystem.Instance.CanFuse(2, 3, out int t2)` doit retourner `true` avec `t2 == 6`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Systems/FusionSystem.cs
git commit -m "feat: add FusionSystem — success roll + cost consumption for gloves/boots fusion"
```

---

### Task 5: FusionUI — 3 slots, validation, résolution

**Files:**
- Create: `Assets/Scripts/UI/Shared/FusionSlotDropTarget.cs`
- Create: `Assets/Scripts/UI/PanelSecondaire/FusionUI.cs`

**Interfaces:**
- Consumes: `InventoryUI.DraggedItem` (public static `InventoryItem`, déjà existant —
  alimenté par `InventoryItemCell.OnBeginDrag()` quand le joueur commence un drag depuis une
  cellule d'inventaire) et `InventoryUI.EndDrag()` (existant). `InventorySystem.Instance
  .RemoveItem(InventoryItem item)` (existant, retourne `bool`) et
  `InventorySystem.Instance.AddItem(InventoryItem item)` (existant). `InventoryItem` — champs
  `GlovesInstance`/`BootsInstance` (existants, publics avec setter privé — construits via
  `new InventoryItem(GlovesInstance i)` / `new InventoryItem(BootsInstance i)`, constructeurs
  déjà existants). `FusionSystem.Instance.CanFuse/TryFuseGloves/TryFuseBoots` (Task 4).
  `ProgressBarUI.Instance.StartProgress(string label, float duration, System.Action
  onComplete, System.Action onCancel, ProgressBarUI.BarType type, Transform followTarget)`
  (existant, déjà utilisé par ForgeUI/RarityUI/CraftPanelUI) et `ProgressBarUI.BarType.Craft`
  (existant).
- Produces: `FusionUI.Instance` (singleton), `public void Open(PNJData pnjData, Player
  player)`, `public void Close()`. Consommé par Task 6 (`PNJ.HandleDialogueAction`).

- [ ] **Step 1: Créer le drop-target réutilisable pour les 2 slots d'entrée**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// FUSIONSLOTDROPTARGET.CS — Zone de dépôt drag&drop pour un slot Fusion
// Path : Assets/Scripts/UI/Shared/FusionSlotDropTarget.cs
//
// Composant générique posé sur les 2 GameObjects "Slot 1"/"Slot 2" de FusionUI. Reçoit un
// drag démarré depuis une InventoryItemCell (InventoryUI.DraggedItem déjà alimenté par
// InventoryItemCell.OnBeginDrag — voir UI/Shared/InventoryItemCell.cs). Ne fait aucune
// validation de type ici (Gants vs Bottes, même item aux 2 slots) — c'est FusionUI qui
// valide via le callback, ce composant est un pur transport UI.
// =============================================================
public class FusionSlotDropTarget : MonoBehaviour, IDropHandler
{
    [Tooltip("0 = Slot 1, 1 = Slot 2 — juste pour que FusionUI sache lequel a reçu le drop.")]
    public int slotIndex;

    public System.Action<int, InventoryItem> OnItemDropped;

    public void OnDrop(PointerEventData eventData)
    {
        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        OnItemDropped?.Invoke(slotIndex, item);
        InventoryUI.EndDrag();
    }
}
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 3: Créer FusionUI.cs**

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// FUSIONUI.CS — Fusion de gants/bottes (Cordonnier, S0 → S6)
// Path : Assets/Scripts/UI/PanelSecondaire/FusionUI.cs
// Voir docs/superpowers/specs/2026-09-04-fusion-system-design.md
//
// 3 slots : Slot 1 + Slot 2 (entrées, glissées depuis l'inventaire) + Slot 3 (preview
// lecture seule, recalculée live). Bouton Fuse désactivé tant que : un slot est vide,
// slot1 == slot2 (même InventoryItem), ou la combinaison dépasse S6.
//
// Résolution : ProgressBarUI (même barre que Forge/Rareté/Craft — BarType.Craft) puis
// FusionSystem.TryFuseXxx. Slot1 ET Slot2 sont TOUJOURS retirés de l'inventaire à la
// résolution (succès ou échec) — seul un succès ajoute le résultat.
// =============================================================
public class FusionUI : MonoBehaviour
{
    public static FusionUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;
    public Button     closeButton;

    [Header("Slots d'entrée (drag & drop)")]
    public FusionSlotDropTarget slot1DropTarget;
    public FusionSlotDropTarget slot2DropTarget;
    public Image           slot1Icon;
    public Image           slot2Icon;
    public TextMeshProUGUI slot1Label; // "S{n} — {nom}" ou "Glisse un objet ici"
    public TextMeshProUGUI slot2Label;

    [Header("Slot 3 — preview résultat")]
    public Image           resultIcon;
    public TextMeshProUGUI resultLabel;       // "S{target} — {nom}"
    public TextMeshProUGUI resultResistances; // "Feu 21% | Eau 21% | ..."
    public TextMeshProUGUI successRateText;   // "Réussite : 80%"
    public TextMeshProUGUI costText;          // "Coût : 150 Aeris"
    public Button           fuseButton;

    private Player        _player;
    private InventoryItem  _slot1Item;
    private InventoryItem  _slot2Item;
    private bool           _channeling;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        fuseButton?.onClick.AddListener(OnFuseClicked);

        if (slot1DropTarget != null) slot1DropTarget.OnItemDropped += OnSlotDropped;
        if (slot2DropTarget != null) slot2DropTarget.OnItemDropped += OnSlotDropped;
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(PNJData pnjData, Player player)
    {
        _player = player;
        _slot1Item = null;
        _slot2Item = null;
        if (panel != null) panel.SetActive(true);
        RefreshSlots();
        RefreshPreview();
    }

    public void Close()
    {
        if (_channeling) return; // ne ferme pas pendant une canalisation en cours
        if (panel != null) panel.SetActive(false);
    }

    // =========================================================
    // SLOTS D'ENTRÉE
    // =========================================================

    private void OnSlotDropped(int slotIndex, InventoryItem item)
    {
        // N'accepte que Gants OU Bottes (jamais un mélange, jamais autre chose)
        bool isGloves = item.GlovesInstance != null;
        bool isBoots  = item.BootsInstance  != null;
        if (!isGloves && !isBoots) return;

        // Si l'autre slot est déjà rempli, il doit être du même type (gants+gants ou bottes+bottes)
        var other = slotIndex == 0 ? _slot2Item : _slot1Item;
        if (other != null)
        {
            bool otherIsGloves = other.GlovesInstance != null;
            if (otherIsGloves != isGloves) return; // type incompatible, drop ignoré
        }

        if (slotIndex == 0) _slot1Item = item;
        else                 _slot2Item = item;

        RefreshSlots();
        RefreshPreview();
    }

    private void RefreshSlots()
    {
        if (slot1Icon  != null) slot1Icon.sprite  = _slot1Item?.Icon;
        if (slot1Icon  != null) slot1Icon.enabled  = _slot1Item?.Icon != null;
        if (slot1Label != null) slot1Label.text    = _slot1Item != null
            ? $"S{GetFusionLevel(_slot1Item)} — {_slot1Item.Name}"
            : "Glisse un objet ici";

        if (slot2Icon  != null) slot2Icon.sprite  = _slot2Item?.Icon;
        if (slot2Icon  != null) slot2Icon.enabled  = _slot2Item?.Icon != null;
        if (slot2Label != null) slot2Label.text    = _slot2Item != null
            ? $"S{GetFusionLevel(_slot2Item)} — {_slot2Item.Name}"
            : "Glisse un objet ici";
    }

    private int GetFusionLevel(InventoryItem item)
        => item.GlovesInstance?.fusionLevel ?? item.BootsInstance?.fusionLevel ?? 0;

    // =========================================================
    // PREVIEW (SLOT 3) + VALIDATION
    // =========================================================

    private void RefreshPreview()
    {
        bool bothFilled = _slot1Item != null && _slot2Item != null;
        bool sameItem   = bothFilled && ReferenceEquals(_slot1Item, _slot2Item);

        if (!bothFilled || sameItem)
        {
            if (resultIcon  != null) resultIcon.enabled = false;
            if (resultLabel != null) resultLabel.text    = sameItem ? "Objet identique aux 2 slots" : "";
            if (resultResistances != null) resultResistances.text = "";
            if (successRateText   != null) successRateText.text   = "";
            if (costText           != null) costText.text           = "";
            if (fuseButton          != null) fuseButton.interactable = false;
            return;
        }

        int level1 = GetFusionLevel(_slot1Item);
        int level2 = GetFusionLevel(_slot2Item);
        bool canFuse = FusionSystem.Instance != null && FusionSystem.Instance.CanFuse(level1, level2, out int target);

        if (!canFuse)
        {
            if (resultIcon  != null) resultIcon.enabled = false;
            if (resultLabel != null) resultLabel.text    = $"Combo invalide — dépasse S6 (S{level1}+S{level2} → S{level1 + level2 + 1})";
            if (resultResistances != null) resultResistances.text = "";
            if (successRateText   != null) successRateText.text   = "";
            if (costText           != null) costText.text           = "";
            if (fuseButton          != null) fuseButton.interactable = false;
            return;
        }

        var tier = FusionSystem.Instance.table.GetTier(target);

        if (resultIcon  != null) { resultIcon.sprite = _slot2Item.Icon; resultIcon.enabled = _slot2Item.Icon != null; }
        if (resultLabel != null) resultLabel.text     = $"S{target} — {_slot2Item.Name}";
        if (resultResistances != null) resultResistances.text = BuildResistancePreviewText();
        if (successRateText   != null) successRateText.text   = tier != null ? $"Réussite : {tier.successRate:P0}" : "";
        if (costText           != null) costText.text           = tier != null ? $"Coût : {tier.aerisCost} Aeris" : "";
        if (fuseButton          != null) fuseButton.interactable = tier != null;
    }

    private string BuildResistancePreviewText()
    {
        if (_slot1Item.GlovesInstance != null && _slot2Item.GlovesInstance != null)
        {
            var g1 = _slot1Item.GlovesInstance;
            var g2 = _slot2Item.GlovesInstance;
            return $"Feu {(g1.resistFire + g2.resistFire):P0} | Eau {(g1.resistWater + g2.resistWater):P0} | " +
                   $"Foudre {(g1.resistLightning + g2.resistLightning):P0} | Terre {(g1.resistEarth + g2.resistEarth):P0} | " +
                   $"Nature {(g1.resistNature + g2.resistNature):P0} | Ténèbres {(g1.resistDarkness + g2.resistDarkness):P0} | " +
                   $"Lumière {(g1.resistLight + g2.resistLight):P0}";
        }
        if (_slot1Item.BootsInstance != null && _slot2Item.BootsInstance != null)
        {
            var b1 = _slot1Item.BootsInstance;
            var b2 = _slot2Item.BootsInstance;
            return $"Feu {(b1.resistFire + b2.resistFire):P0} | Eau {(b1.resistWater + b2.resistWater):P0} | " +
                   $"Foudre {(b1.resistLightning + b2.resistLightning):P0} | Terre {(b1.resistEarth + b2.resistEarth):P0} | " +
                   $"Nature {(b1.resistNature + b2.resistNature):P0} | Ténèbres {(b1.resistDarkness + b2.resistDarkness):P0} | " +
                   $"Lumière {(b1.resistLight + b2.resistLight):P0}";
        }
        return "";
    }

    // =========================================================
    // RÉSOLUTION
    // =========================================================

    private void OnFuseClicked()
    {
        if (_channeling) return;
        if (_slot1Item == null || _slot2Item == null || ReferenceEquals(_slot1Item, _slot2Item)) return;

        int level1 = GetFusionLevel(_slot1Item);
        int level2 = GetFusionLevel(_slot2Item);
        if (FusionSystem.Instance == null || !FusionSystem.Instance.CanFuse(level1, level2, out _)) return;

        _channeling = true;
        if (fuseButton != null) fuseButton.interactable = false;

        ProgressBarUI.Instance?.StartProgress(
            label:        "Fusion en cours...",
            duration:     FusionSystem.Instance.table.channelDuration,
            onComplete:   ResolveFusion,
            onCancel:     () => { _channeling = false; if (fuseButton != null) fuseButton.interactable = true; },
            type:         ProgressBarUI.BarType.Craft,
            followTarget: _player.transform
        );
    }

    private void ResolveFusion()
    {
        _channeling = false;

        bool isGloves = _slot1Item.GlovesInstance != null;
        InventoryItem resultItem = null;
        FusionResult outcome;

        if (isGloves)
        {
            outcome = FusionSystem.Instance.TryFuseGloves(_slot1Item.GlovesInstance, _slot2Item.GlovesInstance, _player, out var result);
            if (outcome == FusionResult.Success) resultItem = new InventoryItem(result);
        }
        else
        {
            outcome = FusionSystem.Instance.TryFuseBoots(_slot1Item.BootsInstance, _slot2Item.BootsInstance, _player, out var result);
            if (outcome == FusionResult.Success) resultItem = new InventoryItem(result);
        }

        // Slot1 et Slot2 sont TOUJOURS retirés — succès ou échec (GDD : les deux détruites).
        InventorySystem.Instance?.RemoveItem(_slot1Item);
        InventorySystem.Instance?.RemoveItem(_slot2Item);

        if (outcome == FusionResult.Success && resultItem != null)
            InventorySystem.Instance?.AddItem(resultItem);

        Debug.Log(outcome == FusionResult.Success
            ? "[FUSION] Réussite — nouvelle pièce ajoutée à l'inventaire."
            : $"[FUSION] {outcome} — les 2 pièces sont perdues.");

        _slot1Item = null;
        _slot2Item = null;
        RefreshSlots();
        RefreshPreview();
        if (fuseButton != null) fuseButton.interactable = false;
    }
}
```

- [ ] **Step 4: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 5: Wiring éditeur (manuel, hors code)**

Créer la Hierarchy `FusionUI` (panel + 2 slots d'entrée avec `FusionSlotDropTarget`
`slotIndex = 0`/`1` + slot 3 preview + textes + bouton Fuse + bouton fermer), assigner tous
les champs `public` dans l'Inspector. Même structure visuelle que `CraftPanelUI`/`ForgeUI`
(icône + nom + stats), pas de nouveau pattern à inventer.

- [ ] **Step 6: Test manuel — cas nominal**

En jeu : ouvrir l'inventaire, avoir 2 paires de gants S0 (via craft ou debug). Glisser la 1ʳᵉ
dans Slot 1, la 2ᵉ dans Slot 2. Vérifier : Slot 3 affiche "S1", résistances = somme des deux,
taux "Réussite : 100%". Cliquer Fuse → barre de canalisation → à la fin, les 2 items ont
disparu de l'inventaire, un nouvel objet S1 est apparu.

- [ ] **Step 7: Test manuel — combo invalide**

Avoir un objet S6 (via debug/plusieurs fusions) + n'importe quel autre objet. Les glisser dans
Slot 1/2. Vérifier : Slot 3 affiche "Combo invalide — dépasse S6", bouton Fuse grisé,
impossible de cliquer.

- [ ] **Step 8: Test manuel — même item aux 2 slots**

Glisser le même objet d'inventaire dans Slot 1 puis tenter de le glisser aussi dans Slot 2.
Vérifier : soit le 2ᵉ drop est refusé silencieusement (l'item ne bouge pas), soit s'il est
accepté par erreur, `RefreshPreview()` doit détecter `ReferenceEquals` et griser le bouton
avec le message "Objet identique aux 2 slots" — jamais de bouton actif dans ce cas.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/UI/Shared/FusionSlotDropTarget.cs Assets/Scripts/UI/PanelSecondaire/FusionUI.cs
git commit -m "feat: add FusionUI — 3-slot fusion panel with live preview and drag-drop input"
```

---

### Task 6: Brancher FusionUI au Cordonnier

**Files:**
- Modify: `Assets/Scripts/Entities/PNJ.cs`

**Interfaces:**
- Consumes: `FusionUI.Instance.Open(PNJData pnjData, Player player)` (Task 5).
- Produces: rien — dernière tâche du plan.

- [ ] **Step 1: Localiser le point de dispatch réel**

Dans `PNJ.cs`, méthode `HandleDialogueAction(DialogueAction action, Player player)`, la ligne
actuelle (confirmée présente dans le fichier tel quel aujourd'hui) :
```csharp
            case DialogueAction.OpenFusionUI:       Debug.Log("[PNJ] OpenFusionUI — FusionUI Phase 6"); break;
```

- [ ] **Step 2: Remplacer par l'ouverture réelle**

```csharp
            case DialogueAction.OpenFusionUI:       FusionUI.Instance?.Open(data, player); break;
```

- [ ] **Step 3: Vérifier que `Close()` est bien appelé quand la fenêtre PNJ se ferme**

Chercher dans `PNJ.cs` (ou `PNJWindowUI.cs` si c'est là que vivent les autres `Close()` du
même genre — `ForgeUI.Instance?.Close();`, `RarityUI.Instance?.Close();`) le bloc qui ferme
toutes les fenêtres PNJ à la fermeture du dialogue/de la fenêtre. Ajouter
`FusionUI.Instance?.Close();` à côté des autres `Instance?.Close()` du même bloc, pour que
Fusion se ferme comme Forge/Rareté quand le joueur quitte le PNJ.

- [ ] **Step 4: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, confirmer 0 erreur dans la Console.

- [ ] **Step 5: Test manuel — bout en bout**

Parler au PNJ Cordonnier en jeu, cliquer l'option de dialogue qui déclenche `OpenFusionUI` —
la fenêtre `FusionUI` doit s'ouvrir. Refaire le test nominal de Task 5 Step 6 depuis ce point
d'entrée réel (pas juste en activant le panel à la main dans l'éditeur).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Entities/PNJ.cs
git commit -m "feat: wire FusionUI to Cordonnier dialogue action"
```

---

## Hors scope de ce plan

- Calibrage exact des `aerisCost` par palier — laissés à `0` par défaut, à ajuster en test.
- Recettes de craft pour obtenir les toutes premières paires de gants/bottes S0 — dépend du
  contenu craft existant (`CraftGlovesBoots`), pas de ce plan.
- Runes/Gemmes — hors scope, reporté à plus tard.
