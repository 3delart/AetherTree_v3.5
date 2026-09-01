using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// PLAYER — Entité joueur
// Path : Assets/Scripts/Core/Player.cs
// AetherTree GDD v3.5 — §3.1 (Entity), §3.2 (Joueur), §3.2.1 (StatPoints)
//                        §5.1 à §5.6 (Équipement)
//
// Hérite de Entity. Seule entité contrôlée par le joueur.
//
// Systèmes requis :
//   ActivityCounter, ElementalSystem, StatusEffectSystem, SkillSystem,
//   TargetingSystem, PlayerController, SkillBar, CombatSystem,
//   LootApproach, StatPointSystem, NavMeshAgent, CapsuleCollider
//
// Flow stats :
//   Toutes les stats passent par CharacterStats.RecalculateStats() →
//   push sur Entity via SetMaxHP(), SetMeleeDefense(), etc.
//   CombatSystem lit directement Entity — jamais sur CharacterStats.
//   Les buffs/debuffs modifient Entity directement via StatusEffectSystem,
//   indépendamment de RecalculateStats().
//
// Flow équipement :
//   EquipWeapon/Armor/Helmet/Gloves/Boots/Jewelry/Spirit(instance)
//     → stats.RecalculateStats(this) → push sur Entity
//   Les effets (OnHitEffects, StatusEffects, DebuffResistances, bonuses)
//   sont lus depuis instance.config (EquipmentConfig) — champ unique.
//
// Flow level up :
//   AddCombatXP() → OnLevelUp(n) → statPoints.GainPoints(3) → RecalculateStats()
//   ⚠ OnLevelUp() ne touche JAMAIS directement aux champs Entity
//
// Flow StatPoints :
//   UI → statPoints.InvestPoint(stat) → ApplyMilestone() → RecalculateStats()
//   Respec → statPoints.Respec() → RecalculateStats()
//
// KnockBack (GDD §3.1.1.1) :
//   Override de Entity.ApplyKnockBack() — interagit avec PlayerController.
//
// weaponCategory :
//   Immuable après la création du personnage — vit sur Entity (protected set).
//   Détermine les courbes HP/Mana (CharacterData SO) et interdit le crit
//   sur les armes Magic (GDD §5.1).
//
// armorType (SUPPRIMÉ — GDD §5.2) :
//   L'armure est entièrement libre — tout joueur peut équiper n'importe
//   quel type sans restriction liée à son arme ou weaponCategory.
// =============================================================

[RequireComponent(typeof(ActivityCounter))]
[RequireComponent(typeof(ElementalSystem))]
[RequireComponent(typeof(StatusEffectSystem))]
[RequireComponent(typeof(SkillSystem))]
[RequireComponent(typeof(TargetingSystem))]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(SkillBar))]
[RequireComponent(typeof(CombatSystem))]
[RequireComponent(typeof(StatPointSystem))]
[RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
[RequireComponent(typeof(UnityEngine.CapsuleCollider))]
public class Player : Entity
{
    // ── Template ──────────────────────────────────────────────
    [Header("Template personnage (assigner ici)")]
    public CharacterData characterData;

    // ── Moteur de calcul des stats ────────────────────────────
    public CharacterStats stats { get; private set; } = new CharacterStats();

    // ── Système de points de stats ────────────────────────────
    public StatPointSystem statPoints { get; private set; }

    // =========================================================
    // ÉQUIPEMENT — slots GDD §5.0 à §5.6
    // Arme (§5.1) : weaponCategory imposée à la création — immuable.
    // Armure (§5.2) : entièrement libre — aucune restriction par weaponCategory.
    // Casque (§5.3) : pas de rareté, pas d'upgrade, pas de rune.
    // Gants/Bottes (§5.4) : fusion S0→S6, résistances cumulées sans plafond.
    // Bijoux (§5.5) : 1 Anneau, 1 Collier, 1 Bracelet — gemmes irréversibles.
    // Esprit (§5.6) : 1 actif à la fois — source principale de pts élémentaires.
    // =========================================================

    [HideInInspector] public WeaponInstance        equippedWeaponInstance;
    [HideInInspector] public ArmorInstance         equippedArmorInstance;
    [HideInInspector] public HelmetInstance        equippedHelmetInstance;
    [HideInInspector] public GlovesInstance        equippedGlovesInstance;
    [HideInInspector] public BootsInstance         equippedBootsInstance;
    [HideInInspector] public List<JewelryInstance> equippedJewelryInstances = new List<JewelryInstance>();
    [HideInInspector] public List<SpiritInstance>  equippedSpiritInstances  = new List<SpiritInstance>();
    [HideInInspector] public CardInstance          equippedCardInstance;
    [HideInInspector] public CosmeticInstanceHead     equippedCosmeticHeadInstance;
    [HideInInspector] public CosmeticInstanceBody     equippedCosmeticBodyInstance;

    public WeaponData equippedWeapon => equippedWeaponInstance?.data;

    // ── Skills ────────────────────────────────────────────────
    [HideInInspector] public List<SkillData>          startingSkills     = new List<SkillData>();
    [HideInInspector] public List<SkillData>          unlockedSkills     = new List<SkillData>();

    /// <summary>
    /// Skills permanents — passifs définitifs (StatBonus / OnHitEffects / DebuffResistances).
    /// Débloqués via UnlockPermanent() — jamais retirés.
    /// Lus par CharacterStats.RecalculateStats() à chaque recalcul.
    /// </summary>
    [HideInInspector] public List<PermanentSkillData> unlockedPermanents = new List<PermanentSkillData>();

    /// <summary>
    /// Skills passifs conditionnels — procs sur événements de combat.
    /// Pool possédé (débloqués via UnlockPassive) — pas encore forcément actifs,
    /// voir equippedPassives ci-dessous pour ceux réellement évalués en combat.
    /// </summary>
    [HideInInspector] public List<PassiveSkillData> unlockedPassives = new List<PassiveSkillData>();

    /// <summary>
    /// Les 3 passifs RÉELLEMENT actifs (slots P1/P2/P3 de la PassifBar, GDD §7.5 —
    /// "assignés hors combat"), choisis parmi unlockedPassives. Seuls ceux-ci sont
    /// évalués par PassiveSkillSystem — un passif débloqué mais pas équipé ne proc jamais.
    /// Case à null = slot vide.
    /// </summary>
    [HideInInspector] public PassiveSkillData[] equippedPassives = new PassiveSkillData[3];

    // ── Progression ───────────────────────────────────────────
    [HideInInspector] public int level         = 1;
    [HideInInspector] public int xpCombat      = 0;
    [HideInInspector] public int xpToNextLevel = 100;

    // ── Titre actif ───────────────────────────────────────────
    [HideInInspector] public string activeTitle = "";

    // ── Réputation — GDD §3.4 ────────────────────────────────
    [HideInInspector] public int worldReputation     = 0;
    [HideInInspector] public int pvpReputation       = 0;
    [HideInInspector] public int worldReputationRank = 0;
    [HideInInspector] public int pvpReputationRank   = 0;

    private static readonly int[] WorldRepThresholds = { 0, 100, 300, 700, 1500, 3000 };
    private static readonly int[] PvPRepThresholds   = { 0, 50, 150, 300, 550, 900, 1400, 2100, 3000, 4200, 6000 };

    // ── Contexte combat ───────────────────────────────────────
    [HideInInspector] public SkillData lastSkillUsed = null;
    [HideInInspector] public bool      isInStealth   = false;
    [HideInInspector] public string    currentZoneID = "";

    [HideInInspector] public bool IsAFK = false;

    /// <summary>
    /// True dès qu'un skill est casté ou que le joueur subit des dégâts — repasse à
    /// false après combatExitDelay secondes sans nouvelle action combat (même patron
    /// que IsAFK/afkDelay). Piloté par RegisterCombatAction(), lu en polling par
    /// l'Animator (course armée vs désarmée) — pas d'event, cohérent avec le reste.
    /// </summary>
    [HideInInspector] public bool CombatActive = false;

    // ── AFK ───────────────────────────────────────────────────
    [Header("AFK")]
    [Tooltip("Délai d'inactivité en secondes avant de passer AFK.")]
    public float afkDelay = 30f;
    private float _lastActionTime = 0f;

    // ── Combat ────────────────────────────────────────────────
    [Header("Combat")]
    [Tooltip("Délai sans action combat (cast, dégâts subis) avant de sortir de CombatActive.")]
    public float combatExitDelay = 6f;
    private float _lastCombatActionTime = -999f;

    // ── Guilde ────────────────────────────────────────────────
    [HideInInspector] public HashSet<string> uniqueGroupMembersLed = new HashSet<string>();

    // ── Composants ────────────────────────────────────────────
    private ActivityCounter activityCounter;
    private ElementalSystem elementalSystem;
    private PlayerAnimatorController animatorController;
    private WeaponVisual             weaponVisual;

    // =========================================================
    // INITIALISATION
    // =========================================================

    protected override void Awake()
    {
        // Garantit l'initialisation même après désérialisation Unity
        if (equippedJewelryInstances == null) equippedJewelryInstances = new List<JewelryInstance>();
        if (equippedSpiritInstances  == null) equippedSpiritInstances  = new List<SpiritInstance>();
        if (unlockedPermanents       == null) unlockedPermanents       = new List<PermanentSkillData>();
        if (unlockedPassives         == null) unlockedPassives         = new List<PassiveSkillData>();
        if (equippedPassives         == null || equippedPassives.Length != 3) equippedPassives = new PassiveSkillData[3];

        activityCounter    = GetComponent<ActivityCounter>();
        elementalSystem    = GetComponent<ElementalSystem>();
        statPoints         = GetComponent<StatPointSystem>();
        animatorController = GetComponent<PlayerAnimatorController>();
        weaponVisual        = GetComponent<WeaponVisual>();

        // entityType assigné sur Entity (GDD §3.1)
        entityType = EntityType.Player;

        if (characterData != null)
        {
            entityName = characterData.characterName;

            // weaponCategory vit sur Entity (protected set) — immuable après création. GDD §5.1.
            weaponCategory = characterData.WeaponCategory;

            // StatPoints initiaux — level × 3 pts au départ. GDD §3.2.1.
            // Utilise la même formule que OnLevelUp : total attendu = level × 3.
            if (statPoints != null) statPoints.GainPoints(level * 3);

            if (characterData.startingWeapon != null)
                EquipWeapon(characterData.startingWeapon.CreateDropInstance(0, 0));
            else
                stats.RecalculateStats(this);
        }
        else
        {
            Debug.LogWarning("[PLAYER] Aucun CharacterData assigné !");
            stats.RecalculateStats(this);
        }

        base.Awake(); // currentHP/Mana initialisés avec maxHP/maxMana déjà poussés
    }

    protected override void Update()
    {
        base.Update();   // tick de régénération passive (Entity.Update) — sans lui, regen joueur morte
        UpdateAFK();
        UpdateCombat();
    }

    // =========================================================
    // AFK
    // =========================================================

    /// <summary>
    /// Appelé par PlayerController, SkillBar, et tout système
    /// qui représente une action volontaire du joueur.
    /// </summary>
    public void RegisterAction()
    {
        _lastActionTime = Time.time;
        if (IsAFK)
        {
            IsAFK = false;
            if (UnlockManager.Instance != null && UnlockManager.Instance.verboseLogs)
                Debug.Log("[PLAYER] AFK terminé.");
        }
    }

    private void UpdateAFK()
    {
        if (isDead) return;

        bool shouldBeAFK = (Time.time - _lastActionTime) >= afkDelay;
        if (shouldBeAFK != IsAFK)
        {
            IsAFK = shouldBeAFK;
            if (UnlockManager.Instance != null && UnlockManager.Instance.verboseLogs)
                Debug.Log(IsAFK ? "[PLAYER] Joueur AFK." : "[PLAYER] AFK terminé.");
        }
    }

    // =========================================================
    // COMBAT ACTIVE
    // =========================================================

    /// <summary>Appelé au cast d'un skill (UseSkill) et à la réception de dégâts (TakeDamage).</summary>
    public void RegisterCombatAction()
    {
        _lastCombatActionTime = Time.time;
        if (!CombatActive)
        {
            CombatActive = true;
            if (UnlockManager.Instance != null && UnlockManager.Instance.verboseLogs)
                Debug.Log("[PLAYER] Entrée en combat.");
        }
    }

    private void UpdateCombat()
    {
        if (!CombatActive || isDead) return;

        if ((Time.time - _lastCombatActionTime) >= combatExitDelay)
        {
            CombatActive = false;
            if (UnlockManager.Instance != null && UnlockManager.Instance.verboseLogs)
                Debug.Log("[PLAYER] Sortie de combat.");
        }
    }

    private void Start()
    {
        var skills = (characterData != null && characterData.startingSkills.Count > 0)
            ? characterData.startingSkills : startingSkills;
        foreach (var skill in skills)
            UnlockSkill(skill);

        if (UnlockManager.Instance != null)
            UnlockManager.Instance.Init(activityCounter);

        StartCoroutine(SetStartingSkillBar());
        RefreshTitle();
    }

    private void UnlockStartingBasicAttack()
    {
        var registry = WeaponTypeRegistry.Instance;
        if (registry == null)
        {
            Debug.LogWarning("[PLAYER] WeaponTypeRegistry introuvable — BasicAttack de départ non débloquée.");
            return;
        }
        if (equippedWeapon == null)
        {
            Debug.LogWarning("[PLAYER] Aucune arme équipée au départ — BasicAttack non débloquée.");
            return;
        }

        WeaponType family      = equippedWeapon.weaponType.GetStartingFamily();
        SkillData  basicAttack = registry.GetBasicAttackSkill(family);

        if (basicAttack == null)
        {
            Debug.LogWarning($"[PLAYER] Aucune BasicAttack trouvée pour {family} dans WeaponTypeRegistry.");
            return;
        }
        UnlockSkill(basicAttack);
    }

    /// <summary>
    /// Mis à true par SaveSystem après avoir restauré la SkillBar depuis la save.
    /// Empêche SetStartingSkillBar d'écraser le slot 0 sauvegardé.
    /// </summary>
    [HideInInspector] public bool skillBarRestoredFromSave = false;

    private IEnumerator SetStartingSkillBar()
    {
        // Attendre que WeaponTypeRegistry soit prêt
        float elapsed = 0f;
        while (WeaponTypeRegistry.Instance == null)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= 5f) { Debug.LogError("[PLAYER] WeaponTypeRegistry introuvable après 5s."); yield break; }
            yield return null;
        }

        // ⚠ Attendre que SaveSystem ait eu le temps de charger et de lever le flag.
        // 0.15s = LOAD_DELAY dans SaveSystem. On attend un peu plus pour être sûr.
        float saveWait = 0f;
        while (!skillBarRestoredFromSave && saveWait < 0.5f)
        {
            saveWait += Time.deltaTime;
            yield return null;
        }

        if (SkillBar.Instance == null) yield break;

        UnlockStartingBasicAttack();

        // Ne touche au slot 0 que si la save n'a PAS restauré la SkillBar
        if (!skillBarRestoredFromSave)
        {
            RefreshSlot0();
            var startingBasicAttack = unlockedSkills.Find(s =>
                s != null && (s.skillType == SkillType.BasicAttack || s.HasTag(SkillTag.BasicAttack)));
            if (startingBasicAttack != null)
                SkillBar.Instance.SetSkillAtSlot(0, startingBasicAttack);
        }

        // Slots 1-9 — uniquement pour un nouveau personnage sans save
        if (!skillBarRestoredFromSave)
        {
            var skills = (characterData != null && characterData.startingSkills.Count > 0)
                ? characterData.startingSkills : startingSkills;

            int slot = 1;
            foreach (var skill in skills)
            {
                if (skill == null) continue;
                if (skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack)) continue;
                if (skill.skillType == SkillType.Ultimate)
                    SkillBar.Instance.SetSkillAtSlot(9, skill);
                else if (slot <= 8)
                    SkillBar.Instance.SetSkillAtSlot(slot++, skill);
            }
        }
    }

    // =========================================================
    // ÉQUIPEMENT — méthodes d'équipement / déséquipement
    // Chaque méthode appelle stats.RecalculateStats(this) qui pousse
    // toutes les stats sur Entity et fige le snapshot.
    // =========================================================

    public void EquipWeapon(WeaponInstance instance)
    {
        if (instance == null) return;
        // GDD §5.1 — weaponCategory imposée à la création, immuable.
        if (instance.data != null && instance.Category != weaponCategory)
        {
            Debug.LogWarning($"[PLAYER] Arme incompatible — {instance.WeaponName} ({instance.Category}) " +
                             $"ne peut pas être équipée par un joueur {weaponCategory}.");
            return;
        }
        equippedWeaponInstance = instance;
        stats.RecalculateStats(this);
        RefreshSlot0();
        weaponVisual?.RefreshWeapon(instance.data);
    }

    public void UnequipWeapon()
    {
        equippedWeaponInstance = null;
        stats.RecalculateStats(this);
        RefreshSlot0();
        weaponVisual?.RefreshWeapon(null);
    }

    public void EquipArmor(ArmorInstance instance)
    {
        if (instance == null) return;
        // GDD §5.2 — l'armure est entièrement libre, aucune restriction par weaponCategory.
        equippedArmorInstance = instance;
        stats.RecalculateStats(this);
    }

    public void UnequipArmor()  { equippedArmorInstance  = null; stats.RecalculateStats(this); }
    public void EquipHelmet (HelmetInstance  i) { if (i == null) return; equippedHelmetInstance  = i; stats.RecalculateStats(this); }
    public void UnequipHelmet() { equippedHelmetInstance  = null; stats.RecalculateStats(this); }
    public void EquipGloves (GlovesInstance  i) { if (i == null) return; equippedGlovesInstance  = i; stats.RecalculateStats(this); }
    public void UnequipGloves() { equippedGlovesInstance  = null; stats.RecalculateStats(this); }
    public void EquipBoots  (BootsInstance   i) { if (i == null) return; equippedBootsInstance   = i; stats.RecalculateStats(this); }
    public void UnequipBoots()  { equippedBootsInstance   = null; stats.RecalculateStats(this); }

    public void EquipJewelry(JewelryInstance instance)
    {
        if (instance == null || equippedJewelryInstances.Contains(instance)) return;
        foreach (var j in equippedJewelryInstances)
        {
            if (j.Slot == instance.Slot)
            {
                Debug.LogWarning($"[PLAYER] Slot bijou {instance.Slot} déjà occupé par {j.JewelryName}.");
                return;
            }
        }
        equippedJewelryInstances.Add(instance);
        stats.RecalculateStats(this);
    }

    public void UnequipJewelry(JewelryInstance instance)
    {
        if (equippedJewelryInstances.Remove(instance))
            stats.RecalculateStats(this);
    }

    public void EquipSpirit(SpiritInstance instance)
    {
        if (instance == null || equippedSpiritInstances.Contains(instance)) return;
        if (equippedSpiritInstances.Count >= 1)
        {
            Debug.LogWarning($"[PLAYER] Esprit déjà actif ({equippedSpiritInstances[0].SpiritName}). Déséquipez-le d'abord.");
            return;
        }
        equippedSpiritInstances.Add(instance);
        stats.RecalculateStats(this);
    }

    public void UnequipSpirit(SpiritInstance instance)
    {
        if (equippedSpiritInstances.Remove(instance))
            stats.RecalculateStats(this);
    }

    public void EquipCard(CardInstance instance)
    {
        if (instance == null) return;
        equippedCardInstance = instance;
        stats.RecalculateStats(this);
    }

    public void UnequipCard()
    {
        equippedCardInstance = null;
        stats.RecalculateStats(this);
    }  
    
    public void EquipCosmeticHead(CosmeticInstanceHead instance)
    {
        if (instance == null) return;
        equippedCosmeticHeadInstance = instance;
        stats.RecalculateStats(this);
    }

    public void UnequipCosmeticHead()
    {
        equippedCosmeticHeadInstance = null;
        stats.RecalculateStats(this);
    }

    public void EquipCosmeticBody(CosmeticInstanceBody instance)
    {
        if (instance == null) return;
        equippedCosmeticBodyInstance = instance;
        stats.RecalculateStats(this);
    }

    public void UnequipCosmeticBody()
    {
        equippedCosmeticBodyInstance = null;
        stats.RecalculateStats(this);
    }

    // =========================================================
    // SKILLS
    // =========================================================

    public void UnlockSkill(SkillData skill)
    {
        if (skill == null || unlockedSkills.Contains(skill)) return;
        unlockedSkills.Add(skill);
        SkillLibraryUI.Instance?.RefreshIfOpen();
    }

    public void UnlockPermanent(PermanentSkillData permanent)
    {
        if (permanent == null || unlockedPermanents.Contains(permanent)) return;
        unlockedPermanents.Add(permanent);
        stats.RecalculateStats(this);
        SkillLibraryUI.Instance?.RefreshIfOpen();
        Debug.Log($"[PLAYER] Permanent débloqué : {permanent.name}");
    }

    public void UnlockPassive(PassiveSkillData passive)
    {
        if (passive == null || unlockedPassives.Contains(passive)) return;
        unlockedPassives.Add(passive);
        SkillLibraryUI.Instance?.RefreshIfOpen();
        Debug.Log($"[PLAYER] Passive débloquée : {passive.name}");
    }

        // ── AJOUTER RefreshSlot0() ────────────────────────────────
    /// <summary>
    /// Met à jour le slot 0 de SkillBar selon l'arme équipée.
    /// - Arme équipée  → BasicAttack de la famille (choisie par le joueur
    ///                   si plusieurs dans unlockedSkills, sinon celle du registry).
    /// - Aucune arme   → UnArmedSkill (WeaponType.UnArmed).
    /// Appelé par EquipWeapon() et UnequipWeapon().
    /// </summary>
    private void RefreshSlot0()
    {
        if (SkillBar.Instance == null) return;

        // Si la save a déjà restauré la SkillBar (ex: pendant LoadItemsDelayed),
        // on ne touche pas au slot 0 — UnequipAll/EquipItem ne doivent pas écraser.
        if (skillBarRestoredFromSave) return;
 
        // ── Pas d'arme → Unarmed ─────────────────────────────
        if (equippedWeapon == null)
        {
            var registry = WeaponTypeRegistry.Instance;
            if (registry != null)
            {
                SkillData unarmedSkill = registry.GetBasicAttackSkill(WeaponType.UnArmed);
                if (unarmedSkill != null)
                {
                    SkillBar.Instance.SetSkillAtSlot(0, unarmedSkill);
                    Debug.Log("[PLAYER] Slot 0 → UnArmedSkill (aucune arme équipée).");
                    return;
                }
            }
            Debug.LogWarning("[PLAYER] RefreshSlot0 : UnArmedSkill introuvable dans WeaponTypeRegistry.");
            return;
        }
 
        // ── Arme équipée → BasicAttack de la famille ──────────
        WeaponType family = equippedWeapon.weaponType.GetStartingFamily();
 
        // Priorité 1 : skill BasicAttack déjà débloqué et compatible avec cette famille
        // (le joueur peut avoir débloqué une BasicAttack alternative — ex: BasicAttackFeu)
        SkillData chosen = unlockedSkills.Find(s =>
            s != null
            && (s.skillType == SkillType.BasicAttack || s.HasTag(SkillTag.BasicAttack))
            && (s.compatibleWeapons == null
                || s.compatibleWeapons.Count == 0
                || s.compatibleWeapons.Contains(family)
                || s.compatibleWeapons.Contains(WeaponType.Any)));
 
        // Priorité 2 : BasicAttack de départ du registry
        if (chosen == null)
        {
            var registry = WeaponTypeRegistry.Instance;
            if (registry != null)
                chosen = registry.GetBasicAttackSkill(family);
        }
 
        if (chosen != null)
        {
            SkillBar.Instance.SetSkillAtSlot(0, chosen);
            Debug.Log($"[PLAYER] Slot 0 → {chosen.name} ({family}).");
        }
        // Si chosen == null ici c'est que le registry n'est pas encore prêt (Awake trop tôt).
        // SetStartingSkillBar() s'en occupe un frame après Start(), pas de warning parasite.
    }
 

    // =========================================================
    // RECALCUL — override Entity.RequestRecalculate()
    // =========================================================

    /// <summary>
    /// Recalcul complet depuis l'équipement + niveau + StatPoints.
    /// CharacterStats.RecalculateStats() snapshote les stats propres en fin de calcul,
    /// puis StatusEffectSystem.ReapplyActiveModifiers() ré-applique les effets actifs.
    /// </summary>
    public override void RequestRecalculate()
    {
        stats.RecalculateStats(this);
        // SnapshotBaseStats() est appelé dans RecalculateStats() — pas besoin de le rappeler.
        // ReapplyActiveModifiers() est appelé par Entity.RequestRecalculate() de base,
        // mais on override complètement — on l'appelle explicitement ici.
        statusEffects?.ReapplyActiveModifiers(this);
    }

    // =========================================================
    // DÉGÂTS & MORT
    // =========================================================

    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
    {
        float hpBefore = currentHP;

        // Réduction de dégâts reçus Neutre rang 4+ — GDD §6.3
        if (elementalSystem != null)
        {
            float neutralReduction = elementalSystem.GetNeutralDamageReduction();
            if (neutralReduction > 0f)
                amount *= (1f - neutralReduction);
        }

        base.TakeDamage(amount, sourceElement, source);

        // Entrée en combat uniquement si une source réelle a infligé les dégâts (pas un
        // coût HP de skill ou autre auto-infligé qui passerait par TakeDamage).
        if (source != null) RegisterCombatAction();

        activityCounter.Increment("DAMAGE_TAKEN_TOTAL", (int)amount);

        // isOneHit : ce seul coup a suffi à vider les HP (la cible était pleine ou presque,
        // et tombe à 0). On considère "full HP" = hpBefore == maxHP pour coller au GDD.
        // isCrit : non connu ici (calculé dans CombatSystem) — reste false côté réception ;
        // les conditions isReceived+isCrit doivent être évaluées côté attaquant.
        bool isOneHit = hpBefore >= maxHP && currentHP <= 0f;

        GameEventBus.Publish(new DamageDealtEvent
        {
            amount   = amount,
            element  = sourceElement,
            source   = source,
            target   = this,
            isCrit   = false,
            isOneHit = isOneHit,
        });

        if (hpBefore > 1f && currentHP <= 1f)
            activityCounter.Increment(CounterKeys.SURVIVE_1HP);

        if (source != null)
            ApplyOnHitEffects(amount, source);
    }

    // =========================================================
    // KNOCKBACK — GDD v3.5 §3.1.1.1
    // =========================================================

    /// <summary>
    /// Override de Entity.ApplyKnockBack() — délègue le déplacement à PlayerController.
    /// Interrompt le déplacement et l'éventuel cast en cours.
    /// </summary>
    public override void ApplyKnockBack(Vector3 direction, float force)
    {
        if (isDead || statusEffects.isStunned || statusEffects.isRooted) return;
        // TODO: PlayerController.Instance?.ApplyKnockBack(direction, force);
        Debug.Log($"[PLAYER] KnockBack — direction:{direction} force:{force}");
    }

    /// <summary>
    /// Applique les effets On-Hit de tout l'équipement quand le joueur reçoit un coup.
    /// Chaque effet a une chance d'activation définie sur OnHitEffectData.
    /// Les effets sont lus depuis instance.OnHitEffects → data.config.onHitEffects.
    /// GDD §5.1 à §5.6 — unifié via EquipmentConfig.
    /// </summary>
    private void ApplyOnHitEffects(float damageTaken, Entity attacker)
    {
        var allOnHit = new List<List<OnHitEffectEntry>>();

        // Équipements via EquipmentConfig.onHitEffects
        if (equippedWeaponInstance?.OnHitEffects != null) allOnHit.Add(equippedWeaponInstance.OnHitEffects);
        if (equippedArmorInstance?.OnHitEffects  != null) allOnHit.Add(equippedArmorInstance.OnHitEffects);
        if (equippedHelmetInstance?.OnHitEffects != null) allOnHit.Add(equippedHelmetInstance.OnHitEffects);
        if (equippedGlovesInstance?.OnHitEffects != null) allOnHit.Add(equippedGlovesInstance.OnHitEffects);
        if (equippedBootsInstance?.OnHitEffects  != null) allOnHit.Add(equippedBootsInstance.OnHitEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitEffects != null) allOnHit.Add(j.OnHitEffects);

        // Skills permanents — liste propre (pas via EquipmentConfig)
        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitEffects != null && p.onHitEffects.Count > 0)
                    allOnHit.Add(p.onHitEffects);

        foreach (var list in allOnHit)
        {
            if (list == null) continue;
            foreach (var entry in list)
            {
                if (entry?.effect == null || !entry.Roll()) continue;
                switch (entry.effect.effectType)
                {
                    case OnHitEffectType.Thorns:
                        attacker.TakeDamage(entry.effect.thornsDamage, entry.effect.reflectElement, this);
                        break;
                    case OnHitEffectType.ReflectPercent:
                        attacker.TakeDamage(damageTaken * entry.effect.reflectPercent, entry.effect.reflectElement, this);
                        break;
                    case OnHitEffectType.HealOnHit:
                        Heal(entry.effect.GetHealAmount(MaxHP));
                        break;
                    case OnHitEffectType.CounterDebuff:
                        if (entry.effect.counterDebuff != null && attacker.statusEffects != null)
                            attacker.statusEffects.TryApplyDebuff(entry.effect.counterDebuff, this);
                        break;
                    case OnHitEffectType.CounterBuff:
                        if (entry.effect.counterBuff != null && statusEffects != null)
                            statusEffects.ApplyBuff(entry.effect.counterBuff, this);
                        break;
                }
            }
        }
    }

    protected override void Die()
    {
        base.Die();
        GameEventBus.Publish(new PlayerDeathEvent
        {
            cause     = ElementType.Neutral,
            killer    = null,
            hpAtDeath = currentHP,
            context   = DeathContext.OpenWorld,
        });
        RespawnSystem.Instance?.TriggerDeath();
    }

    // =========================================================
    // KILLS
    // =========================================================

    private void OnEnable()  => GameEventBus.OnMobKilled += HandleMobKilled;
    private void OnDisable() => GameEventBus.OnMobKilled -= HandleMobKilled;

    private void HandleMobKilled(MobKilledEvent e)
    {
        if (e.eligiblePlayers == null || !e.eligiblePlayers.Contains(this)) return;
        if (e.mob == null) return;

        activityCounter.Increment(CounterKeys.KILLS_TOTAL);
        activityCounter.Increment($"KILLS_{e.mob.elementType.ToString().ToUpper()}_MOB");
        activityCounter.Increment($"KILLS_MOB_{e.mob.mobName.ToUpper().Replace(" ", "_")}");
        lastSkillUsed = null;
    }

    // =========================================================
    // SKILLS — usage
    // =========================================================

    public void UseSkill(SkillData skill, Entity target = null)
    {
        if (skill == null) return;
        lastSkillUsed = skill;
        RegisterCombatAction();
        animatorController?.PlayAttack(skill.attackAnimation);

        bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);

        if (!skill.IsNeutral)
            foreach (var element in skill.elements)
                elementalSystem.RegisterCast(element, isBasicAttack: isBasic);
        else
            elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic);

        stats.RecalculateStats(this);
        RefreshTitle();
    }

    // =========================================================
    // TITRE ACTIF — GDD §2.3
    // =========================================================

    public void RefreshTitle()
    {
        if (elementalSystem == null) return;

        bool       hasDual  = SkillBar.Instance?.HasDualElementSkillEquipped() ?? false;
        WeaponType wt       = equippedWeapon?.weaponType ?? WeaponType.UnArmed;
        TitleMode  mode     = elementalSystem.GetTitleMode(hasDual);

        string weaponLabel = wt.GetWeaponLabel();  // ex: "Lame", "Berserker"...

        switch (mode)
        {
            case TitleMode.Mono:
            {
                string epithet = elementalSystem.BuildTitle(wt, hasDual);
                activeTitle = string.IsNullOrEmpty(weaponLabel)
                    ? epithet
                    : $"{weaponLabel} {epithet}";
                break;
            }

            case TitleMode.Dual:
            {
                var candidates = elementalSystem.GetDualCandidates();
                if (candidates.Count >= 2)
                {
                    string ep1 = candidates[0].GetEpithet(wt);
                    string ep2 = candidates[1].GetEpithet(wt);
                    if (string.IsNullOrEmpty(ep1)) ep1 = candidates[0].GetLabel();
                    if (string.IsNullOrEmpty(ep2)) ep2 = candidates[1].GetLabel();

                    activeTitle = string.IsNullOrEmpty(weaponLabel)
                        ? $"{ep1} et {ep2}"
                        : $"{weaponLabel} {ep1} et {ep2}";
                }
                else
                {
                    goto case TitleMode.Mono;  // fallback
                }
                break;
            }

            case TitleMode.Equilibriste:
                activeTitle = string.IsNullOrEmpty(weaponLabel)
                    ? "Équilibriste"
                    : $"{weaponLabel} Équilibriste";
                break;

            default:  // Neutral
                string neutralEp = ElementType.Neutral.GetEpithet(wt);
                activeTitle = string.IsNullOrEmpty(weaponLabel)
                    ? (string.IsNullOrEmpty(neutralEp) ? "Aventurier" : neutralEp)
                    : $"{weaponLabel} {(string.IsNullOrEmpty(neutralEp) ? "Pur" : neutralEp)}";
                break;
        }
    }

    // =========================================================
    // RÉPUTATION MONDE — GDD §3.4
    // =========================================================

    public void AddWorldReputation(int amount)
    {
        worldReputation = Mathf.Max(0, worldReputation + amount);
        worldReputationRank = 0;
        for (int i = WorldRepThresholds.Length - 1; i >= 0; i--)
            if (worldReputation >= WorldRepThresholds[i]) { worldReputationRank = i; break; }
    }

    public float GetHdVListingFeeRate()
    {
        float[] fees = { 0.02f, 0.015f, 0.01f, 0.0075f, 0.005f, 0.0025f };
        return fees[Mathf.Clamp(worldReputationRank, 0, fees.Length - 1)];
    }

    public int GetHdVSlotCount()
    {
        int[] slots = { 3, 5, 8, 12, 16, 20 };
        return slots[Mathf.Clamp(worldReputationRank, 0, slots.Length - 1)];
    }

    // =========================================================
    // RÉPUTATION PVP — GDD §3.4
    // =========================================================

    public void AddPvPReputation(int amount)
    {
        pvpReputation = Mathf.Max(0, pvpReputation + amount);
        pvpReputationRank = 0;
        for (int i = PvPRepThresholds.Length - 1; i >= 0; i--)
            if (pvpReputation >= PvPRepThresholds[i]) { pvpReputationRank = i; break; }
    }

    public void OnOpenWorldDeath() { activityCounter.Increment("DEATHS_OPEN_WORLD"); AddWorldReputation(-1); }
    public void OnPvPDeath()       { activityCounter.Increment("PVP_DEATHS");        AddPvPReputation(-2); }
    public void OnPvPKill(string id)   { activityCounter.Increment("PVP_KILLS");      AddPvPReputation(2);  }
    public void OnDuelWon(string id)   { activityCounter.Increment("DUELS_WON");      GameEventBus.Publish(new SocialEvent { action = SocialAction.DuelWin, otherPlayerID = id }); AddPvPReputation(3); }
    public void OnDuelLost(string id)  { activityCounter.Increment("DUELS_LOST");     AddPvPReputation(-1); }
    public void OnFreeArenaTop3()      { activityCounter.Increment("FREE_ARENA_TOP3"); AddPvPReputation(4); }
    public void OnPvPReportValidated()    { AddPvPReputation(-10); }
    public void OnWorldReportValidated()  { AddWorldReputation(-5); }

    public void OnArenaResult(bool won)
    {
        if (won) { activityCounter.Increment("ARENA_WINS");   AddPvPReputation(5);  }
        else     { activityCounter.Increment("ARENA_LOSSES"); AddPvPReputation(-2); }
    }

    public void OnBattlefieldResult(bool won)
    {
        if (won) { activityCounter.Increment("BATTLEFIELD_WINS");   AddPvPReputation(6);  }
        else     { activityCounter.Increment("BATTLEFIELD_LOSSES"); AddPvPReputation(-2); }
    }

    // =========================================================
    // GUILDE — GDD §3.4
    // =========================================================

    public void RegisterGroupMemberLed(string playerID)
    {
        if (string.IsNullOrEmpty(playerID)) return;
        uniqueGroupMembersLed.Add(playerID);
        activityCounter.Set("GROUP_LEADER_UNIQUE_COUNT", uniqueGroupMembersLed.Count);
        GameEventBus.Publish(new SocialEvent { action = SocialAction.GroupLeader, otherPlayerID = playerID });
    }

    public bool CanCreateGuild() => uniqueGroupMembersLed.Count >= 20;

    // =========================================================
    // PROGRESSION
    // =========================================================

    /// <summary>
    /// Level up — met à jour level, gagne 3 StatPoints, recalcule les stats.
    /// ⚠ Ne touche JAMAIS directement aux champs Entity — tout passe par RecalculateStats().
    /// </summary>
    public void OnLevelUp(int newLevel)
    {
        level = newLevel;
        activityCounter.Set("PLAYER_LEVEL", newLevel);

        // Total attendu = level × 3 — on donne la différence pour gérer
        // les level ups multiples (chargement save) sans doubler. GDD §3.2.1.
        if (statPoints != null)
        {
            int expected = newLevel * 3;
            int toGive   = expected - statPoints.totalPointsEarned;
            if (toGive > 0) statPoints.GainPoints(toGive);
        }

        stats.RecalculateStats(this);

        // Soin complet au level up
        currentHP   = maxHP;
        currentMana = maxMana;

        RefreshTitle();
        Debug.Log($"[PLAYER] Level up → {newLevel} | StatPoints disponibles : {statPoints?.availablePoints}");
    }

    public void AddCombatXP(int amount)
    {
        xpCombat += amount;
        xpToNextLevel = characterData != null
            ? characterData.GetXPThreshold(level)
            : XPSystem.CalculateXPForLevel(level);

        if (xpCombat >= xpToNextLevel)
        {
            xpCombat -= xpToNextLevel;
            OnLevelUp(level + 1);
        }
    }

    // =========================================================
    // RÉSURRECTION — GDD §3.1.1.3
    // =========================================================

    public void Revive(float hpPercent = 0.30f, float manaPercent = 0.30f)
    {
        isDead      = false;
        currentHP   = maxHP   * hpPercent;
        currentMana = maxMana * manaPercent;
        PassiveSkillSystem.Instance?.ResetCombat();
    }

    public void ReviveAtRespawnPoint()
    {
        isDead      = false;
        currentHP   = maxHP;
        currentMana = maxMana;
        // TODO: TeleportSystem.Instance?.RespawnAtPoint(this)
    }

    // =========================================================
    // ÉVÉNEMENTS DIVERS
    // =========================================================

    public void OnEnterZone(string zoneID)
    {
        currentZoneID = zoneID;
        activityCounter.Increment($"ZONE_{zoneID}");
        GameEventBus.Publish(new ZoneEvent { zoneID = zoneID, timeSpentSeconds = 0f, isAFK = false });
    }

    public void OnZoneTick(string zoneID, float duration, bool isAFK)
        => GameEventBus.Publish(new ZoneEvent { zoneID = zoneID, timeSpentSeconds = duration, isAFK = isAFK });

    public void OnItemConsumed(string itemID, bool wastePotion = false)
        => activityCounter.Increment(wastePotion ? "POTIONS_WASTED" : "POTIONS_USED");

    public void OnItemCrafted(string itemID)
    {
        activityCounter.Increment(CounterKeys.CRAFTS_TOTAL);
        GameEventBus.Publish(new ItemEvent { itemID = itemID, action = ItemAction.Craft, quantity = 1 });
    }

    public void OnItemSold(string itemID, int aeris)
    {
        activityCounter.Increment("ITEMS_SOLD");
        activityCounter.Increment("AERIS_EARNED_TOTAL", aeris);
        GameEventBus.Publish(new ItemEvent { itemID = itemID, action = ItemAction.Sell, quantity = 1, aerisAmount = aeris });
    }

    public void OnHdVTransactionCompleted()  { activityCounter.Increment("HDV_TRANSACTIONS");  AddWorldReputation(1);  }
    public void OnHdVListingCancelled()      { activityCounter.Increment("HDV_CANCELLATIONS"); AddWorldReputation(-1); }
    public void OnPlayerMet(string playerID) { activityCounter.Increment(CounterKeys.PLAYERS_MET); GameEventBus.Publish(new SocialEvent { action = SocialAction.MeetPlayer, otherPlayerID = playerID }); }

    public void OnActivitySessionCompleted(string activityID)
    {
        activityCounter.Increment($"ACTIVITY_{activityID}");
        int today = activityCounter.GetToday("WORLD_REP_ACTIVITY_DAILY");
        if (today < 5) { activityCounter.IncrementToday("WORLD_REP_ACTIVITY_DAILY"); AddWorldReputation(1); }
        GameEventBus.Publish(new MetierEvent { metierID = activityID, actionType = "session_complete", newLevel = 0 });
    }

    public void OnPetCaptured(MobData mob)                  { activityCounter.Increment("PETS_CAPTURED");    GameEventBus.Publish(new PetEvent { action = PetAction.Capture, mob = mob }); }
    public void OnAnimalCaressed(string id)                 { activityCounter.Increment("ANIMALS_CARESSED"); GameEventBus.Publish(new PetEvent { action = PetAction.Talk, npcID = id }); }
    public void OnMetierAction(string m, string a, int l=0) => GameEventBus.Publish(new MetierEvent { metierID = m, actionType = a, newLevel = l });
    public void OnServerConnect(bool isFirst)               { if (isFirst) activityCounter.Increment("FIRST_ON_SERVER"); GameEventBus.Publish(new ServerEvent { firstConnection = isFirst }); }

    // =========================================================
    // ACCESSEURS
    // =========================================================

    public ActivityCounter GetActivityCounter() => activityCounter;
    public ElementalSystem GetElementalSystem()  => elementalSystem;

    public float GetElementPoints(ElementType element)
        => stats.GetElementalPoints(element);

    /// <summary>
    /// Réduction de dégâts critiques reçus [0..1].
    /// Source : StatPoints Défense. Lue par CombatSystem au moment du calcul.
    /// </summary>
    public float GetCritDamageReduction() => critDamageReduction;

    // =========================================================
    // SETTERS ENTITY spécifiques au joueur
    // Les setters génériques (SetMaxHP, SetMeleeDefense...) sont sur Entity.
    // =========================================================

}
