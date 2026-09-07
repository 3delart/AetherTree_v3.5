# Debuffs/Buffs empilables (Stats & Dot) — Design

## Contexte

`StatusEffectSystem` stocke les effets actifs de chaque entité dans deux dictionnaires :

```csharp
private Dictionary<DebuffType, DebuffInstance> _activeDebuffs;
private Dictionary<BuffType, BuffInstance>      _activeBuffs;
```

Une seule instance active par valeur d'enum. C'est le comportement voulu pour les effets
nommés (Stun, Fear, Slow, Freeze, Root, Sleep, Silence, Taunt...) — GDD §3.1.1.4 : *"un debuff
ne se cumule pas avec lui-même (refresh uniquement)"*, *"deux Burn ne se cumulent pas — le plus
récent remplace l'ancien"*.

Le problème : deux valeurs d'enum sont des **buckets génériques réutilisés pour plusieurs
effets différents** :

- `DebuffType.Stats` / `BuffType.Stats` — sert à TOUTE réduction/augmentation de stat
  individuelle via `StatModifierType` (Précision, Résistances, Défense, etc. — remplace les
  anciens ArmorBreak/Blind). Un debuff "-Précision" et un debuff "-Résistance" (sources
  différentes) sont TOUS LES DEUX du `DebuffType.Stats`.
- `DebuffType.Dot` — sert à tout dégât sur la durée élémentaire via `damageElement` (remplace
  Burn/Poison/Bleed). Un DoT Feu et un DoT Nature sont TOUS LES DEUX du `DebuffType.Dot`.

Bug confirmé : appliquer un 2e debuff Stats (ou Dot) pendant qu'un 1er est actif écrase
complètement le 1er dans le dictionnaire (même clé), au lieu de coexister — un "-Précision"
puis un "-Résistance" fait disparaître le "-Précision" entièrement, pas juste sa durée. Deux
DoT de sources différentes qui devraient cumuler leurs dégâts s'écrasent au lieu de ticker en
parallèle.

`DebuffType.Other` / `BuffType.Other` sont aussi des buckets génériques mais confirmés morts
(jamais câblés, aucun hook, marqués `[Obsolete]` côté Debuff) — hors scope.

Un fix de bug complémentaire a déjà été appliqué le même jour (2026-09-07, avant ce design) :
au refresh d'une instance existante, `existing.data` est réassignée à la nouvelle
DebuffData/BuffData avant `Refresh()` — avant ce fix, `Refresh()` relisait toujours la durée de
l'instance EXISTANTE, jamais celle du debuff nouvellement appliqué (un `Stun_Lv2` recasté sur
un `Stun_Lv1` actif se serait "refresh" à l'ancienne durée 1s au lieu de 2s). Ce fix reste
correct et n'est pas remis en cause — il est nécessaire mais pas suffisant : il corrige le
refresh d'UNE instance, ce design permet à PLUSIEURS instances de coexister.

## Décisions déjà validées (Florian, 2026-09-07)

- **Pas de clé unique par asset partout** — testé et rejeté : casserait le système de debuffs
  "à paliers texte" (ex: 3 assets `DebuffData` séparés `Stun_Lv1`/`Stun_Lv2`/`Stun_Lv3`, même
  `debuffType = Stun`, durée différente) où un SEUL doit être actif à la fois, peu importe le
  palier — le plus récent remplace l'ancien, jamais un stack Lv1+Lv2 en parallèle.
- **Diagnostic risque** : les CC purs (flags booléens `isStunned`/`isFeared`/`isRooted`/
  `isSleeping`/`isSilenced`/`isTaunted`/`isFreezed`) n'ont aujourd'hui aucun compteur, juste un
  flag simple posé/retiré. Autoriser plusieurs instances actives du même type pour CES types-là
  ne changerait rien à l'expérience joueur (stunné = stunné) mais introduirait un vrai risque de
  bug de bookkeeping (quel flag clear en premier si 2 instances expirent à des moments
  différents) pour un gain nul. Décision : ces types restent strictement single-instance.
- **Approche retenue : B — modèle uniforme `Dictionary<Type, List<Instance>>` partout**,
  choisie pour la maintenabilité/scalabilité plutôt que le moindre effort — comparée et
  préférée à : (A) un 2e dictionnaire parallèle réservé à Stats/Dot (duplique la logique à
  chaque site de lecture, dette qui s'accumule), et (C) une clé composite `(Type, Asset)` avec
  égalité conditionnelle cachée dans `Equals()`/`GetHashCode()` (peu de lignes mais règle de
  stacking invisible au site d'appel, mauvaise découvrabilité pour étendre plus tard). B coûte
  plus cher à écrire aujourd'hui mais offre un seul modèle mental, extensible sans structure
  cachée (ajouter un futur type empilable = ne pas capper sa liste à 1, visible directement).
- **Confirmé par lecture du code** (avant ce document) : `ReapplyActiveModifiers()` itère déjà
  `_activeDebuffs`/`_activeBuffs` et somme CORRECTEMENT chaque instance individuellement
  (`d.debuffStatType`, `d.debuffValue` par instance, ligne 480-483 de
  `StatusEffectSystem.cs`) — l'accumulation numérique est déjà générique et correcte, elle n'a
  simplement jamais l'occasion de voir plus d'une instance Stats à la fois. Ce design ne
  touche PAS cette logique de calcul, seulement la structure de stockage et les sites qui
  identifient "quelle instance a expiré."

## Périmètre

**Types empilables** (peuvent avoir plusieurs instances actives simultanément) :
- `DebuffType.Stats`, `DebuffType.Dot`
- `BuffType.Stats` (pas d'équivalent Dot côté buff)

**Tous les autres types** (Stun, Fear, Slow, Freeze, Root, Sleep, Silence, Taunt, Mark,
Blind\*, ArmorBreak\*, Poison\*, ManaDrain, HpDrain, Dispel côté debuff ; Heal, Regeneration,
Shield, Purified, Invincible, Stealth, Dispel\*, Revive côté buff — \* = obsolètes, gardés pour
compat) restent strictement single-instance, comportement actuel identique (refresh en place).

## Architecture

### Structure de données

```csharp
private Dictionary<DebuffType, List<DebuffInstance>> _activeDebuffs;
private Dictionary<BuffType, List<BuffInstance>>      _activeBuffs;

private static readonly HashSet<DebuffType> StackableDebuffTypes = new HashSet<DebuffType>
{
    DebuffType.Stats, DebuffType.Dot,
};
private static readonly HashSet<BuffType> StackableBuffTypes = new HashSet<BuffType>
{
    BuffType.Stats,
};
```

Une entrée de dictionnaire absente équivaut à liste vide (jamais de liste vide stockée — un
type sans instance active n'a pas de clé du tout, cohérent avec le `ContainsKey`/
`TryGetValue` d'aujourd'hui).

### Application (`TryApplyDebuff` / `ApplyBuff`)

```
si le type n'est PAS dans StackableDebuffTypes (type nommé) :
    si une liste existe pour ce type (donc 1 instance, invariant) :
        instance.data = nouveauDebuff ; instance.Refresh() ; instance.source = source
        return
    sinon : créer liste [nouvelle instance], OnApplyDebuff(instance)

si le type EST dans StackableDebuffTypes (Stats/Dot) :
    chercher dans la liste existante (s'il y en a une) une instance dont
    instance.data == nouveauDebuff (même asset)
    si trouvée : instance.data = nouveauDebuff ; instance.Refresh() ; instance.source = source
        (no-op sur data puisque même asset, mais garde le code uniforme avec le cas nommé)
    sinon : ajouter une NOUVELLE instance à la liste (créer la liste si absente),
        OnApplyDebuff(instance) — la nouvelle instance coexiste avec les autres
```

Symétrique pour `ApplyBuff`. `ApplyBuffWithDuration` (talismans) — talismans sont toujours
`BuffType.Stats` avec bonusStats ; suit la même règle de recherche par asset avant d'ajouter.

### Itération — helper d'aplatissement

```csharp
private IEnumerable<(DebuffType Type, DebuffInstance Instance)> AllDebuffInstances()
{
    foreach (var kvp in _activeDebuffs)
        foreach (var instance in kvp.Value)
            yield return (kvp.Key, instance);
}
// Équivalent AllBuffInstances() pour BuffType/BuffInstance.
```

Remplace la forme `foreach (var kvp in _activeDebuffs)` (kvp.Value = 1 instance) par
`foreach (var (type, instance) in AllDebuffInstances())` partout où le code itérait
auparavant sur "toutes les entrées". Les corps de boucle restent quasi identiques : `kvp.Key`
→ `type`, `kvp.Value` → `instance`.

Sites concernés :
- `Update()` — tick DoT/ManaDrain (debuffs) et Regeneration (buffs). Le pattern
  "snapshot avant itération" (copie dans une `List<>` avant de ticker, pour survivre à un
  `Die()` déclenché par un DoT en plein tick — voir commentaire existant ligne 178-185) est
  conservé à l'identique, juste sur la version aplatie :
  `new List<(DebuffType, DebuffInstance)>(AllDebuffInstances())`.
- `ReapplyActiveModifiers()` (ligne ~448 et ~500) — AUCUN changement de logique de calcul
  interne, juste la source de la boucle.
- `GetActiveEffectsForUI()` — 1 entrée UI par INSTANCE (pas par type) : 2 Stats actifs =
  2 icônes distinctes avec chacune son propre temps restant. Comportement correct/désiré.
- Rafraîchissement des listes debug Inspector (`_debugDebuffs`/`_debugBuffs`) — idem, 1 ligne
  par instance.
- `RemoveDebuffsByChance` / `RemoveBuffsByChance` (Purified/Dispel) — voir section dédiée.

### Expiration — instance-aware

```csharp
private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)
{
    if (!_activeDebuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
    if (list.Count == 0) _activeDebuffs.Remove(type);

    // Flags booléens — logique switch(type) INCHANGÉE (clear inconditionnel). Reste correct :
    // les types à flag (Stun, Fear...) ne sont jamais dans StackableDebuffTypes, donc `list`
    // ne contient jamais qu'un seul élément pour eux — le clear est donc toujours sûr.
    switch (type) { /* ... identique à l'actuel ExpireDebuff ... */ }

    // Recalcul — même switch(type) qu'aujourd'hui pour Slow/Freeze/Blind/ArmorBreak/Poison/
    // Mark/Stats, + le filet bonusStats. AUCUN changement : ReapplyActiveModifiers() re-somme
    // déjà depuis la liste (raccourcie), pas de logique d'annulation à écrire spécifiquement
    // pour l'instance qui vient de partir.
}

/// Convenience pour les appelants qui ne connaissent que le TYPE (OnTakeDamage → Sleep,
/// AbsorbWithShield → Shield...) — tous des types NON-empilables, où la liste ne contient
/// jamais qu'un seul élément. Pour un type empilable appelé sans instance précise (ne devrait
/// jamais arriver depuis un site à jour), expire la PREMIÈRE instance trouvée (comportement
/// dégradé documenté, pas un vrai chemin d'usage attendu).
private void ExpireDebuff(DebuffType type)
{
    if (_activeDebuffs.TryGetValue(type, out var list) && list.Count > 0)
        ExpireDebuffInstance(type, list[0]);
}
```

Symétrique pour `ExpireBuff`/`ExpireBuffInstance`.

### Purified / Dispel — jet par instance, pas par type

`RemoveDebuffsByChance`/`RemoveBuffsByChance` documentent déjà l'intention *"jet INDÉPENDANT
par effet actif — 4 debuffs à 50% ≠ 50% de tout enlever d'un coup"*. Aujourd'hui l'implémentation
roule par TYPE (`_activeDebuffs.Keys`), équivalent à "par instance" tant qu'il n'y a qu'une
instance par type. Avec ce design, elle doit rouler sur `AllDebuffInstances()`/
`AllBuffInstances()` (snapshot en `List<>` d'abord, ces méthodes mutent la collection) pour
rester fidèle à l'intention documentée : 2 Stats actifs = 2 jets indépendants.

### Accesseurs externes — signatures inchangées

```csharp
public bool HasDebuff(DebuffType type) => _activeDebuffs.TryGetValue(type, out var l) && l.Count > 0;
public bool HasBuff(BuffType type)     => _activeBuffs.TryGetValue(type, out var l) && l.Count > 0;

public Entity GetDebuffSource(DebuffType type) =>
    _activeDebuffs.TryGetValue(type, out var l) && l.Count > 0 ? l[0].source : null;
```

Vérifié : seuls 2 appelants externes existent (`Mob.cs` → `GetDebuffSource(DebuffType.Taunt)`,
`PlayerController.cs` → `GetDebuffSource(DebuffType.Fear)`), tous deux sur des types NON
empilables (liste toujours ≤1) — comportement 100% identique à aujourd'hui pour ces appels.
Pour un type empilable interrogé via `GetDebuffSource`, retourne la source de la PREMIÈRE
instance de la liste (choix arbitraire documenté, sans impact puisque non appelé
externellement pour Stats/Dot aujourd'hui).

### Sites single-instance restants (Shield, Revive, Sleep, Stealth)

`AbsorbWithShield`, `TryConsumeRevive`, `OnTakeDamage` (Sleep/Stealth) — tous des types NON
empilables. Migrent de `_activeBuffs[BuffType.X]`/`TryGetValue` direct vers
`_activeBuffs[BuffType.X][0]` (liste garantie ≤1 élément), logique inchangée sinon.

### `ClearAllEffects()`

Doit maintenant expirer TOUTES les instances de TOUS les types (pas juste itérer les clés) —
snapshot `AllDebuffInstances()`/`AllBuffInstances()` en `List<>`, puis
`ExpireDebuffInstance`/`ExpireBuffInstance` sur chaque paire. Sans ça, un 2e Stats/Dot actif
survivrait à la mort de l'entité.

## Hors scope

- `_debuffResistances` (résistance % par type, roulée une fois à l'application) — non affectée,
  concept indépendant du nombre d'instances actives.
- Renommer/retravailler `DebuffType.Other`/`BuffType.Other` — confirmés morts, non touchés.
- Étendre le système à d'AUTRES types empilables (au-delà de Stats/Dot) — hors scope de ce
  design ; l'architecture choisie (HashSet `StackableDebuffTypes`/`StackableBuffTypes`) rend
  cette extension future triviale (ajouter une valeur au HashSet) sans y toucher maintenant.

## Vérification

Pas de framework de test automatisé — vérification manuelle en Play Mode par Florian :
1. Compiler, 0 erreur.
2. Appliquer 2 debuffs Stats de sources différentes (ex: "-Précision" puis "-Résistance") sur
   un mob — vérifier dans l'Inspector debug (`_debugDebuffs`) que les DEUX apparaissent, et que
   les 2 stats sont bien réduites simultanément (CharacterPanelUI ou équivalent côté mob si
   affiché).
3. Appliquer 2 DoT de sources/éléments différents (ex: Feu + Nature) — vérifier que les DEUX
   ticks de dégâts s'appliquent (dégâts totaux par seconde = somme des deux, pas juste un DoT).
4. Réappliquer EXACTEMENT le même asset Stats/Dot pendant qu'il est déjà actif — vérifier
   refresh normal (pas de doublon dans `_debugDebuffs`, durée repart à fond).
5. Cas Stun_Lv1/Lv2 (déjà vérifié par le fix précédent, à re-tester après ce changement pour
   confirmer non-régression) : caster Lv1 puis Lv2 — un SEUL Stun actif, durée = celle de Lv2.
6. Purified/Dispel avec plusieurs Stats/DoT actifs — vérifier jets indépendants (pas
   "tout ou rien").
7. Tuer une entité avec 2+ debuffs Stats/DoT actifs — vérifier `ClearAllEffects()` retire bien
   tout (pas de stat qui reste appliquée après la mort).
