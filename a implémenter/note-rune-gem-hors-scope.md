# RuneData / GemData — hors scope de la refonte ItemData/multilangue/ratio-roll

> **But de ce fichier** : consigner explicitement une exclusion pour qu'elle ne soit pas oubliée entre deux sessions. Court, volontairement.

Lors de l'exploration ayant mené à `note-refonte-itemdata-classe-de-base.md`, `note-refonte-roll-ratio-phase2.md` et `note-refonte-settings-langue-phase3.md`, une désynchronisation majeure a été découverte entre le code actuel et le GDD v3.6 concernant les runes :

- **Code actuel** (`Data/Inventory/RuneData.cs`) : `RuneStatEntry.valueMin/valueMax`, roll effectué au **drop** (`CreateDropInstance()`), rareté r0→r+7, table de lignes lv50/65/80/95 × r0/r2/r4/r6/r7 (4×5).
- **GDD v3.6 §5.11** : système bien plus complexe — tiers de stats **S/A/B/C/D/E** avec fourchettes relatives par tier, roll effectué à **l'identification** (pas au drop — le SO ne contient que les paramètres de génération), rareté r1→r7, 5 tranches de niveau (T1-T5, lv50-100 par pas de 10), tables de probabilité de lignes S par tranche×rareté, plafond S+A, jamais deux fois le même `statType` sur une même rune, pools de stats séparés Weapon/Armor, jusqu'à 10 lignes (T5 r7).

C'est une refonte complète du système de runes, pas un simple ratio-roll ou une migration de champ `string`→`LocalizedText`. Idem pour `GemData` dans une moindre mesure (dépend en partie du même pool de stats et du même `StatType`).

**Décision de l'utilisateur** : ne pas toucher à RuneData/GemData dans les phases 1/2/3 décrites dans ce dossier. Le code actuel (brouillon fonctionnel, même s'il ne correspond plus au GDD) reste tel quel — **aucun héritage de `ItemData`, aucune conversion en `LocalizedText`, aucun changement de mécanique de roll** tant qu'une refonte dédiée n'a pas été planifiée séparément, alignée sur le GDD §5.11 (et sa section Gemmes, §5.12, à relire aussi à ce moment-là).

**Quand cette refonte dédiée sera planifiée**, elle devra probablement inclure, en plus du système de tiers lui-même :
- L'héritage `ItemData`/`LocalizedText` (rattrapage — RuneData/GemData en auront besoin comme tous les autres types, juste pas maintenant).
- La compatibilité avec `WeaponInstance.TryInsertRune()`/`ArmorInstance.TryInsertRune()` (déjà existants, référencent `RuneInstance.RuneName`, `.Label`, `.RarityLabel`, `.CanInsertInto()` — signatures à préserver ou migrer en connaissance de cause).
