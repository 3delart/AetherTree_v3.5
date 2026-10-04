# HP/Mana — valeurs finales toujours entières

## Contexte

Florian a trouvé deux bugs d'affichage liés à l'arrondi (`CeilToInt` montrant plus de mana que
réellement disponible, `FloorToInt` affichant "0 HP" alors que le joueur était encore en vie à
0.7 HP). Ces deux bugs d'affichage ont été corrigés séparément, mais en creusant la cause
racine, Florian a posé la vraie question : pourquoi `currentHP`/`currentMana` sont-ils
fractionnaires en interne du tout ?

Réponse trouvée en lisant le code réel (pas supposée) : les dégâts de compétence sont déjà
fractionnaires aujourd'hui. `SkillSystem.cs` calcule `dmg` (float, après multiplicateurs de
crit/armure/ratio élémentaire — tous des pourcentages) et l'envoie BRUT à
`target.TakeDamage(dmg, ...)` — seul le floating text d'affichage l'arrondit
(`Mathf.RoundToInt(dmg)`), jamais la valeur réellement soustraite à `currentHP`. Idem pour la
régénération passive (`Entity.ApplyRegen()`, calculée via pourcentages de stats) et pour
plusieurs effets de statut (`StatusEffectInstance.cs` : DoT, ManaDrain, HpDrain,
Regeneration/HoT) qui appliquent tous un `taux × deltaTime` À CHAQUE FRAME — un DoT de
10 dégâts/sec à 60 FPS vaut 0.16 dégât/frame.

Décision de Florian : tout montant FINAL (pas les calculs intermédiaires — multiplicateurs,
pourcentages, formules de dégâts) appliqué à `currentHP`/`currentMana`/`maxHP`/`maxMana` doit
être un nombre entier. Les calculs eux-mêmes restent en `float` (aucune perte de précision dans
les formules), seul le moment où le résultat touche réellement une des 4 valeurs est concerné.

## Pourquoi un arrondi naïf ne suffit pas partout

Deux familles de sites de mutation existent, avec des contraintes opposées :

1. **Montants ponctuels** (un coup de compétence, un coût de cast, un vol de vie, un soin
   on-hit, une résurrection) — une seule application, jamais répétée dans la même frame.
   `Mathf.Round()` direct au point d'application suffit : la précision perdue est d'au plus
   0.5 point, sur un événement qui ne se reproduit pas frame après frame.

2. **Flux continus** (régénération passive 1×/sec, DoT/ManaDrain/HpDrain/HoT appliqués CHAQUE
   FRAME via `taux × deltaTime`) — arrondir chaque delta individuellement les détruirait
   silencieusement : un DoT à 0.16 dégât/frame arrondirait à 0 à CHAQUE frame pour toujours,
   pas juste de façon imprécise. Ces sites ont besoin d'un accumulateur qui garde le reliquat
   fractionnaire d'une frame à l'autre et ne déclenche une application réelle (`Heal`/
   `TakeDamage`/etc., toujours avec un entier) que lorsqu'au moins un point entier s'est
   accumulé — exactement le mécanisme que Florian a proposé (0.2, 0.4, 0.6, 0.8 → +1).

## Inventaire exhaustif des sites de mutation

Recherche exhaustive (`currentHP\s*=`, `currentMana\s*=`, etc.) dans tout `Assets/Scripts` —
liste complète, pas un échantillon :

| Site | Fichier | Catégorie | Traitement |
|---|---|---|---|
| `TakeDamage(float amount, ...)` | `Entity.cs:501` | Ponctuel | `Mathf.Round(amount)` |
| `Heal(float amount)` | `Entity.cs:673` | Ponctuel | `Mathf.Round(amount)` |
| `SpendMana(float amount)` | `Entity.cs:682` | Ponctuel | `Mathf.Round(amount)` |
| `SpendHP(float amount)` | `Entity.cs:692` | Ponctuel | `Mathf.Round(amount)` |
| `RecoverMana(float amount)` | `Entity.cs:698` | Ponctuel | `Mathf.Round(amount)` |
| `SetMaxHP(float value)` | `Entity.cs:822` | Ponctuel | `Mathf.Round(value)` |
| `SetMaxMana(float value)` | `Entity.cs:828` | Ponctuel | `Mathf.Round(value)` |
| `currentHP = 1f` (survie OnFatalHit) | `Entity.cs:513` | Déjà entier | Aucun |
| `currentHP = 0f` (`Die()`) | `Entity.cs:711` | Déjà entier | Aucun |
| `currentHP/Mana = max*` (spawn init) | `Entity.cs:344-345` | Dérivé de max | Couvert (max arrondi) |
| `ApplyRegen()` (regenHP/regenMana) | `Entity.cs:366-370` | **Continu (1×/sec)** | Accumulateur |
| `currentHP/Mana = max*` (reset) | `PNJ.cs:1124-1125`, `Player.cs:1515-1516,1557-1558`, `Mob.cs:184-185,384-385` | Dérivé de max | Couvert (max arrondi) |
| `Revive(hpPercent, manaPercent)` | `Player.cs:1542-1543` | **Ponctuel, bypass direct** | `Mathf.Round(maxHP * hpPercent)` |
| DoT (`DebuffType.Dot`/Burn/Bleed/Poison) | `StatusEffectInstance.cs:66-68` | **Continu (par frame)** | Accumulateur |
| ManaDrain | `StatusEffectInstance.cs:78-86` | **Continu (par frame)** | Accumulateur |
| HpDrain | `StatusEffectInstance.cs:98-106` | **Continu (par frame)** | Accumulateur |
| Regeneration/HoT (buff) | `StatusEffectInstance.cs:195-200` | **Continu (par frame)** | Accumulateur |

`Revive()` était le seul site non prévu dans la conception initiale — trouvé en vérifiant
l'inventaire avant d'écrire cette spec, PAS en test manuel. Sans ce correctif, un joueur
ressuscité à 30% d'un `maxHP` de 213 se serait vu assigner 63.9 HP bruts, recréant exactement le
bug que ce chantier corrige ailleurs.

## Design

### `Utils/FractionalAccumulator.cs` (nouveau fichier)

Struct minimaliste et réutilisable — remplace la duplication du même bloc à 4 endroits
différents (`Entity.ApplyRegen()`, et 3 cas distincts dans `StatusEffectInstance.cs`) :

```csharp
public struct FractionalAccumulator
{
    private float _remainder;

    /// <summary>Ajoute `delta` au reliquat et retourne la partie ENTIÈRE accumulée depuis la
    /// dernière extraction (0 si rien n'a encore atteint un point entier) — le reliquat
    /// fractionnaire est conservé pour le prochain appel, jamais perdu.</summary>
    public int ExtractWhole(float delta)
    {
        _remainder += delta;
        int whole = Mathf.FloorToInt(_remainder);
        if (whole > 0) _remainder -= whole;
        return whole;
    }
}
```

`Mathf.FloorToInt` (pas `Round`) est le bon choix ici précisément PARCE QUE c'est un
accumulateur : un reliquat de 0.5 n'a pas encore atteint un point entier, il doit continuer à
s'accumuler, pas être arrondi prématurément vers le haut.

### `Entity.cs`

- `TakeDamage`, `Heal`, `SpendMana`, `SpendHP`, `RecoverMana` : `amount = Mathf.Round(amount);`
  en tout début de méthode (après les premiers early-return existants comme `isDead`/`amount <=
  0f`, pour ne rien changer à leur logique de garde actuelle).
- `SetMaxHP`, `SetMaxMana` : arrondir `value` avant le `Mathf.Max(...)`/`Mathf.Clamp(...)`
  existant, pour que `maxHP`/`maxMana` eux-mêmes soient toujours entiers (sinon le clamp de
  `currentHP`/`currentMana` contre un plafond fractionnaire réintroduirait le problème).
- Deux nouveaux champs privés `FractionalAccumulator _regenHPAccum` et `_regenManaAccum`.
- `ApplyRegen()` réécrite :
  ```csharp
  private void ApplyRegen()
  {
      if (regenHP > 0f)
      {
          int whole = _regenHPAccum.ExtractWhole(regenHP);
          if (whole > 0) Heal(whole);
      }
      if (regenMana > 0f)
      {
          int whole = _regenManaAccum.ExtractWhole(regenMana);
          if (whole > 0) RecoverMana(whole);
      }
  }
  ```

### `Entities/Player.cs`

- `Revive(float hpPercent, float manaPercent)` : `currentHP = Mathf.Round(maxHP * hpPercent);` /
  `currentMana = Mathf.Round(maxMana * manaPercent);` au lieu de l'assignation brute actuelle.

### `Data/StatusEffect/StatusEffectInstance.cs`

- `DebuffInstance` : un nouveau champ privé `FractionalAccumulator _tickAccum`. Un seul champ
  suffit — `DebuffType` est unique par instance (jamais Dot ET ManaDrain en même temps sur la
  même instance), donc Dot/ManaDrain/HpDrain ne se marchent jamais dessus en se partageant le
  même accumulateur.
  - Dot (`float dmg = ComputeDotDps(target) * deltaTime;`) → `int whole =
    _tickAccum.ExtractWhole(dmg); if (whole > 0) target.TakeDamage(whole, ...);`
  - ManaDrain (`float drain = drainRate * deltaTime;`) → `int whole =
    _tickAccum.ExtractWhole(drain); if (whole > 0) { target.SpendMana(whole);
    source?.RecoverMana(whole); }`
  - HpDrain (`float hpDrain = hpDrainRate * deltaTime;`) → `int whole =
    _tickAccum.ExtractWhole(hpDrain); if (whole > 0) { target.TakeDamage(whole, ...);
    source?.Heal(whole); }`
- `BuffInstance` : un nouveau champ privé `FractionalAccumulator _regenAccum`, utilisé par
  `BuffType.Regeneration` (`float heal = BuffData.GetHealPerSecond(target.MaxHP) * deltaTime;`)
  → `int whole = _regenAccum.ExtractWhole(heal); if (whole > 0) target.Heal(whole);`

## Comportement assumé / conséquences acceptées

- Un DoT ou une régénération très faible (ex: 0.3/sec) continue de s'accumuler frame après
  frame et finit par appliquer son point entier avec un léger délai (ex: ~3.3 secondes pour le
  premier point à 0.3/sec) plutôt que de perdre la fraction — comportement voulu, c'est
  exactement le mécanisme demandé par Florian, pas un effet de bord à corriger.
- `Mathf.Round` sur les montants ponctuels peut arrondir un coup de 28.5 dégâts vers 28 OU 29
  selon la convention C# (`MidpointRounding.ToEven` par défaut) — négligeable, un seul point de
  différence sur un événement qui ne se répète pas.
- Aucune signature publique ne change (tous les paramètres restent `float` — seule leur
  INTERPRÉTATION interne devient "sera arrondi avant application"). Aucun appelant existant
  (`SkillSystem.cs`, `SkillBar.cs`, `CombatAIController.cs`, etc.) n'a besoin d'être modifié.
- Pas de changement de format de sauvegarde — vérifié : `currentHP`/`currentMana` ne sont
  persistés nulle part dans `Progression/Save/` (confirmé par recherche exhaustive), le
  personnage est toujours réinitialisé à `maxHP`/`maxMana` au chargement.
- Les écrans d'affichage HP/Mana (`FloorToInt`/`Max(1, ...)` déjà corrigés dans
  `PlayerInfosPanel.cs`/`CharacterPanelUI.cs` lors d'un chantier précédent) restent inchangés et
  redondants-mais-inoffensifs : une fois `currentHP`/`currentMana` toujours entiers en interne,
  `FloorToInt` d'un entier est un no-op.

## Fichiers touchés

- `Utils/FractionalAccumulator.cs` — nouveau fichier, struct réutilisable.
- `Entities/Entity.cs` — arrondi dans les 5 méthodes de mutation + `SetMaxHP`/`SetMaxMana`,
  accumulateurs dans `ApplyRegen()`.
- `Entities/Player.cs` — arrondi dans `Revive()`.
- `Data/StatusEffect/StatusEffectInstance.cs` — accumulateurs dans `DebuffInstance.Tick()` (Dot/
  ManaDrain/HpDrain) et `BuffInstance.Tick()` (Regeneration).

Aucun changement dans `SkillSystem.cs`, `SkillBar.cs`, `CombatAIController.cs`,
`Progression/Save/`, ou toute UI — tous restent inchangés, bénéficient du correctif
automatiquement via les méthodes `Entity.cs` qu'ils appellent déjà.

## Vérification

Pas de framework de test automatisé — vérification manuelle en Play Mode par Florian :

1. Compiler, 0 erreur.
2. Prendre un coup de compétence avec crit/réduction d'armure (valeurs qui produisaient
   auparavant un `dmg` fractionnaire) → `CurrentHP` après le coup est un entier exact (vérifier
   via un breakpoint/log temporaire ou l'affichage, déjà en `FloorToInt`).
3. Rester immobile hors combat avec `regenHP`/`regenMana` fractionnaire (ex: 0.3/sec) pendant
   10+ secondes → la régénération progresse bien par paliers entiers avec un léger délai au
   démarrage, jamais bloquée à 0.
4. Se faire poser un DoT avec un faible DPS (ex: 1-2 dégâts/sec) → les dégâts s'appliquent
   toujours (pas de DoT silencieusement neutralisé par l'arrondi par-frame).
5. Mourir et utiliser `Revive()` (30% HP/Mana par défaut) → `CurrentHP`/`CurrentMana` après
   résurrection sont des entiers exacts, pas de résidu fractionnaire.
6. Cas limite : un DoT très faible (ex: 0.05/sec) sur un combat court (< 20s) → aucun dégât
   appliqué pendant tout le combat est un comportement ATTENDU (le reliquat n'a jamais atteint
   1 point entier), pas un bug à signaler.
