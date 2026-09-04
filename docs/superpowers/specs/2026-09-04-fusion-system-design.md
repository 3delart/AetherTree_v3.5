# Système de Fusion (Cordonnier — Gants/Bottes S0→S6)

## Contexte

Le scaffolding existe déjà depuis le refactor équipement de cette session :
`GlovesInstance.Fuse(slot1, slot2)` / `BootsInstance.Fuse(slot1, slot2)` (statiques, dans
`Data/Equipment/GlovesData.cs` / `BootsData.cs`), `fusionLevel`/résistances déjà persistés dans
les saves (`CharacterProgress.cs`, `SaveSystem.cs`). Confirmé par grep project-wide : **`Fuse()`
n'est appelé nulle part** — 100% scaffolding mort, aucun risque à le modifier.

En le relisant avant de brancher quoi que ce soit, deux incohérences trouvées :
1. Commentaire d'en-tête de `GlovesData.cs`/`BootsData.cs` : *"Résistances plafonnées à 75%"* —
   contredit par le commentaire de `Fuse()` juste en dessous (*"sans plafond, peut dépasser
   100%"*) et par `Entity.GetElementalResistance()` (aucun clamp nulle part). Le plafond 75%
   n'existe dans AUCUN code, juste un commentaire obsolète — cohérent avec la philosophie "tout
   est possible" déjà actée pour le DoT cette session.
2. `CombatSystem.CalculateDamage()` ligne 153 : `elemDamage *= (1f - elemResist)` sans clamp —
   une résistance >100% (que la Fusion permet explicitement) inverse le signe et peut annuler
   la part physique d'un skill mixte phys+élém. Pas un exploit de heal (floor final à 1 dégât),
   mais un skill mixte contre une cible sur-résistée peut tomber à 1 dégât alors que sa part
   physique aurait dû passer.

Le `Fuse()` actuel (formule `max(a,b)+1`) ne correspond pas non plus au vrai design voulu —
trouvé en comparant avec la référence NosTale (même système, taux de réussite par palier +
destruction des deux pièces à l'échec) que Florian a demandé de regarder en détail.

## Décisions actées (session de brainstorming)

### 1. Formule de palier — `a+b+1`, pas `max(a,b)+1`

```
target = slot1.fusionLevel + slot2.fusionLevel + 1
```
N'importe quelle paire (a,b) est valide tant que `target ≤ 6` — pas de contrainte symétrique
(S1+S1 comme S2+S0, les deux marchent). `target > 6` → combo invalide, refusé AVANT le jet
(bouton désactivé dans l'UI, jamais un "échec" consommant des ressources).

**Pourquoi ce changement vs `max+1`** : sous `max+1`, combiner deux pièces déjà avancées est du
gâchis (le 2ᵉ n'apporte rien de plus qu'un item frais S0) — un seul chemin optimal existe
(grinder une pièce avec du fodder S0 en boucle). Sous `a+b+1`, combiner deux pièces avancées
donne un vrai bond (ex: S2+S2→S5 au lieu de S3) — plusieurs chemins valides avec un vrai
arbitrage risque/investissement (voir exemple ci-dessous).

**Conséquence naturelle** : un item déjà S6 ne peut plus jamais servir d'input (6+0+1=7 > 6,
toujours invalide) — répond à la question initiale "faut-il bloquer la fusion à S6 max" sans
règle spéciale à coder, c'est déjà bloqué par la contrainte `target ≤ 6`.

**Exemple validé avec Florian** (bottes fraîches S0, 3% feu / 6 def all) :
- Chemin linéaire (toujours + un S0 frais) : 6 fusions, 7 bottes S0 consommées, mais tout repose
  sur UNE pièce qui traverse les 6 jets — la perdre à n'importe quelle étape perd tout.
- Chemin équilibré (construire un S2 et un S3 séparément, puis S2+S3→S6) : même coût total (6
  fusions, 7 bottes S0 — invariant, c'est un arbre de fusion à 7 feuilles quel que soit sa
  forme), mais le risque est réparti — rater une branche ne perd que cette branche.

### 2. Taux de réussite — indexés par **palier d'arrivée**, pas par la paire utilisée

Calqués directement sur la référence NosTale (confirmé par Florian) :

| Palier d'arrivée | S1   | S2   | S3  | S4  | S5  | S6  |
|-------------------|------|------|-----|-----|-----|-----|
| Taux de réussite  | 100% | 100% | 80% | 70% | 50% | 20% |

Toutes les paires valides menant au même palier d'arrivée ont le même taux (ex: S0+S5, S1+S4,
S2+S3 → toutes 20% pour viser S6).

### 3. Échec = les DEUX pièces détruites (pas juste slot1)

Contrairement à `UpgradeSystem` (échec = pas de régression, rien détruit), Fusion suit le
pattern `RaritySystem` (Pari de rareté) : coût consommé même en cas d'échec, ET en cas d'échec
**slot1 ET slot2 sont détruits** — aucune récupération, exactement comme NosTale ("tout sera
perdu : argent, ressource, pièces fusionnées").

### 4. Coût — Aeris (par palier, comme Upgrade/Rareté) + les 2 pièces elles-mêmes

Montants Aeris exacts à calibrer plus tard (comme `RarityGambleTableData`/`UpgradeTableData` —
non bloquant pour le design).

### 5. Résultat — nouvelle 3ᵉ instance, jamais de mutation en place

`slot1` et `slot2` ne sont jamais mutés. Succès → une nouvelle instance est créée :
- **Identité + défenses rollées** (mêlée/dist/magie) → héritées intégralement de **slot2**
  (`data`, `rolledRatioMelee/Ranged/Magic`).
- **Résistances élémentaires** → **additionnées** slot1 + slot2 (déjà le comportement du
  `Fuse()` actuel, confirmé correct par Florian sur un exemple concret : botte 13%feu/8%eau +
  botte 8%feu/13%eau → résultat 21%feu/21%eau).
- **`fusionLevel`** → `target` calculé en 1.

Créer une nouvelle instance plutôt que muter slot2 en place évite les bugs d'aliasing si
l'objet slot2 est référencé ailleurs (équipé, autre liste) — même piège que le bug
`InventoryItemCell` débuggé cette session.

### 6. UI — 3 slots visibles

Slot 1 (sacrifiée) + Slot 2 (sacrifiée aussi en cas d'échec, base du résultat si succès) + Slot
3 = **prévisualisation live** du résultat final (identité/def/résistances calculées, palier
cible, taux de réussite, coût Aeris) — mis à jour dès que slot1+slot2 sont tous les deux
remplis. Bouton Fuse désactivé si : un slot vide, `slot1 == slot2` (même instance
d'inventaire — validation obligatoire), ou `target > 6`.

## Architecture

### `Data/Equipment/FusionTableData.cs` (nouveau SO)

Calque `UpgradeTableData.cs`/`RarityGambleTableData.cs` — un seul asset pour tout le jeu :
```csharp
[CreateAssetMenu(fileName = "FusionTable", menuName = "AetherTree/Config/FusionTableData")]
public class FusionTableData : ScriptableObject
{
    [System.Serializable]
    public class FusionTier
    {
        public string label;              // "→ S1", "→ S2"... lisible Inspector seulement
        [Range(0f,1f)] public float successRate;
        public int aerisCost = 0;
    }

    public float channelDuration = 2.5f;  // même pattern que UpgradeTableData

    // index 0 = cible S1 ... index 5 = cible S6
    public FusionTier[] tiers = new FusionTier[]
    {
        new FusionTier { label = "→ S1", successRate = 1.00f },
        new FusionTier { label = "→ S2", successRate = 1.00f },
        new FusionTier { label = "→ S3", successRate = 0.80f },
        new FusionTier { label = "→ S4", successRate = 0.70f },
        new FusionTier { label = "→ S5", successRate = 0.50f },
        new FusionTier { label = "→ S6", successRate = 0.20f },
    };

    public FusionTier GetTier(int targetFusionLevel)
        => (targetFusionLevel >= 1 && targetFusionLevel <= 6) ? tiers[targetFusionLevel - 1] : null;
}
```

### `Data/Equipment/GlovesData.cs` / `BootsData.cs` — corriger `Fuse()`

Remplacer `fusionLevel = Mathf.Min(6, Mathf.Max(slot1.fusionLevel, slot2.fusionLevel) + 1)` par
`slot1.fusionLevel + slot2.fusionLevel + 1`, **sans** `Mathf.Min` — le clamp devient une
validation de refus en amont (voir `FusionSystem.CanFuse`), pas un clamp silencieux qui
gâcherait une paire trop ambitieuse en la ramenant à 6 quand même.

### `Systems/FusionSystem.cs` (nouveau singleton)

Calque `RaritySystem.cs` — c'est le bon modèle (jet + coût consommé même en échec, destruction
gérée par l'appelant, pas par le système lui-même) :
```csharp
public enum FusionResult { Success, Failure, InvalidCombo, SameItem, MissingResource, InvalidTarget }

public class FusionSystem : MonoBehaviour
{
    public static FusionSystem Instance { get; private set; }
    public FusionTableData table;

    public bool CanFuse(int fusionLevel1, int fusionLevel2, out int target)
    {
        target = fusionLevel1 + fusionLevel2 + 1;
        return target <= 6;
    }

    public FusionResult TryFuseGloves(GlovesInstance slot1, GlovesInstance slot2, Player player, out GlovesInstance result)
    {
        result = null;
        if (slot1 == null || slot2 == null || player == null || table == null) return FusionResult.InvalidTarget;
        if (slot1 == slot2) return FusionResult.SameItem;
        if (!CanFuse(slot1.fusionLevel, slot2.fusionLevel, out int target)) return FusionResult.InvalidCombo;

        var tier = table.GetTier(target);
        if (tier == null) return FusionResult.InvalidCombo;
        if (!TryConsumeCost(tier)) return FusionResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success) result = GlovesInstance.Fuse(slot1, slot2);
        return success ? FusionResult.Success : FusionResult.Failure;
    }

    // TryFuseBoots — même structure, symétrique

    private bool TryConsumeCost(FusionTableData.FusionTier tier)
    {
        if (tier.aerisCost <= 0) return true;
        if (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < tier.aerisCost) return false;
        AerisSystem.Instance.Spend(tier.aerisCost);
        return true;
    }
}
```
Le système ne touche jamais l'inventaire — c'est `FusionUI` qui retire slot1 ET slot2
(`InventorySystem.RemoveItem()`, déjà dispo) dans TOUS les cas, puis ajoute `result` seulement
si `FusionResult.Success`.

### `UI/PanelSecondaire/FusionUI.cs` (nouveau)

Panel partagé Gants/Bottes au Cordonnier, à côté de `CraftGlovesBoots` (déjà fait) :
- Slot 1 + Slot 2 — sélection depuis l'inventaire (drag ou clic, même pattern que
  `InventoryItemCell`/`ForgeUI`).
- Validation `slot1 != slot2` avant même d'autoriser le remplissage des deux slots avec le
  même `InventoryItem`.
- Slot 3 (preview, lecture seule) — recalculé live dès que slot1+slot2 sont remplis : identité
  (nom/icône de slot2), défenses (de slot2), résistances (somme), palier cible, taux de
  réussite (`FusionTableData.GetTier(target).successRate`), coût Aeris.
- Bouton Fuse désactivé si slot manquant, `slot1==slot2`, ou `target > 6`.
- Clic → `ProgressBarUI.BarType.Craft` (même barre que Forge/Rareté/Craft, pas de nouveau
  type) → à la résolution, appelle `FusionSystem.TryFuseGloves/Boots`, retire slot1+slot2 de
  l'inventaire dans tous les cas, ajoute le résultat si succès, affiche le résultat
  (succès/échec) à l'écran.

### `Entities/PNJ.cs` — brancher

`InteractCordonnier()` : remplace le `Debug.Log("[PNJ/Cordonnier] Fenêtre Fusion Phase 6")` par
l'ouverture réelle de `FusionUI` (même schéma que `CraftPanelUI` pour les autres stations).

### Fix indépendant — `Combat/CombatSystem.cs:153`

```csharp
elemDamage *= (1f - Mathf.Clamp01(elemResist));
```
Empêche l'inversion de signe qu'une résistance >100% (permise par Fusion) provoquerait sur un
skill mixte phys+élém. Le stat de résistance affiché/sauvegardé reste sans plafond — seul le
calcul de dégâts est protégé. Petit changement, indépendant du reste, peut se faire en premier
ou en parallèle.

## Hors scope

- Calibrage exact des coûts Aeris par palier — comme les autres systèmes de gamble/upgrade,
  laissé à `0` ou une valeur provisoire, à ajuster en test.
- UI de sélection de PIÈCE de départ pour un nouveau joueur (comment on obtient sa toute
  première paire de gants/bottes S0) — hors scope, dépend du contenu craft déjà existant
  (`CraftGlovesBoots`).
- Runes/Gemmes — explicitement reporté à plus tard (confirmé plus tôt cette session).

## Vérification

- Compiler sans erreur après les changements `GlovesData.cs`/`BootsData.cs`/nouveaux fichiers.
- En jeu : fusionner 2 bottes S0 fraîches → doit réussir à 100%, résultat S1, résistances
  sommées, def héritée de slot2 (celle glissée en 2ᵉ position).
- Tenter S6+S0 (target=7) → bouton Fuse doit rester désactivé, impossible de cliquer.
- Glisser le même item en slot1 et slot2 → doit être rejeté par l'UI, jamais assignable aux
  deux en même temps.
- Fusion échouée (forcer un taux bas en test, ex: viser S6 à 20%) → les deux items disparaissent
  de l'inventaire, rien n'est ajouté, l'Aeris est quand même dépensé.
