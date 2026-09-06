using UnityEngine;
using System.Collections.Generic;

// =============================================================
// ENTITY.CS — Classe de base pour toutes les entités vivantes
// Path : Assets/Scripts/Core/Entity.cs
// AetherTree GDD v3.5 — §3.1 (Entity)
//
// Héritiers directs : Player, Mob, PNJ, Pet
//
// Principe :
//   Tous les champs de stats runtime vivent ici.
//   CombatSystem lit TOUJOURS sur Entity, quelle que soit l'entité concrète.
//   Chaque sous-classe est responsable d'écrire sur ces champs à l'init
//   (et à chaque recalcul pour le joueur).
//
// Sources d'écriture par sous-classe :
//   Player   → base unarmed/0 défense au départ,
//              puis CharacterStats.RecalculateStats() réécrit à chaque équipement
//   Mob/Boss → MobStatCalculator.Calculate() appelé dans ApplyData()
//   PNJ      → NpcData SO écrit directement dans Awake()
//   Pet      → dérivé du mob capturé au moment de la capture
//
// Régénération passive (GDD v3.5 §3.1) :
//   Tick actif sur toutes les entités — sans effet si regenHP/regenMana == 0f.
//   Mob, PNJ et Pet laissent ces champs à 0f (pas de regen hors combat).
//   Le joueur les reçoit depuis CharacterData via RecalculateStats().
//
// Résistances élémentaires (GDD v3.5 §3.1) :
//   Dictionary<ElementType, float> — pas de plafond.
//   Valeur positive = résistance, valeur négative = vulnérabilité.
//   Les debuffs de réduction peuvent passer en négatif (dégâts amplifiés).
//
// KnockBack (GDD v3.5 §3.1.1.1) :
//   Effet ponctuel — pas de flag runtime sur Entity.
//   Appliqué directement par CombatSystem/SkillSystem via ApplyKnockBack().
// =============================================================

[RequireComponent(typeof(StatusEffectSystem))]
public abstract class Entity : MonoBehaviour
{
    // =========================================================
    // IDENTITÉ
    // =========================================================

    [Header("Identité")]
    public string entityName = "Entity";

    /// <summary>
    /// Type de l'entité — permet à CombatSystem et aux conditions de déblocage
    /// d'identifier l'entité sans casting. GDD v3.5 §3.1.
    /// Assigné par chaque sous-classe dans Awake() avant base.Awake().
    /// </summary>
    public EntityType entityType { get; protected set; } = EntityType.Mob;

    /// <summary>
    /// Catégorie d'arme — détermine quelle défense du défenseur s'applique
    /// lors du calcul des dégâts dans CombatSystem. GDD v3.5 §3.1.
    /// Joueur : choix à la création (immuable).
    /// Mob    : défini sur MobData SO (attackType).
    /// PNJ    : défini sur NpcData SO.
    /// </summary>
    public WeaponCategory weaponCategory { get; protected set; } = WeaponCategory.Melee;

    // =========================================================
    // STATS VITALES
    // =========================================================

    protected float maxHP   = 100f;
    protected float maxMana = 50f;

    /// <summary>
    /// Régénération HP par seconde.
    /// Joueur : depuis CharacterData + équipement + StatPoints via RecalculateStats().
    /// Mob / PNJ civil / Pet : 0f — pas de regen hors combat.
    /// Garde : défini sur NpcData SO.
    /// </summary>
    protected float regenHP = 0f;

    /// <summary>
    /// Régénération Mana par seconde.
    /// Joueur : depuis CharacterData + équipement + StatPoints via RecalculateStats().
    /// Mob / PNJ / Pet : 0f par défaut.
    /// </summary>
    protected float regenMana = 0f;

    protected float currentHP;
    protected float currentMana;

    // =========================================================
    // STATS DE MOUVEMENT
    // =========================================================

    /// <summary>
    /// Vitesse de déplacement de base.
    /// Joueur   : CharacterData.baseMoveSpeed + équipement/monture via RecalculateStats().
    /// Mob      : MobData.moveSpeed — assigné dans ApplyData().
    /// PNJ      : NpcData SO.
    /// </summary>
    protected float moveSpeed = 5f;

    // =========================================================
    // STATS D'ATTAQUE
    // =========================================================

    /// <summary>
    /// Dégâts minimum de l'attaque de base.
    /// Joueur   : coup de poing si désarmé (base + level), sinon WeaponData via RecalculateStats().
    /// Mob/PNJ  : calculé par MobStatCalculator / NpcData SO.
    /// </summary>
    protected float attackDamageMin = 1f;

    /// <summary>Dégâts maximum de l'attaque de base. Même sources que attackDamageMin.</summary>
    protected float attackDamageMax = 2f;


    /// <summary>
    /// Précision — intervient dans le calcul de taux de toucher.
    /// Miss% = dodge^6 / (dodge^6 + precision^6) × 100.
    /// Magic type joueur/mob/pnj : fixée à 999 dans CombatSystem. jamais de miss, mais pas de crit non plus.
    /// </summary>
    protected float precision = 15f;

    /// <summary>Chance de critique [0..1]. Forcé à 0f si WeaponCategory.Magic (joueur).</summary>
    protected float critChance = 0.00f;

    /// <summary>Multiplicateur dégâts critique. Base 1f.</summary>
    protected float critDamage = 1f;

    /// <summary>Bonus de gain XP/Aeris [0f..], additif — voir XPBonusPercent/GoldBonusPercent.
    /// Base toujours 0f (pas de source d'équipement, uniquement talismans à ce jour).</summary>
    protected float xpBonusPercent   = 0f;
    protected float goldBonusPercent = 0f;

    /// <summary>Bonus de gain XP Esprit [0f..], additif — même schéma que XPBonusPercent, voir
    /// SpiritXpBonusPercent. Base toujours 0f, uniquement via talisman Spirit_XP.</summary>
    protected float spiritXpBonusPercent = 0f;

    /// <summary>Dégâts finaux — accumulateurs bruts (pas de "stat de base" à multiplier),
    /// alimentés par StatModifierType.FinalDamageBonus/FinalDamageReduction (buffs) et
    /// StatType.FinalDamageBonus/FinalDamageReduction (équipement). Appliqués par
    /// CombatSystem.CalculateDamage/CalculateMobDamage, tout à la fin du pipeline de dégâts,
    /// PAS via la formule (Base+Flat)×(1+%) du Plan A. Toujours 0f par défaut — pas de source
    /// permanente aujourd'hui, uniquement buffs/équipement additifs.</summary>
    protected float finalDamageBonusFlat        = 0f;
    protected float finalDamageBonusPercent     = 0f;
    protected float finalDamageReductionFlat    = 0f;
    protected float finalDamageReductionPercent = 0f;

    // =========================================================
    // STATS DE DÉFENSE
    // =========================================================

    /// <summary>
    /// Défense contre les attaques de mêlée.
    /// Joueur   : 0f de base, tout vient de l'équipement via RecalculateStats().
    /// Mob      : base + value × level × meleeDefMult via MobStatCalculator.
    /// PNJ      : NpcData SO (0f pour les civils).
    /// </summary>
    protected float meleeDefense = 0f;

    /// <summary>Défense contre les attaques à distance. Mêmes sources que meleeDefense.</summary>
    protected float rangedDefense = 0f;

    /// <summary>Défense contre les attaques magiques. Mêmes sources que meleeDefense.</summary>
    protected float magicDefense = 0f;

    /// <summary>
    /// Esquive — intervient dans le calcul de taux de toucher.
    /// Joueur : 0f de base, équipement + StatPoints via RecalculateStats().
    /// Mob    : valeur SO.
    /// </summary>
    protected float dodge = 0f;

    /// <summary>
    /// Réduction des dégâts reçus lors d'un coup critique [0..1].
    /// Joueur uniquement — source : StatPoints Défense paliers.
    /// Mob / PNJ : 0f par défaut.
    /// Lue par CombatSystem après confirmation du crit entrant.
    /// </summary>
    protected float critDamageReduction = 0f;

    // =========================================================
    // RÉSISTANCES ÉLÉMENTAIRES
    // =========================================================

    /// <summary>
    /// Résistances élémentaires par élément — pas de plafond.
    /// Valeur positive = résistance, valeur négative = vulnérabilité (dégâts amplifiés).
    /// Initialisé à 0f pour chaque ElementType dans Awake().
    /// Joueur   : agrégé depuis l'équipement + StatPoints via RecalculateStats().
    /// Mob      : valeurs fixes depuis MobData SO, écrites dans ApplyData().
    /// PNJ      : 0f par défaut (NpcData SO peut surcharger).
    /// Les debuffs de réduction élémentaire écrivent directement ici via
    /// StatusEffectSystem pour permettre les valeurs négatives (vulnérabilité).
    /// </summary>
    protected Dictionary<ElementType, float> elementalResistances
        = new Dictionary<ElementType, float>();

    /// <summary>
    /// Points élémentaires par élément.
    /// Agrège : esprits + équipement (casque/bijoux/runes) + permanents + StatPoints.
    /// Joueur   : calculé et poussé par CharacterStats.RecalculateStats().
    /// Mob/PNJ  : 0f par défaut (pas de système élémentaire actif).
    /// Lus par ElementalSystem et CombatSystem pour le calcul des dégâts élémentaires.
    /// </summary>
    protected Dictionary<ElementType, float> elementalPoints
        = new Dictionary<ElementType, float>();

    // =========================================================
    // ÉTAT
    // =========================================================

    public bool isDead { get; protected set; } = false;

    // =========================================================
    // SYSTÈMES
    // =========================================================

    /// <summary>Présent sur toutes les entités via [RequireComponent].</summary>
    public StatusEffectSystem statusEffects { get; private set; }

    // =========================================================
    // TIMER RÉGÉNÉRATION
    // =========================================================

    private float _regenTimer = 0f;
    private const float REGEN_TICK = 1f;

    // =========================================================
    // PROPRIÉTÉS PUBLIQUES EN LECTURE SEULE
    // Pour l'UI et les systèmes qui lisent sans modifier.
    // =========================================================

    public float MaxHP           => maxHP;
    public float CurrentHP       => currentHP;
    public float MaxMana         => maxMana;
    public float CurrentMana     => currentMana;
    public float RegenHP         => regenHP;
    public float RegenMana       => regenMana;
    public float MoveSpeed       => moveSpeed;
    public float AttackDamageMin => attackDamageMin;
    public float AttackDamageMax => attackDamageMax;
    public float Precision       => precision;
    public float CritChance      => critChance;
    public float CritMultiplier  => critDamage;
    public float MeleeDefense    => meleeDefense;
    public float RangedDefense   => rangedDefense;
    public float MagicDefense    => magicDefense;
    public float Dodge               => dodge;
    public float CritDamageReduction => critDamageReduction;

    /// <summary>Bonus de gain XP/Aeris [0f = aucun, 0.20 = +20%] — alimenté uniquement par
    /// StatModifierType.XPBonus/GoldBonus (talismans). Aucune source d'équipement aujourd'hui,
    /// base toujours 0f.</summary>
    public float XPBonusPercent   => xpBonusPercent;
    public float GoldBonusPercent => goldBonusPercent;

    /// <summary>Bonus de gain XP Esprit [0f = aucun, 0.25 = +25%] — alimenté uniquement par
    /// StatModifierType.SpiritXpBonus (talisman Spirit_XP). Base toujours 0f.</summary>
    public float SpiritXpBonusPercent => spiritXpBonusPercent;

    public float FinalDamageBonusFlat        => finalDamageBonusFlat;
    public float FinalDamageBonusPercent     => finalDamageBonusPercent;
    public float FinalDamageReductionFlat    => finalDamageReductionFlat;
    public float FinalDamageReductionPercent => finalDamageReductionPercent;

    /// <summary>HP courant exprimé en [0..1]. Utile pour les barres et conditions.</summary>
    public float HPPercent   => maxHP   > 0f ? currentHP   / maxHP   : 0f;

    /// <summary>Mana courant exprimé en [0..1].</summary>
    public float ManaPercent => maxMana > 0f ? currentMana / maxMana : 0f;

    /// <summary>
    /// Résistance élémentaire pour un élément donné.
    /// Peut être négative (vulnérabilité). Aucun plafond. GDD v3.5 §3.1.
    /// </summary>
    public float GetElementalResistance(ElementType element)
        => elementalResistances.TryGetValue(element, out float v) ? v : 0f;

    /// <summary>
    /// Points élémentaires pour un élément donné.
    /// Lus par ElementalSystem et CombatSystem.
    /// </summary>
    public float GetElementalPoints(ElementType element)
        => elementalPoints.TryGetValue(element, out float v) ? v : 0f;

    // =========================================================
    // INITIALISATION
    // =========================================================

    protected virtual void Awake()
    {
        // Initialise toutes les résistances élémentaires à 0f
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
        {
            elementalResistances[e] = 0f;
            elementalPoints[e]      = 0f;
        }

        currentHP    = maxHP;
        currentMana  = maxMana;
        statusEffects = GetComponent<StatusEffectSystem>();
    }

    // =========================================================
    // UPDATE — régénération passive
    // =========================================================

    protected virtual void Update()
    {
        if (isDead) return;

        _regenTimer += Time.deltaTime;
        if (_regenTimer >= REGEN_TICK)
        {
            _regenTimer = 0f;
            ApplyRegen();
        }
    }

    // Sans effet si regenHP == 0f && regenMana == 0f (Mob/PNJ/Pet).
    private void ApplyRegen()
    {
        if (regenHP   > 0f) Heal(regenHP);
        if (regenMana > 0f) RecoverMana(regenMana);
    }

    // =========================================================
    // DÉFENSES — accesseurs pour CombatSystem
    // Les sous-classes n'ont plus besoin d'override — CombatSystem
    // lit les propriétés publiques directement.
    // =========================================================

    public virtual float GetMeleeDefense()
    {
        float base_ = meleeDefense;
        if (statusEffects != null)
        {
            base_ += statusEffects.GetBuffDefenseBonus();
            base_ *= (1f - statusEffects.GetArmorBreakReduction()); // ArmorBreak obsolète, compat assets existants
        }
        return Mathf.Max(0f, base_);
    }

    public virtual float GetRangedDefense()
    {
        float base_ = rangedDefense;
        if (statusEffects != null)
        {
            base_ += statusEffects.GetBuffDefenseBonus();
            base_ *= (1f - statusEffects.GetArmorBreakReduction()); // ArmorBreak obsolète, compat assets existants
        }
        return Mathf.Max(0f, base_);
    }

    public virtual float GetMagicDefense()
    {
        float base_ = magicDefense;
        if (statusEffects != null)
            base_ += statusEffects.GetBuffDefenseBonus();
        return Mathf.Max(0f, base_);
    }

    // =========================================================
    // STATS EFFECTIVES — intègrent les bonus de buffs actifs
    // CombatSystem doit lire ces getters plutôt que les propriétés brutes
    // pour que AttackUp, CritChanceUp et DodgeUp soient pris en compte.
    // GDD v3.5 §3.1.1.2 — buffAttackBonus / buffCritChanceBonus /
    //                       buffCritDamageBonus / buffDodgeBonus
    // =========================================================

    /// <summary>
    /// Dégâts min effectifs = base + buffAttackBonus.
    /// À utiliser dans CombatSystem à la place de AttackDamageMin.
    /// </summary>
    public float GetEffectiveAttackMin()
    {
        float v = attackDamageMin;
        if (statusEffects != null) v += statusEffects.GetBuffAttackBonus();
        return Mathf.Max(0f, v);
    }

    /// <summary>
    /// Dégâts max effectifs = base + buffAttackBonus.
    /// À utiliser dans CombatSystem à la place de AttackDamageMax.
    /// </summary>
    public float GetEffectiveAttackMax()
    {
        float v = attackDamageMax;
        if (statusEffects != null) v += statusEffects.GetBuffAttackBonus();
        return Mathf.Max(0f, v);
    }

    /// <summary>
    /// Chance de critique effective = base + buffCritChanceBonus.
    /// À utiliser dans CombatSystem à la place de CritChance.
    /// </summary>
    public float GetEffectiveCritChance()
    {
        float v = critChance;
        if (statusEffects != null) v += statusEffects.GetBuffCritChanceBonus();
        return Mathf.Max(0f, v);
    }

    /// <summary>
    /// Multiplicateur critique effectif = base + buffCritDamageBonus.
    /// À utiliser dans CombatSystem à la place de CritMultiplier.
    /// </summary>
    public float GetEffectiveCritMultiplier()
    {
        float v = critDamage;
        if (statusEffects != null) v += statusEffects.GetBuffCritDamageBonus();
        return Mathf.Max(1f, v);
    }

    /// <summary>
    /// Esquive effective = base + buffDodgeBonus.
    /// À utiliser dans CombatSystem à la place de Dodge.
    /// </summary>
    public float GetEffectiveDodge()
    {
        float v = dodge;
        if (statusEffects != null) v += statusEffects.GetBuffDodgeBonus();
        return Mathf.Max(0f, v);
    }

    public float GetEffectivePrecision()
    {
        float v = precision;
        if (statusEffects != null) v += statusEffects.GetBuffPrecisionBonus();
        return Mathf.Max(0f, v);
    }

    // =========================================================
    // DÉGÂTS
    // =========================================================

    /// <summary>
    /// Inflige des dégâts bruts (déjà calculés par CombatSystem).
    /// Override dans Player et Mob pour ajouter des effets spécifiques.
    /// </summary>
    public virtual void TakeDamage(float amount,
                                   ElementType sourceElement = ElementType.Neutral,
                                   Entity source = null)
    {
        if (isDead || amount <= 0f) return;

        // Invincible — annule les dégâts | Sleep — réveil au premier dégât
        if (statusEffects != null && statusEffects.OnTakeDamage()) return;

        // Shield — absorbe les dégâts en priorité
        if (statusEffects != null)
            amount = statusEffects.AbsorbWithShield(amount);

        if (amount <= 0f) return;

        currentHP = Mathf.Max(0f, currentHP - amount);

        if (currentHP <= 0f)
        {
            // ── OnFatalHit — intercept AVANT Die() ───────────────
            // PassiveSkillSystem.CanSurviveFatalHit() évalue les passives OnFatalHit,
            // applique leurs effets, et retourne true si le joueur survit.
            Player playerSelf = this as Player;
            if (playerSelf != null &&
                PassiveSkillSystem.Instance != null &&
                PassiveSkillSystem.Instance.CanSurviveFatalHit())
            {
                currentHP = 1f;
            }
            else
            {
                Die();
            }
        }

        // Réactions On-Hit reçues (Thorns/Reflect/HealOnHit/CounterDebuff/CounterBuff) —
        // centralisé ici (au lieu de Player uniquement) pour que Mob/PNJ en bénéficient aussi
        // via leur propre override de GetOnHitReceivedEffects(). `amount` ici reflète déjà le
        // Shield (contrairement à l'ancien Player.ApplyOnHitEffects, qui recevait la valeur
        // AVANT absorption de bouclier — précision, pas un changement de comportement visible
        // puisque le montant absorbé n'était de toute façon jamais la cible d'un proc réel).
        if (source != null)
            ApplyOnHitReceivedEffects(amount, source);
    }

    /// <summary>Effets On-Hit infligés actifs sur cette entité (équipement+permanents pour
    /// Player, MobData/PNJData pour Mob/PNJ). Null par défaut — override dans les sous-classes
    /// concernées. Consommé par SkillSystem (réactions) et CombatSystem (DamageAmpOnHit).</summary>
    public virtual List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => null;

    /// <summary>Effets On-Hit reçus actifs sur cette entité — voir GetOnHitDealtEffects().
    /// Consommé par ApplyOnHitReceivedEffects (réactions) et CombatSystem
    /// (DamageReductionOnHit).</summary>
    public virtual List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => null;

    private bool _isProcessingOnHitReceived = false;

    /// <summary>Applique les effets On-Hit REÇUS (Thorns/ReflectPercent/HealOnHit/
    /// CounterDebuff/CounterBuff) de cette entité quand elle reçoit un coup. DamageReductionOnHit
    /// n'apparaît pas ici — c'est un modificateur, déjà consommé dans CombatSystem au moment
    /// du calcul, pas une réaction post-coup.</summary>
    private void ApplyOnHitReceivedEffects(float damageTaken, Entity attacker)
    {
        // Garde de ré-entrance — Thorns/Reflect mutuels (joueur ET cible ont un effet reçu)
        // provoqueraient sinon une récursion infinie : A riposte sur B, B riposte sur A, etc.
        // jusqu'au StackOverflow (crash non-rattrapable). Le 2e rebond sur la MÊME entité,
        // pendant que sa 1ère résolution est encore sur la pile, est ignoré ici.
        if (_isProcessingOnHitReceived) return;

        var effects = GetOnHitReceivedEffects();
        if (effects == null) return;

        _isProcessingOnHitReceived = true;
        try
        {
            foreach (var entry in effects)
            {
                if (entry?.effect == null || !entry.Roll()) continue;
                switch (entry.effect.effectType)
                {
                    case OnHitReceivedEffectType.Thorns:
                        DealOnHitCounterDamage(entry.effect.thornsDamage, entry.effect, attacker);
                        break;
                    case OnHitReceivedEffectType.ReflectPercent:
                        DealOnHitCounterDamage(damageTaken * entry.effect.reflectPercent, entry.effect, attacker);
                        break;
                    case OnHitReceivedEffectType.HealOnHit:
                        Heal(entry.effect.GetHealAmount(MaxHP));
                        break;
                    case OnHitReceivedEffectType.CounterDebuff:
                        if (entry.effect.counterDebuff != null && attacker.statusEffects != null)
                            attacker.statusEffects.TryApplyDebuff(entry.effect.counterDebuff, this);
                        break;
                    case OnHitReceivedEffectType.CounterBuff:
                        if (entry.effect.counterBuff != null && statusEffects != null)
                            statusEffects.ApplyBuff(entry.effect.counterBuff, this);
                        break;
                }
            }
        }
        finally
        {
            _isProcessingOnHitReceived = false;
        }
    }

    /// <summary>Applique les dégâts d'un Thorns/ReflectPercent à l'attaquant. `pierceDefense`
    /// (Inspector: "Dégâts renvoyés bruts") true → montant tel quel. False → réduit comme un
    /// dégât normal : formule physique quadratique (défense mêlée) si reflectElement = Neutral,
    /// résistance élémentaire linéaire sinon — même split physique/élémentaire indépendant que
    /// CombatSystem, pas de mélange des deux canaux. Déplacé depuis Player.cs (session
    /// précédente) pour être partagé par toutes les entités.</summary>
    private void DealOnHitCounterDamage(float rawAmount, OnHitReceivedEffectData effect, Entity attacker)
    {
        if (rawAmount <= 0f || attacker == null) return;

        float amount = rawAmount;
        if (!effect.pierceDefense)
        {
            if (effect.reflectElement == ElementType.Neutral)
            {
                float def = attacker.GetMeleeDefense();
                amount = (rawAmount * rawAmount) / (rawAmount + def * 1.5f);
            }
            else
            {
                float resist = attacker.GetElementalResistance(effect.reflectElement);
                amount = rawAmount * (1f - Mathf.Clamp01(resist));
            }
        }

        attacker.TakeDamage(amount, effect.reflectElement, this);
    }

    // =========================================================
    // KNOCKBACK (GDD v3.5 §3.1.1.1)
    // Repoussement (pas un étourdissement en soi) + mini-stun ponctuel type Shocked 0.5s
    // le temps du recul (StatusEffectSystem.isKnockedBack) — pas le pipeline DebuffData.
    // Appelé directement par CombatSystem / SkillSystem sur la cible. Implémentation
    // générique ici (Player/Mob/PNJ) via DisplacementUtils — les sous-classes n'ont besoin
    // d'override que pour un garde-fou (ex: Player refuse si déjà mort/stun/root).
    // =========================================================

    /// <summary>
    /// Applique un recul physique sur l'entité.
    /// direction : vecteur normalisé depuis la source vers la cible.
    /// force     : intensité du recul (unités Unity).
    /// Interrompt le déplacement en cours.
    /// </summary>
    public virtual void ApplyKnockBack(Vector3 direction, float force)
    {
        // Repoussement — réutilise DisplacementUtils (même helper que Push/Pull des skills,
        // WarpToNavMesh gère déjà NavMeshAgent.Warp si présent). Générique Player/Mob/PNJ,
        // pas besoin d'override par sous-classe pour le déplacement lui-même.
        // Player.ApplyKnockBack() a son propre garde isDead (+ stun/root) ; ce garde ici
        // couvre Mob/PNJ/Pet qui n'overrident jamais cette méthode.
        if (isDead || force <= 0f) return;
        Vector3 flatDir = direction;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;

        Vector3 dest = transform.position + flatDir.normalized * force;
        DisplacementUtils.WarpToNavMesh(this, dest);

        // Knockback = mini-stun type Shocked (0.5s) le temps du recul, pas un étourdissement
        // prolongé — voir StatusEffectSystem.isKnockedBack.
        statusEffects?.ApplyKnockbackStun(0.5f);
    }

    // =========================================================
    // SOIN
    // =========================================================

    public virtual void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;

        // Poison — réduit les soins reçus. GDD v3.5 §3.1.1.1.
        // La réduction s'applique à toutes les entités (pas seulement le joueur).
        if (statusEffects != null)
        {
            float reduction = statusEffects.GetPoisonHealReduction();
            if (reduction > 0f)
                amount *= (1f - reduction);
        }

        if (amount <= 0f) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    // =========================================================
    // MANA
    // =========================================================

    public virtual void SpendMana(float amount)
    {
        currentMana = Mathf.Max(0f, currentMana - amount);
    }

    /// <summary>Coût en HP d'un skill (SkillData.hpCost) — PAS des dégâts : pas de check
    /// Invincible/Sleep/Poison, pas de floating text, pas de mort déclenchée. L'appelant
    /// (SkillBar.TryUseSlot) garantit déjà CurrentHP > amount avant d'appeler ceci (bloque
    /// le cast sinon, même convention que SpendMana), donc ce clamp à 0f n'est qu'un
    /// filet de sécurité, jamais censé s'activer en pratique.</summary>
    public virtual void SpendHP(float amount)
    {
        currentHP = Mathf.Max(0f, currentHP - amount);
    }

    public virtual void RecoverMana(float amount)
    {
        if (isDead) return;
        currentMana = Mathf.Min(maxMana, currentMana + amount);
    }

    public bool HasMana(float amount) => currentMana >= amount;

    // =========================================================
    // MORT
    // =========================================================

    protected virtual void Die()
    {
        if (isDead) return;
        isDead    = true;
        currentHP = 0f;
        // Perd tous les buffs/debuffs actifs à la mort — décision explicite Florian. Un Revive
        // en attente doit être consommé par l'appelant (voir Player.Die()) AVANT ce Die() de
        // base, sinon ClearAllEffects() l'efface avant qu'il ait pu servir.
        statusEffects?.ClearAllEffects();
    }

    // =========================================================
    // SNAPSHOT DES STATS DE BASE
    // Figé après init par Mob.ApplyData() / PNJ.Awake() / CharacterStats.RecalculateStats().
    // RequestRecalculate() repart de ces valeurs avant de ré-appliquer les modificateurs actifs.
    // =========================================================

    private struct BaseStats
    {
        public float attackDamageMin, attackDamageMax;
        public float meleeDefense, rangedDefense, magicDefense;
        public float dodge, precision;
        public float critChance, critDamage, critDamageReduction;
        public float maxHP, maxMana;
        public float regenHP, regenMana;
        public float moveSpeed;
        public float xpBonusPercent, goldBonusPercent, spiritXpBonusPercent;
        public float finalDamageBonusFlat, finalDamageBonusPercent;
        public float finalDamageReductionFlat, finalDamageReductionPercent;
    }
    private BaseStats _base;

    /// <summary>
    /// Fige le snapshot des stats de base actuelles.
    /// Appelé en fin d'init par chaque sous-classe (Mob.ApplyData, PNJ.Awake, Player via RecalculateStats).
    /// </summary>
    public void SnapshotBaseStats()
    {
        _base = new BaseStats
        {
            attackDamageMin = attackDamageMin,
            attackDamageMax = attackDamageMax,
            meleeDefense    = meleeDefense,
            rangedDefense   = rangedDefense,
            magicDefense    = magicDefense,
            dodge           = dodge,
            precision       = precision,
            critChance      = critChance,
            critDamage      = critDamage,
            critDamageReduction = critDamageReduction,
            maxHP           = maxHP,
            maxMana         = maxMana,
            regenHP         = regenHP,
            regenMana       = regenMana,
            moveSpeed       = moveSpeed,
            xpBonusPercent  = 0f, // pas de base — uniquement additif via talismans
            goldBonusPercent = 0f,
            spiritXpBonusPercent = 0f,
            finalDamageBonusFlat        = 0f, // pas de base — uniquement additif via buffs/équipement
            finalDamageBonusPercent     = 0f,
            finalDamageReductionFlat    = 0f,
            finalDamageReductionPercent = 0f,
        };
    }

    // =========================================================
    // RECALCUL — demande de recalcul des stats propres
    // =========================================================

    /// <summary>
    /// Remet les stats à leur base snapshottée puis demande
    /// à StatusEffectSystem de ré-appliquer les modificateurs actifs.
    /// Player override : appelle RecalculateStats() d'abord (qui snapshote lui-même),
    /// puis ReapplyActiveModifiers(). Mob / PNJ / Pet : ce chemin suffit.
    /// </summary>
    public virtual void RequestRecalculate()
    {
        // Restaure les valeurs de base
        SetAttackDamageMin(_base.attackDamageMin);
        SetAttackDamageMax(_base.attackDamageMax);
        SetMeleeDefense   (_base.meleeDefense);
        SetRangedDefense  (_base.rangedDefense);
        SetMagicDefense   (_base.magicDefense);
        SetDodge          (_base.dodge);
        SetPrecision      (_base.precision);
        SetCritChance     (_base.critChance);
        SetCritMultiplier (_base.critDamage);
        SetMaxHP          (_base.maxHP);
        SetMaxMana        (_base.maxMana);
        SetRegenHP        (_base.regenHP);
        SetRegenMana      (_base.regenMana);
        SetMoveSpeed      (_base.moveSpeed);
        SetXPBonusPercent  (_base.xpBonusPercent);
        SetGoldBonusPercent(_base.goldBonusPercent);
        SetSpiritXpBonusPercent(_base.spiritXpBonusPercent);
        SetFinalDamageBonusFlat       (_base.finalDamageBonusFlat);
        SetFinalDamageBonusPercent    (_base.finalDamageBonusPercent);
        SetFinalDamageReductionFlat   (_base.finalDamageReductionFlat);
        SetFinalDamageReductionPercent(_base.finalDamageReductionPercent);

        // Résistances élémentaires — remet à zéro puis laisse ReapplyActiveModifiers gérer
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            elementalResistances[e] = 0f;

        // Ré-applique tous les modificateurs actifs
        statusEffects?.ReapplyActiveModifiers(this);
    }

    // =========================================================
    // SETTERS — appelés par CharacterStats.RecalculateStats(),
    // MobStatCalculator.Apply() et NpcData.ApplyTo()
    // =========================================================

    public void SetMaxHP(float value)
    {
        maxHP     = Mathf.Max(1f, value);
        currentHP = Mathf.Clamp(currentHP, 0f, maxHP);
    }

    public void SetMaxMana(float value)
    {
        maxMana     = Mathf.Max(0f, value);
        currentMana = Mathf.Clamp(currentMana, 0f, maxMana);
    }

    public void SetRegenHP(float value)         => regenHP         = Mathf.Max(0f, value);
    public void SetRegenMana(float value)       => regenMana       = Mathf.Max(0f, value);
    public void SetMoveSpeed(float value)       => moveSpeed       = Mathf.Max(0f, value);
    public void SetAttackDamageMin(float value) => attackDamageMin = Mathf.Max(0f, value);
    public void SetAttackDamageMax(float value) => attackDamageMax = Mathf.Max(0f, value);
    public void SetPrecision(float value)       => precision       = Mathf.Max(0f, value);
    public void SetCritChance(float value)      => critChance      = Mathf.Max(0f, value);
    public void SetCritMultiplier(float value)  => critDamage  = Mathf.Max(1f, value);
    public void SetMeleeDefense(float value)    => meleeDefense    = Mathf.Max(0f, value);
    public void SetRangedDefense(float value)   => rangedDefense   = Mathf.Max(0f, value);
    public void SetMagicDefense(float value)    => magicDefense    = Mathf.Max(0f, value);
    public void SetDodge(float value)           => dodge           = Mathf.Max(0f, value);
    public void SetCritDamageReduction(float v) => critDamageReduction = Mathf.Clamp01(v);
    public void SetXPBonusPercent(float value)   => xpBonusPercent   = Mathf.Max(0f, value);
    public void SetGoldBonusPercent(float value) => goldBonusPercent = Mathf.Max(0f, value);
    public void SetSpiritXpBonusPercent(float value) => spiritXpBonusPercent = Mathf.Max(0f, value);

    public void SetFinalDamageBonusFlat(float value)        => finalDamageBonusFlat        = Mathf.Max(0f, value);
    public void SetFinalDamageBonusPercent(float value)     => finalDamageBonusPercent     = Mathf.Max(0f, value);
    public void SetFinalDamageReductionFlat(float value)    => finalDamageReductionFlat    = Mathf.Max(0f, value);
    public void SetFinalDamageReductionPercent(float value) => finalDamageReductionPercent = Mathf.Max(0f, value);

    /// <summary>
    /// Écrit une résistance élémentaire. Accepte les valeurs négatives (vulnérabilité).
    /// Appelé par RecalculateStats(), MobStatCalculator et StatusEffectSystem (debuffs).
    /// </summary>
    public void SetElementalResistance(ElementType element, float value)
        => elementalResistances[element] = value;

    /// <summary>
    /// Écrit les points élémentaires pour un élément donné.
    /// Appelé par CharacterStats.RecalculateStats().
    /// </summary>
    public void SetElementalPoints(ElementType element, float value)
        => elementalPoints[element] = Mathf.Max(0f, value);

    /// <summary>
    /// Modifie une résistance élémentaire de façon additive.
    /// Utilisé par StatusEffectSystem pour appliquer / retirer des debuffs de réduction.
    /// </summary>
    public void AddElementalResistance(ElementType element, float delta)
    {
        if (!elementalResistances.ContainsKey(element))
            elementalResistances[element] = 0f;
        elementalResistances[element] += delta;
    }
}
public enum EntityType { Player, Mob, PNJ, Pet }