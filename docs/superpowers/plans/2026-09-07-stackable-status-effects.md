# Stackable Status Effects (Stats/Dot) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `DebuffType.Stats`/`DebuffType.Dot`/`BuffType.Stats` (generic buckets reused by many different concrete effects) hold multiple simultaneous instances per entity, instead of one silently evicting another, while every other debuff/buff type keeps its current single-instance "most recent replaces" behavior unchanged.

**Architecture:** Change `_activeDebuffs`/`_activeBuffs` from `Dictionary<Type, Instance>` to `Dictionary<Type, List<Instance>>`. A small `StackableDebuffTypes`/`StackableBuffTypes` HashSet decides, at apply time, whether a type ever holds more than one list entry. Two flattening iterator helpers (`AllDebuffInstances()`/`AllBuffInstances()`) let every existing "iterate all active effects" call site keep the same loop *body*, just a different loop *source*. Expiration becomes instance-aware (`ExpireDebuffInstance`/`ExpireBuffInstance`) instead of type-only, with a thin type-only convenience wrapper for the many callers that only ever deal with single-instance types.

**Tech Stack:** Unity 2022+, C# 9, no automated test framework (manual Play Mode verification).

**Spec:** `docs/superpowers/specs/2026-09-07-stackable-status-effects-design.md`

## Global Constraints

- Only `Entities/StatusEffectSystem.cs` changes. No other file in the project needs edits — verified: the only external callers of a touched public method (`GetDebuffSource`) are `Entities/Mob.cs:219` (`GetDebuffSource(DebuffType.Taunt)`) and `World/PlayerController.cs:74` (`GetDebuffSource(DebuffType.Fear)`), both non-stackable types, and the public signatures of `HasDebuff`/`HasBuff`/`GetDebuffSource` do not change.
- No enum changes. `DebuffType`/`BuffType` ordinals are untouched — zero save/asset compatibility risk.
- Stackable types: `DebuffType.Stats`, `DebuffType.Dot`, `BuffType.Stats`. Every other type stays strictly single-instance (list always has 0 or 1 entries), identical observable behavior to today.
- The flag-clearing `switch (type)` inside expiration must be copied verbatim from the current `ExpireDebuff`/`ExpireBuff` — do not "improve" or add defensive logic to it. It stays correct under the new model precisely because flag-carrying types are never stackable, so their list never holds more than one entry.
- The `existing.data = <newAsset>` line (already present in current `TryApplyDebuff`/`ApplyBuff`, added earlier the same day to fix a stale-refresh bug) must be preserved in the new apply logic — it is not a regression to reintroduce, it is required.
- No automated test suite exists for this project. Verification is manual, in Unity Play Mode, by Florian — Task 2 is that checklist, not code.

---

### Task 1: Migrate `_activeDebuffs`/`_activeBuffs` to list-per-type storage

**Files:**
- Modify: `Entities/StatusEffectSystem.cs` (all line numbers below refer to the file's state at the start of this task — re-locate by searching for the quoted old code, since earlier steps in this same task shift later line numbers)

**Interfaces:**
- Produces (used by Task 2's manual verification, and by any future code touching this file):
  - `private Dictionary<DebuffType, List<DebuffInstance>> _activeDebuffs`
  - `private Dictionary<BuffType, List<BuffInstance>> _activeBuffs`
  - `private static readonly HashSet<DebuffType> StackableDebuffTypes` (contains `DebuffType.Stats`, `DebuffType.Dot`)
  - `private static readonly HashSet<BuffType> StackableBuffTypes` (contains `BuffType.Stats`)
  - `private IEnumerable<(DebuffType Type, DebuffInstance Instance)> AllDebuffInstances()`
  - `private IEnumerable<(BuffType Type, BuffInstance Instance)> AllBuffInstances()`
  - `private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)`
  - `private void ExpireDebuff(DebuffType type)` (unchanged public/private-ness and signature, now a thin wrapper)
  - `private void ExpireBuffInstance(BuffType type, BuffInstance instance)`
  - `private void ExpireBuff(BuffType type)` (unchanged signature, now a thin wrapper)
  - `public bool HasDebuff(DebuffType type)` / `public bool HasBuff(BuffType type)` — signatures unchanged
  - `public Entity GetDebuffSource(DebuffType type)` — signature unchanged

This is one atomic task because every one of the ~20 sites below reads or writes the same two fields — a partial migration does not compile in C#, so there is no meaningful earlier stopping point than "all sites updated." Steps are still small and ordered; do them in order and expect the project **not** to compile until Step 12.

- [ ] **Step 1: Change the field declarations and add the stackable-type sets**

Find (near the top of the class, `_activeDebuffs`/`_activeBuffs` declarations):

```csharp
    // ── Effets actifs ─────────────────────────────────────────
    private Dictionary<DebuffType, DebuffInstance> _activeDebuffs
        = new Dictionary<DebuffType, DebuffInstance>();

    private Dictionary<BuffType, BuffInstance> _activeBuffs
        = new Dictionary<BuffType, BuffInstance>();
```

Replace with:

```csharp
    // ── Effets actifs ─────────────────────────────────────────
    // Dictionary<Type, List<Instance>> plutôt que Dictionary<Type, Instance> — la plupart des
    // types n'ont jamais qu'une instance (StackableDebuffTypes/StackableBuffTypes vides pour
    // eux), mais Stats/Dot sont des buckets génériques réutilisés par plusieurs effets
    // DIFFÉRENTS (ex: debuff "-Précision" et debuff "-Résistance" sont tous les deux
    // DebuffType.Stats) qui doivent pouvoir coexister au lieu de s'écraser. Voir
    // docs/superpowers/specs/2026-09-07-stackable-status-effects-design.md.
    private Dictionary<DebuffType, List<DebuffInstance>> _activeDebuffs
        = new Dictionary<DebuffType, List<DebuffInstance>>();

    private Dictionary<BuffType, List<BuffInstance>> _activeBuffs
        = new Dictionary<BuffType, List<BuffInstance>>();

    /// <summary>Types dont PLUSIEURS instances peuvent être actives simultanément (buckets
    /// génériques réutilisés par plusieurs effets différents). Tout le reste (Stun, Fear,
    /// Slow...) reste strictement 1 instance active à la fois — même comportement qu'avant
    /// ce changement.</summary>
    private static readonly HashSet<DebuffType> StackableDebuffTypes = new HashSet<DebuffType>
    {
        DebuffType.Stats, DebuffType.Dot,
    };
    private static readonly HashSet<BuffType> StackableBuffTypes = new HashSet<BuffType>
    {
        BuffType.Stats,
    };

    /// <summary>Aplatit _activeDebuffs en paires (type, instance) — une par instance, pas une
    /// par type. Pour les types non-stackable, équivalent à itérer l'ancien dictionnaire à
    /// plat (liste ≤1). Snapshot-le dans une List&lt;&gt; avant d'itérer si le corps de boucle
    /// peut muter _activeDebuffs (voir Update()).</summary>
    private IEnumerable<(DebuffType Type, DebuffInstance Instance)> AllDebuffInstances()
    {
        foreach (var kvp in _activeDebuffs)
            foreach (var instance in kvp.Value)
                yield return (kvp.Key, instance);
    }

    private IEnumerable<(BuffType Type, BuffInstance Instance)> AllBuffInstances()
    {
        foreach (var kvp in _activeBuffs)
            foreach (var instance in kvp.Value)
                yield return (kvp.Key, instance);
    }
```

- [ ] **Step 2: Update `GetDebuffSource`**

Find:

```csharp
    public Entity GetDebuffSource(DebuffType type)
        => _activeDebuffs.TryGetValue(type, out var instance) ? instance.source : null;
```

Replace with:

```csharp
    /// <summary>Pour un type stackable avec plusieurs instances actives, retourne la source de
    /// la PREMIÈRE de la liste (choix arbitraire, documenté — aucun appelant externe ne
    /// l'utilise pour un type stackable aujourd'hui : Mob.cs et PlayerController.cs
    /// n'appellent ceci que pour Taunt/Fear, non-stackable, liste toujours ≤1).</summary>
    public Entity GetDebuffSource(DebuffType type)
        => _activeDebuffs.TryGetValue(type, out var list) && list.Count > 0 ? list[0].source : null;
```

- [ ] **Step 3: Update the `Update()` tick loop**

Find:

```csharp
        var expiredDebuffs = new List<DebuffType>();
        foreach (var kvp in new List<KeyValuePair<DebuffType, DebuffInstance>>(_activeDebuffs))
        {
            kvp.Value.Tick(_entity, Time.deltaTime);
            if (kvp.Value.IsExpired) expiredDebuffs.Add(kvp.Key);
        }
        foreach (var t in expiredDebuffs) ExpireDebuff(t);

        // Tick buffs (Regeneration via BuffInstance.Tick) — même précaution, voir ci-dessus.
        var expiredBuffs = new List<BuffType>();
        foreach (var kvp in new List<KeyValuePair<BuffType, BuffInstance>>(_activeBuffs))
        {
            kvp.Value.Tick(_entity, Time.deltaTime);
            if (kvp.Value.IsExpired) expiredBuffs.Add(kvp.Key);
        }
        foreach (var t in expiredBuffs) ExpireBuff(t);

        // Refresh debug lists
        _debugDebuffs.Clear();
        foreach (var kvp in _activeDebuffs)
            _debugDebuffs.Add($"{kvp.Key} — {kvp.Value.remainingTime:F1}s");

        _debugBuffs.Clear();
        foreach (var kvp in _activeBuffs)
            _debugBuffs.Add($"{kvp.Key} — {kvp.Value.remainingTime:F1}s");
```

Replace with:

```csharp
        var expiredDebuffs = new List<(DebuffType Type, DebuffInstance Instance)>();
        foreach (var (type, instance) in new List<(DebuffType, DebuffInstance)>(AllDebuffInstances()))
        {
            instance.Tick(_entity, Time.deltaTime);
            if (instance.IsExpired) expiredDebuffs.Add((type, instance));
        }
        foreach (var (type, instance) in expiredDebuffs) ExpireDebuffInstance(type, instance);

        // Tick buffs (Regeneration via BuffInstance.Tick) — même précaution, voir ci-dessus.
        var expiredBuffs = new List<(BuffType Type, BuffInstance Instance)>();
        foreach (var (type, instance) in new List<(BuffType, BuffInstance)>(AllBuffInstances()))
        {
            instance.Tick(_entity, Time.deltaTime);
            if (instance.IsExpired) expiredBuffs.Add((type, instance));
        }
        foreach (var (type, instance) in expiredBuffs) ExpireBuffInstance(type, instance);

        // Refresh debug lists — 1 ligne par INSTANCE, pas par type (2 Stats actifs = 2 lignes).
        _debugDebuffs.Clear();
        foreach (var (type, instance) in AllDebuffInstances())
            _debugDebuffs.Add($"{type} — {instance.remainingTime:F1}s");

        _debugBuffs.Clear();
        foreach (var (type, instance) in AllBuffInstances())
            _debugBuffs.Add($"{type} — {instance.remainingTime:F1}s");
```

- [ ] **Step 4: Update `TryApplyDebuff`**

Find:

```csharp
        // Refresh si déjà actif — GDD v3.5 §3.1.1.4 : le plus récent remplace l'ancien.
        // `data` AUSSI remplacée (pas juste la durée) — sinon Refresh() relit data.duration de
        // L'ANCIEN debuff, jamais celui du nouveau : un Stun_Lv2 (2s) recast sur une cible qui a
        // déjà Stun_Lv1 (1s) actif refresh silencieusement à 1s, ignorant Lv2 (bug trouvé
        // 2026-09-07, en prévision du système de debuffs "à paliers texte" — ex: Stun_Lv1=1s,
        // Lv2=2s, Lv3=3s, plusieurs DebuffData distinctes partageant le même DebuffType).
        // Source aussi mise à jour — sinon un 2e caster qui relance le même debuff (ex: Fear)
        // laisse GetDebuffSource() pointer vers le PREMIER caster, périmé (fuite dans la
        // mauvaise direction).
        if (_activeDebuffs.TryGetValue(debuff.debuffType, out var existing))
        {
            existing.data = debuff;
            existing.Refresh();
            existing.source = source;
            return true;
        }

        // Nouvelle application
        DebuffInstance instance = (DebuffInstance)debuff.CreateInstance(source);
        _activeDebuffs[debuff.debuffType] = instance;
        OnApplyDebuff(instance);

        return true;
    }
```

Replace with:

```csharp
        // Types NON stackable (Stun, Fear, Slow...) — GDD v3.5 §3.1.1.4 : le plus récent
        // remplace l'ancien, une seule instance possible, jamais plus. `data` AUSSI remplacée
        // (pas juste la durée) — sinon Refresh() relit data.duration de L'ANCIEN debuff, jamais
        // celui du nouveau : un Stun_Lv2 (2s) recast sur une cible qui a déjà Stun_Lv1 (1s)
        // actif refresh silencieusement à 1s, ignorant Lv2 (bug trouvé 2026-09-07, en
        // prévision du système de debuffs "à paliers texte" — ex: Stun_Lv1=1s, Lv2=2s, Lv3=3s,
        // plusieurs DebuffData distinctes partageant le même DebuffType). Source aussi mise à
        // jour — sinon un 2e caster qui relance le même debuff (ex: Fear) laisse
        // GetDebuffSource() pointer vers le PREMIER caster, périmé (fuite dans la mauvaise
        // direction).
        if (!StackableDebuffTypes.Contains(debuff.debuffType))
        {
            if (_activeDebuffs.TryGetValue(debuff.debuffType, out var existingList) && existingList.Count > 0)
            {
                var existing = existingList[0];
                existing.data = debuff;
                existing.Refresh();
                existing.source = source;
                return true;
            }

            DebuffInstance newInstance = (DebuffInstance)debuff.CreateInstance(source);
            _activeDebuffs[debuff.debuffType] = new List<DebuffInstance> { newInstance };
            OnApplyDebuff(newInstance);
            return true;
        }

        // Types STACKABLE (Stats, Dot) — plusieurs effets différents partagent le même
        // DebuffType (ex: "-Précision" et "-Résistance" sont tous les deux Stats). Recast du
        // MÊME asset = refresh normal ; asset différent = coexiste en plus, ne remplace rien.
        if (_activeDebuffs.TryGetValue(debuff.debuffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == debuff);
            if (sameAsset != null)
            {
                sameAsset.data = debuff;
                sameAsset.Refresh();
                sameAsset.source = source;
                return true;
            }
        }
        else
        {
            list = new List<DebuffInstance>();
            _activeDebuffs[debuff.debuffType] = list;
        }

        DebuffInstance stackedInstance = (DebuffInstance)debuff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyDebuff(stackedInstance);

        return true;
    }
```

- [ ] **Step 5: Update `ApplyBuff`**

Find:

```csharp
        // Refresh si déjà actif — même correction que TryApplyDebuff : `data` remplacée avant
        // Refresh() pour qu'un Buff_Lv2 recast sur un Buff_Lv1 actif prenne bien la durée (et
        // toute autre valeur) du Lv2, pas un refresh silencieux vers l'ancienne.
        if (_activeBuffs.TryGetValue(buff.buffType, out var existing))
        {
            existing.data = buff;
            existing.Refresh();
            existing.source = source; // Revive : re-cast met à jour QUI recevra le crédit/log au déclenchement
            return;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        _activeBuffs[buff.buffType] = instance;
        OnApplyBuff(instance);
    }
```

Replace with:

```csharp
        // Types NON stackable — même correction que TryApplyDebuff : `data` remplacée avant
        // Refresh() pour qu'un Buff_Lv2 recast sur un Buff_Lv1 actif prenne bien la durée (et
        // toute autre valeur) du Lv2, pas un refresh silencieux vers l'ancienne.
        if (!StackableBuffTypes.Contains(buff.buffType))
        {
            if (_activeBuffs.TryGetValue(buff.buffType, out var existingList) && existingList.Count > 0)
            {
                var existing = existingList[0];
                existing.data = buff;
                existing.Refresh();
                existing.source = source; // Revive : re-cast met à jour QUI recevra le crédit/log au déclenchement
                return;
            }

            BuffInstance newInstance = (BuffInstance)buff.CreateInstance(source);
            _activeBuffs[buff.buffType] = new List<BuffInstance> { newInstance };
            OnApplyBuff(newInstance);
            return;
        }

        // Types STACKABLE (Stats) — même principe que TryApplyDebuff : même asset = refresh,
        // asset différent = coexiste.
        if (_activeBuffs.TryGetValue(buff.buffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == buff);
            if (sameAsset != null)
            {
                sameAsset.data = buff;
                sameAsset.Refresh();
                sameAsset.source = source;
                return;
            }
        }
        else
        {
            list = new List<BuffInstance>();
            _activeBuffs[buff.buffType] = list;
        }

        BuffInstance stackedInstance = (BuffInstance)buff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyBuff(stackedInstance);
    }
```

- [ ] **Step 6: Update `ApplyBuffWithDuration`**

Find:

```csharp
    public void ApplyBuffWithDuration(BuffData buff, Entity source, float remainingSeconds)
    {
        if (buff == null || _entity.isDead || remainingSeconds <= 0f) return;

        if (_activeBuffs.TryGetValue(buff.buffType, out var existing))
        {
            existing.remainingTime = remainingSeconds;
            existing.source = source;
            return;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
        _activeBuffs[buff.buffType] = instance;
        OnApplyBuff(instance);
    }
```

Replace with:

```csharp
    public void ApplyBuffWithDuration(BuffData buff, Entity source, float remainingSeconds)
    {
        if (buff == null || _entity.isDead || remainingSeconds <= 0f) return;

        // Talismans sont toujours BuffType.Stats (stackable) — même règle "même asset = refresh,
        // asset différent = coexiste" que ApplyBuff, mais durée explicite au lieu de buff.duration.
        if (_activeBuffs.TryGetValue(buff.buffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == buff);
            if (sameAsset != null)
            {
                sameAsset.remainingTime = remainingSeconds;
                sameAsset.source = source;
                return;
            }
        }
        else
        {
            list = new List<BuffInstance>();
            _activeBuffs[buff.buffType] = list;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
        list.Add(instance);
        OnApplyBuff(instance);
    }
```

- [ ] **Step 7: Update `ReapplyActiveModifiers`'s two iteration loops**

Find (debuff loop):

```csharp
        foreach (var kvp in _activeDebuffs)
        {
            var d = kvp.Value.DebuffData;
            switch (kvp.Key)
            {
```

Replace with:

```csharp
        foreach (var (type, instance) in AllDebuffInstances())
        {
            var d = instance.DebuffData;
            switch (type)
            {
```

The switch's case bodies (`DebuffType.Slow`, `DebuffType.Freeze`, `DebuffType.Blind`, `DebuffType.Poison`, `DebuffType.ArmorBreak`, `DebuffType.Mark`, `DebuffType.Stats`) reference only `d` (already extracted above) and its fields (`d.slowMultiplier`, `d.debuffValue`, `d.healReduction`, `d.defenseReduction`, `d.markDamageBonusPercent`, `d.debuffStatType`, `d.debuffModifier`, `d.debuffValue`) — none of them reference `kvp.Key` or `kvp.Value` directly, so no further edits are needed inside the switch body itself. The `AccumulateBonusStatsLines(flatSum, percentSum, d.bonusStats, sign: -1f);` line right after the switch's closing brace also stays untouched — it already reads from `d`, not from the loop variable.

Find (buff loop):

```csharp
        foreach (var kvp in _activeBuffs)
        {
            if (kvp.Key == BuffType.Stats)
            {
                var b = kvp.Value.BuffData;
                AccumulateStatLine(flatSum, percentSum, b.buffStatType,
                    b.buffModifier == ModifierType.Percent, b.buffStatValue);
            }
            AccumulateBonusStatsLines(flatSum, percentSum, kvp.Value.BuffData.bonusStats, sign: 1f);
        }
```

Replace with:

```csharp
        foreach (var (type, instance) in AllBuffInstances())
        {
            if (type == BuffType.Stats)
            {
                var b = instance.BuffData;
                AccumulateStatLine(flatSum, percentSum, b.buffStatType,
                    b.buffModifier == ModifierType.Percent, b.buffStatValue);
            }
            AccumulateBonusStatsLines(flatSum, percentSum, instance.BuffData.bonusStats, sign: 1f);
        }
```

- [ ] **Step 8: Update `RemoveDebuffsByChance`/`RemoveBuffsByChance` (Purified/Dispel — 1 roll per instance, not per type)**

Find:

```csharp
    private void RemoveDebuffsByChance(float chance)
    {
        var types = new List<DebuffType>(_activeDebuffs.Keys);
        foreach (DebuffType t in types)
            if (Random.value < chance)
                ExpireDebuff(t);
    }

    /// <summary>Dispel — même principe que RemoveDebuffsByChance, mais sur les buffs actifs de
    /// LA CIBLE sur qui ce debuff Dispel a été appliqué (§3.1.1.3).</summary>
    private void RemoveBuffsByChance(float chance)
    {
        var types = new List<BuffType>(_activeBuffs.Keys);
        foreach (BuffType t in types)
            if (Random.value < chance)
                ExpireBuff(t);
    }
```

Replace with:

```csharp
    private void RemoveDebuffsByChance(float chance)
    {
        // 1 jet par INSTANCE active, pas par type — 2 debuffs Stats actifs = 2 jets
        // indépendants (fidèle au commentaire ci-dessus, qui ne faisait aucune différence
        // avant que Stats/Dot puissent avoir plus d'une instance).
        var snapshot = new List<(DebuffType Type, DebuffInstance Instance)>(AllDebuffInstances());
        foreach (var (type, instance) in snapshot)
            if (Random.value < chance)
                ExpireDebuffInstance(type, instance);
    }

    /// <summary>Dispel — même principe que RemoveDebuffsByChance, mais sur les buffs actifs de
    /// LA CIBLE sur qui ce debuff Dispel a été appliqué (§3.1.1.3).</summary>
    private void RemoveBuffsByChance(float chance)
    {
        var snapshot = new List<(BuffType Type, BuffInstance Instance)>(AllBuffInstances());
        foreach (var (type, instance) in snapshot)
            if (Random.value < chance)
                ExpireBuffInstance(type, instance);
    }
```

- [ ] **Step 9: Replace `ExpireDebuff` with instance-aware `ExpireDebuffInstance` + thin wrapper**

Find:

```csharp
    private void ExpireDebuff(DebuffType type)
    {
        if (!_activeDebuffs.TryGetValue(type, out var expiring)) return;
        var expiringBonusStats = expiring.DebuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case DebuffType.Stun:    isStunned  = false; break;
            case DebuffType.Fear:    isFeared   = false; break;
            case DebuffType.Root:    isRooted   = false; break;
            case DebuffType.Sleep:   isSleeping = false; break;
            case DebuffType.Shocked: isShocked  = false; break;
            case DebuffType.Silence: isSilenced = false; break;
            case DebuffType.Taunt:   isTaunted  = false; break;
            case DebuffType.Freeze:  isFreezed  = false; break;

            // Flags dérivés de valeurs numériques — recalculés dans ReapplyActiveModifiers
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:      isBlinded     = false; break;
            case DebuffType.ArmorBreak: isArmorBroken = false; break;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:     isPoisoned    = false; break;
#pragma warning restore CS0618
            case DebuffType.Mark:       isMarked      = false; break;
        }

        // ⚠ Remove AVANT RecalculateAndReapply — sinon l'effet expiré est encore
        // dans le dict et ses valeurs sont ré-appliquées à tort.
        _activeDebuffs.Remove(type);

        // ── Valeurs numériques — recalcul propre ──────────────
        // Tout effet qui touche des stats ou des multiplicateurs numériques
        // déclenche un recalcul. Les flags purs (Stun, Fear, Shocked...) n'en ont pas besoin.
        switch (type)
        {
            case DebuffType.Slow:
            case DebuffType.Freeze:
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:
            case DebuffType.ArmorBreak:
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:
#pragma warning restore CS0618
            case DebuffType.Mark:
            case DebuffType.Stats:
                RecalculateAndReapply();
                return; // déjà fait — évite le 2e appel juste en dessous (idempotent de toute
                        // façon, mais inutile de le refaire tout de suite après)
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL debuffType (Fear, Stun, Root...), pas
        // seulement les types déjà listés ci-dessus — sans ce filet, un debuff Fear avec un
        // bonusStats -MoveSpeed resterait appliqué POUR TOUJOURS après expiration (aucun des
        // cases ci-dessus ne le recalcule), jusqu'à ce qu'un événement sans rapport
        // (équipement, level up...) déclenche un recalcul complet par ailleurs.
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
    }
```

Replace with:

```csharp
    /// <summary>Expire UNE instance précise (nécessaire pour les types stackable, où plusieurs
    /// instances du même type peuvent coexister — retirer "le type" retirerait tout au lieu
    /// d'une seule). Le switch(type) de flags reste correct tel quel : les types à flag ne sont
    /// jamais dans StackableDebuffTypes, donc leur liste ne contient jamais qu'un seul élément
    /// — le clear inconditionnel du flag est donc toujours sûr, peu importe qu'on l'appelle
    /// "par type" ou "par instance".</summary>
    private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)
    {
        if (!_activeDebuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeDebuffs.Remove(type);

        var expiringBonusStats = instance.DebuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case DebuffType.Stun:    isStunned  = false; break;
            case DebuffType.Fear:    isFeared   = false; break;
            case DebuffType.Root:    isRooted   = false; break;
            case DebuffType.Sleep:   isSleeping = false; break;
            case DebuffType.Shocked: isShocked  = false; break;
            case DebuffType.Silence: isSilenced = false; break;
            case DebuffType.Taunt:   isTaunted  = false; break;
            case DebuffType.Freeze:  isFreezed  = false; break;

            // Flags dérivés de valeurs numériques — recalculés dans ReapplyActiveModifiers
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:      isBlinded     = false; break;
            case DebuffType.ArmorBreak: isArmorBroken = false; break;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:     isPoisoned    = false; break;
#pragma warning restore CS0618
            case DebuffType.Mark:       isMarked      = false; break;
        }

        // ── Valeurs numériques — recalcul propre ──────────────
        // Tout effet qui touche des stats ou des multiplicateurs numériques
        // déclenche un recalcul. Les flags purs (Stun, Fear, Shocked...) n'en ont pas besoin.
        switch (type)
        {
            case DebuffType.Slow:
            case DebuffType.Freeze:
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:
            case DebuffType.ArmorBreak:
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:
#pragma warning restore CS0618
            case DebuffType.Mark:
            case DebuffType.Stats:
                RecalculateAndReapply();
                return; // déjà fait — évite le 2e appel juste en dessous (idempotent de toute
                        // façon, mais inutile de le refaire tout de suite après)
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL debuffType (Fear, Stun, Root...), pas
        // seulement les types déjà listés ci-dessus — sans ce filet, un debuff Fear avec un
        // bonusStats -MoveSpeed resterait appliqué POUR TOUJOURS après expiration (aucun des
        // cases ci-dessus ne le recalcule), jusqu'à ce qu'un événement sans rapport
        // (équipement, level up...) déclenche un recalcul complet par ailleurs.
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
    }

    /// <summary>Convenience pour les appelants qui ne connaissent que le TYPE (OnTakeDamage →
    /// Sleep, AbsorbWithShield → Shield...) — tous des types NON stackable, où la liste ne
    /// contient jamais qu'un seul élément. Pour un type stackable appelé sans instance précise,
    /// expire la PREMIÈRE instance trouvée (aucun site à jour de ce plan ne fait ça — filet de
    /// sécurité documenté, pas un vrai chemin d'usage attendu).</summary>
    private void ExpireDebuff(DebuffType type)
    {
        if (_activeDebuffs.TryGetValue(type, out var list) && list.Count > 0)
            ExpireDebuffInstance(type, list[0]);
    }
```

- [ ] **Step 10: Replace `ExpireBuff` with instance-aware `ExpireBuffInstance` + thin wrapper**

Find:

```csharp
    private void ExpireBuff(BuffType type)
    {
        if (!_activeBuffs.TryGetValue(type, out var expiring)) return;
        var expiringBonusStats = expiring.BuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case BuffType.Invincible: isInvincible = false; break;
            case BuffType.Stealth:    isStealthed  = false; break;
        }

        // ⚠ Remove AVANT RecalculateAndReapply — même raison que pour les debuffs.
        _activeBuffs.Remove(type);

        // ── Valeurs numériques — recalcul propre ──────────────
        switch (type)
        {
            case BuffType.Stats:
                RecalculateAndReapply();
                return; // déjà fait — voir même remarque que ExpireDebuff
            // Purified, Heal : instantanés — pas d'expiration
            // Regeneration, Shield, Invincible, Stealth : pas de stat numérique à recalculer
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL buffType (Heal, Shield, Talisman...),
        // pas seulement Stats — sans ce filet, ses bonus resteraient appliqués POUR TOUJOURS
        // après expiration (voir même remarque que ExpireDebuff ci-dessus).
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
    }
```

Replace with:

```csharp
    /// <summary>Équivalent buff de ExpireDebuffInstance — voir son commentaire pour le
    /// raisonnement complet (flag-clear inconditionnel toujours sûr car les types à flag
    /// Invincible/Stealth ne sont jamais dans StackableBuffTypes).</summary>
    private void ExpireBuffInstance(BuffType type, BuffInstance instance)
    {
        if (!_activeBuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeBuffs.Remove(type);

        var expiringBonusStats = instance.BuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case BuffType.Invincible: isInvincible = false; break;
            case BuffType.Stealth:    isStealthed  = false; break;
        }

        // ── Valeurs numériques — recalcul propre ──────────────
        switch (type)
        {
            case BuffType.Stats:
                RecalculateAndReapply();
                return; // déjà fait — voir même remarque que ExpireDebuffInstance
            // Purified, Heal : instantanés — pas d'expiration
            // Regeneration, Shield, Invincible, Stealth : pas de stat numérique à recalculer
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL buffType (Heal, Shield, Talisman...),
        // pas seulement Stats — sans ce filet, ses bonus resteraient appliqués POUR TOUJOURS
        // après expiration (voir même remarque que ExpireDebuffInstance ci-dessus).
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
    }

    /// <summary>Convenience type-only — voir ExpireDebuff (équivalent debuff) pour le
    /// raisonnement complet.</summary>
    private void ExpireBuff(BuffType type)
    {
        if (_activeBuffs.TryGetValue(type, out var list) && list.Count > 0)
            ExpireBuffInstance(type, list[0]);
    }
```

- [ ] **Step 11: Update the remaining single-instance direct-dictionary sites**

Find (`RemoveBuff`, public):

```csharp
    public void RemoveBuff(BuffType type)
    {
        if (_activeBuffs.ContainsKey(type))
            ExpireBuff(type);
    }
```

Replace with (unchanged logic — `ExpireBuff(type)` already does the right thing via its new thin-wrapper form, `ContainsKey` still works identically on the outer dictionary):

```csharp
    public void RemoveBuff(BuffType type)
    {
        if (_activeBuffs.ContainsKey(type))
            ExpireBuff(type);
    }
```

(No textual change needed for this one — confirm it still compiles once Steps 1 and 10 are in place, since `ExpireBuff(BuffType)` keeps its exact signature.)

Find (`TryConsumeRevive`):

```csharp
    public bool TryConsumeRevive(out float delay, out float hpPercent, out float manaPercent)
    {
        if (_activeBuffs.TryGetValue(BuffType.Revive, out var instance))
        {
            var d = instance.BuffData;
            delay = d.reviveDelay;
            hpPercent = d.reviveHPPercent;
            manaPercent = d.reviveManaPercent;
            _activeBuffs.Remove(BuffType.Revive);
            return true;
        }
        delay = hpPercent = manaPercent = 0f;
        return false;
    }
```

Replace with:

```csharp
    public bool TryConsumeRevive(out float delay, out float hpPercent, out float manaPercent)
    {
        if (_activeBuffs.TryGetValue(BuffType.Revive, out var list) && list.Count > 0)
        {
            var d = list[0].BuffData;
            delay = d.reviveDelay;
            hpPercent = d.reviveHPPercent;
            manaPercent = d.reviveManaPercent;
            _activeBuffs.Remove(BuffType.Revive);
            return true;
        }
        delay = hpPercent = manaPercent = 0f;
        return false;
    }
```

Find (`ClearAllEffects`):

```csharp
    public void ClearAllEffects()
    {
        var debuffKeys = new List<DebuffType>(_activeDebuffs.Keys);
        foreach (var t in debuffKeys) ExpireDebuff(t);

        var buffKeys = new List<BuffType>(_activeBuffs.Keys);
        foreach (var t in buffKeys) ExpireBuff(t);
    }
```

Replace with:

```csharp
    public void ClearAllEffects()
    {
        // Snapshot-et-expire TOUTES les instances aplaties, pas juste les clés de type — sinon
        // un 2e Stats/Dot actif (2e élément de la liste) survivrait à la mort de l'entité.
        var debuffSnapshot = new List<(DebuffType Type, DebuffInstance Instance)>(AllDebuffInstances());
        foreach (var (type, instance) in debuffSnapshot) ExpireDebuffInstance(type, instance);

        var buffSnapshot = new List<(BuffType Type, BuffInstance Instance)>(AllBuffInstances());
        foreach (var (type, instance) in buffSnapshot) ExpireBuffInstance(type, instance);
    }
```

Find (`AbsorbWithShield`):

```csharp
    public float AbsorbWithShield(float incomingDamage)
    {
        if (_activeBuffs.TryGetValue(BuffType.Shield, out var shield))
        {
            incomingDamage = shield.AbsorbDamage(incomingDamage);
            if (shield.remainingShield <= 0f) ExpireBuff(BuffType.Shield);
        }

        return incomingDamage;
    }
```

Replace with:

```csharp
    public float AbsorbWithShield(float incomingDamage)
    {
        if (_activeBuffs.TryGetValue(BuffType.Shield, out var list) && list.Count > 0)
        {
            var shield = list[0];
            incomingDamage = shield.AbsorbDamage(incomingDamage);
            if (shield.remainingShield <= 0f) ExpireBuff(BuffType.Shield);
        }

        return incomingDamage;
    }
```

`OnTakeDamage` (`if (isSleeping) ExpireDebuff(DebuffType.Sleep);` / `if (isStealthed) ExpireBuff(BuffType.Stealth);`) needs **no textual change** — both calls already go through the type-only convenience wrappers established in Steps 9-10, which keep their exact signatures. Confirm it compiles unchanged once earlier steps are done.

- [ ] **Step 12: Update `HasDebuff`/`HasBuff` and `GetActiveEffectsForUI`**

Find:

```csharp
    public bool HasDebuff(DebuffType type) => _activeDebuffs.ContainsKey(type);
    public bool HasBuff(BuffType type)     => _activeBuffs.ContainsKey(type);
```

Replace with:

```csharp
    public bool HasDebuff(DebuffType type) => _activeDebuffs.TryGetValue(type, out var l) && l.Count > 0;
    public bool HasBuff(BuffType type)     => _activeBuffs.TryGetValue(type, out var l) && l.Count > 0;
```

Find:

```csharp
    public List<StatusEffectUIEntry> GetActiveEffectsForUI()
    {
        var list = new List<StatusEffectUIEntry>();

        foreach (var kvp in _activeDebuffs)
            list.Add(new StatusEffectUIEntry
            {
                key           = kvp.Key.ToString(),
                icon          = kvp.Value.data.icon,
                remainingTime = kvp.Value.remainingTime,
                totalDuration = kvp.Value.data.duration,
                isDebuff      = true
            });

        foreach (var kvp in _activeBuffs)
            list.Add(new StatusEffectUIEntry
            {
                key           = kvp.Key.ToString(),
                icon          = kvp.Value.data.icon,
                remainingTime = kvp.Value.remainingTime,
```

Replace with (note: read the few lines right after this excerpt in the live file — the buff entry's closing fields — and keep them exactly as they are; only the two loop headers and the `kvp.Key`/`kvp.Value` → `type`/`instance` renames inside each object initializer change):

```csharp
    public List<StatusEffectUIEntry> GetActiveEffectsForUI()
    {
        var list = new List<StatusEffectUIEntry>();

        // 1 entrée UI par INSTANCE, pas par type — 2 Stats actifs = 2 icônes distinctes,
        // chacune avec son propre temps restant.
        foreach (var (type, instance) in AllDebuffInstances())
            list.Add(new StatusEffectUIEntry
            {
                key           = type.ToString(),
                icon          = instance.data.icon,
                remainingTime = instance.remainingTime,
                totalDuration = instance.data.duration,
                isDebuff      = true
            });

        foreach (var (type, instance) in AllBuffInstances())
            list.Add(new StatusEffectUIEntry
            {
                key           = type.ToString(),
                icon          = instance.data.icon,
                remainingTime = instance.remainingTime,
```

- [ ] **Step 13: Search the file for any remaining reference to the old shape and fix it**

Run a search across `Entities/StatusEffectSystem.cs` for `_activeDebuffs` and `_activeBuffs` and confirm every remaining hit is one of: a declaration (Step 1), an `AllDebuffInstances`/`AllBuffInstances` body (Step 1), a `TryGetValue(..., out var list)` / `.Remove(type)` / `.ContainsKey(type)` pattern already updated in Steps 2-12, or a comment. There should be no compiler error referencing a `DebuffInstance`/`BuffInstance` value where a `List<DebuffInstance>`/`List<BuffInstance>` is now returned (e.g. calling `.remainingTime` directly on a dictionary lookup result without indexing `[0]` first).

- [ ] **Step 14: Compile**

Open the project in Unity (or use its command-line batch mode if available) and confirm zero compiler errors in `Entities/StatusEffectSystem.cs`. Fix anything Step 13 missed.

- [ ] **Step 15: Commit**

```bash
git add Entities/StatusEffectSystem.cs
git commit -m "$(cat <<'EOF'
fix: allow Stats/Dot debuffs and buffs to stack instead of colliding

_activeDebuffs/_activeBuffs were Dictionary<Type, Instance> — one slot
per enum value. DebuffType.Stats/Dot and BuffType.Stats are generic
buckets reused by many different concrete effects (e.g. a "-Precision"
debuff and a "-Resistance" debuff are both DebuffType.Stats), so
applying a 2nd one silently evicted the 1st instead of the two
coexisting. Same for two DoTs of different elements/sources, which
should tick independently rather than overwrite each other.

Switches storage to Dictionary<Type, List<Instance>>. Named CC/utility
types (Stun, Fear, Slow...) keep their exact current single-instance
"most recent replaces" behavior — only Stats and Dot can hold more
than one simultaneous instance now.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Manual verification (Play Mode)

**Files:** none — Play Mode testing only, in the Unity Editor.

**Interfaces:**
- Consumes: everything produced by Task 1.

- [ ] **Step 1: Two different Stats debuffs coexist**

In Play Mode, apply a "-Précision" debuff (DebuffType.Stats) to a mob, then a "-Résistance" debuff (DebuffType.Stats, different asset) to the same mob. Open the Inspector on its `StatusEffectSystem` component and check `_debugDebuffs` — expect **two** lines, one per effect. Confirm both stats are actually reduced (check the mob's effective precision and resistance, e.g. via whatever debug/inspector view already shows those).

- [ ] **Step 2: Two different DoTs stack their damage**

Apply a Fire DoT and a Nature DoT (different DebuffData assets, both DebuffType.Dot) to the same target. Confirm both tick independently — total damage per second should be the sum of both, not just one.

- [ ] **Step 3: Recasting the exact same Stats/Dot asset refreshes, doesn't duplicate**

While the "-Précision" debuff from Step 1 is still active, apply the exact same DebuffData asset again. Confirm `_debugDebuffs` still shows exactly one line for it (not two) and its remaining time resets to full duration.

- [ ] **Step 4: Stun_Lv1 → Stun_Lv2 still replaces, never stacks**

Apply a lower-duration Stun debuff, then a higher-duration Stun debuff (same DebuffType.Stun, different DebuffData assets) before the first expires. Confirm `_debugDebuffs` shows exactly **one** Stun line, with the *second* debuff's duration — not two simultaneous Stun entries. (This re-confirms the same-day `existing.data = debuff` fix still works correctly through the new list-based apply logic.)

- [ ] **Step 5: Purified/Dispel rolls independently per instance**

With 2+ Stats debuffs active (from Step 1), trigger a Purified-type effect with a mid-range chance (e.g. 50%) several times across repeated tests. Confirm it's possible to see one removed while the other survives (not strictly all-or-nothing every time) — matches the "jet indépendant par effet actif" design intent.

- [ ] **Step 6: Death clears every stacked instance**

With 2+ Stats/Dot instances active on an entity, kill it. Confirm `_debugDebuffs`/`_debugBuffs` are both empty afterward (no leftover stat modifier survives past death) — checks `ClearAllEffects()`'s new snapshot-and-expire-everything behavior.

- [ ] **Step 7: Named single-instance types unaffected**

Spot-check a few non-stackable types still behave exactly as before: apply Slow twice (2nd replaces 1st, one line in `_debugDebuffs`), a Shield buff absorbing damage then expiring at 0 remaining, and a Fear debuff where `PlayerController`'s flee-direction logic still resolves a source via `GetDebuffSource(DebuffType.Fear)`.
