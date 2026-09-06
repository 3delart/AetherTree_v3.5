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
    [HideInInspector] public TalismanInstance      equippedTalismanInstance;
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

    /// <summary>Recettes de craft connues du joueur — Base auto-accordées par
    /// CraftSystem.GrantBaseRecipes(), Unlocked via ConditionReward/UnlockRecipe().</summary>
    [HideInInspector] public List<RecipeData> unlockedRecipes = new List<RecipeData>();

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

    // ── Stealth — visuel ──────────────────────────────────────
    [Header("Stealth")]
    [Tooltip("Opacité du modèle pendant le Stealth [0..1]. Nécessite un matériau en mode\n" +
             "Transparent/Fade (Opaque ignore l'alpha, aucun effet visible dans ce cas).")]
    [Range(0f, 1f)] public float stealthAlpha = 0.35f;
    private Renderer[] _renderers;
    private bool       _wasStealthed = false;
    private static readonly int ColorID     = Shader.PropertyToID("_Color");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

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
        if (unlockedRecipes          == null) unlockedRecipes          = new List<RecipeData>();
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
        CheckTalismanExpiry();
        UpdateStealthVisual();
    }

    // =========================================================
    // STEALTH — visuel
    // =========================================================

    /// <summary>Applique/retire la transparence du modèle quand l'état Stealth change — ne
    /// touche les Renderer que sur transition (pas chaque frame). Instancie le matériau de
    /// chaque Renderer (via renderer.material — Unity clone automatiquement à la 1ère
    /// utilisation, jamais le sharedMaterial d'origine) plutôt que d'utiliser un
    /// MaterialPropertyBlock : en URP, un matériau en Surface Type "Opaque" ignore
    /// complètement l'alpha au niveau GPU, donc juste changer la couleur ne suffit pas — il
    /// faut aussi basculer le matériau en "Transparent" au runtime (voir
    /// SetMaterialSurfaceTransparent). Choix voulu pour le futur multijoueur : marche sur
    /// N'IMPORTE QUEL modèle/skin sans préparation manuelle d'un matériau dédié par asset.</summary>
    private void UpdateStealthVisual()
    {
        bool stealthed = statusEffects != null && statusEffects.isStealthed;
        if (stealthed == _wasStealthed) return;
        _wasStealthed = stealthed;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();

        float alpha = stealthed ? stealthAlpha : 1f;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            Material mat = r.material; // instance per-renderer, jamais le sharedMaterial

            if (mat.HasProperty(ColorID))
            {
                Color c = mat.GetColor(ColorID);
                c.a = alpha;
                mat.SetColor(ColorID, c);
            }
            if (mat.HasProperty(BaseColorID))
            {
                Color c = mat.GetColor(BaseColorID);
                c.a = alpha;
                mat.SetColor(BaseColorID, c);
            }

            SetMaterialSurfaceTransparent(mat, stealthed);
        }
    }

    /// <summary>Bascule un matériau URP Lit/SimpleLit entre Opaque et Transparent au runtime —
    /// recette standard (SetOverrideTag/SrcBlend/DstBlend/ZWrite/keywords/renderQueue/_Surface).
    /// Sans effet si le shader n'expose pas _Surface (ex: ShaderGraph custom sans ce paramètre)
    /// — dans ce cas seule la couleur change, pas de régression par rapport à avant.</summary>
    private static void SetMaterialSurfaceTransparent(Material mat, bool transparent)
    {
        if (!mat.HasProperty("_Surface")) return;

        if (transparent)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetFloat("_Surface", 1f);
        }
        else
        {
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = -1; // reset — reprend la queue par défaut du shader
            mat.SetFloat("_Surface", 0f);
        }
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

    /// <summary>Mis à true par SaveSystem juste avant RestoreItems() (équipement), remis à
    /// false juste après — fenêtre courte, contrairement à skillBarRestoredFromSave qui
    /// reste vrai toute la session. Empêche uniquement EquipWeapon() pendant la restauration
    /// d'écraser le slot 0 déjà posé depuis skillBarSlots ; les équip/déséquip normaux du
    /// reste de la partie (CharacterPanel...) doivent continuer à mettre à jour RefreshSlot0().</summary>
    [HideInInspector] public bool isRestoringEquipment = false;

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

    /// <summary>Équipe le talisman — démarre son chrono s'il n'a jamais été activé (sans
    /// effet si déjà activé) et réapplique son buff, même après un ré-équipement.</summary>
    public void EquipTalisman(TalismanInstance instance)
    {
        if (instance == null || instance.IsExpired) return;
        instance.Activate();
        equippedTalismanInstance = instance;
        if (instance.data?.buffToApply != null)
            statusEffects?.ApplyBuffWithDuration(instance.data.buffToApply, this, instance.RemainingSeconds);
        stats.RecalculateStats(this);
    }

    /// <summary>Retire le talisman du CharacterPanel — coupe son buff immédiatement. Le
    /// chrono continue de tourner indépendamment (voir TalismanInstance.activatedAt) : ceci
    /// ne détruit PAS l'objet, juste retiré du slot (voir CheckTalismanExpiry pour la
    /// destruction à expiration).</summary>
    public void UnequipTalisman()
    {
        if (equippedTalismanInstance?.data?.buffToApply != null)
            statusEffects?.RemoveBuff(equippedTalismanInstance.data.buffToApply.buffType);
        equippedTalismanInstance = null;
        stats.RecalculateStats(this);
    }

    /// <summary>Détruit le talisman équipé s'il a expiré — appelé depuis Update(). Ne renvoie
    /// PAS l'objet en inventaire (contrairement à un retrait manuel via InventorySystem),
    /// c'est la destruction elle-même.</summary>
    private void CheckTalismanExpiry()
    {
        if (equippedTalismanInstance != null && equippedTalismanInstance.IsExpired)
        {
            Debug.Log($"[TALISMAN] Expiré et détruit : {equippedTalismanInstance.TalismanName}");
            UnequipTalisman();
        }
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

    public void UnlockRecipe(RecipeData recipe)
    {
        if (recipe == null || unlockedRecipes.Contains(recipe)) return;
        unlockedRecipes.Add(recipe);
        CraftJournalUI.Instance?.RefreshIfOpen();
        Debug.Log($"[PLAYER] Recette débloquée : {recipe.name}");
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

        // Suppression UNIQUEMENT pendant la restauration d'équipement au chargement
        // (SaveSystem.RestoreItems() appelle EquipWeapon(), qui ne doit pas écraser le
        // slot 0 déjà restauré depuis skillBarSlots). isRestoringEquipment est remis à
        // false juste après par SaveSystem — contrairement à skillBarRestoredFromSave
        // (permanent pour la session), ce flag-ci ne bloque QUE cette fenêtre précise,
        // pas les équip/déséquip normaux du reste de la partie (ex: CharacterPanel).
        if (isRestoringEquipment) return;
 
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
        var        familyRegistry = WeaponTypeRegistry.Instance;
        SkillData  currentUnarmedSkill = familyRegistry?.GetBasicAttackSkill(WeaponType.UnArmed);

        bool IsCompatible(SkillData s) => s != null
            && s != currentUnarmedSkill // compatibleWeapons vide = "universel", mais l'UnArmedSkill
                                         // ne doit jamais survivre à un équipement d'arme réelle
            && (s.skillType == SkillType.BasicAttack || s.HasTag(SkillTag.BasicAttack))
            && (s.compatibleWeapons == null
                || s.compatibleWeapons.Count == 0
                || s.compatibleWeapons.Contains(family)
                || s.compatibleWeapons.Contains(WeaponType.Any));

        // Priorité 0 : garder le choix déjà en slot 0 s'il reste compatible avec la
        // nouvelle arme (ex: switch entre deux Shortsword — le joueur avait choisi la
        // variante Eau, changer d'arme de la même famille ne doit pas revenir au neutre).
        SkillData chosen = IsCompatible(SkillBar.Instance.GetSkillAtSlot(0))
            ? SkillBar.Instance.GetSkillAtSlot(0)
            : null;

        // Priorité 1 : skill BasicAttack débloqué et compatible avec cette famille
        // (le joueur peut avoir débloqué une BasicAttack alternative — ex: BasicAttackFeu)
        if (chosen == null)
            chosen = unlockedSkills.Find(IsCompatible);

        // Priorité 2 : BasicAttack de départ du registry
        if (chosen == null && familyRegistry != null)
            chosen = familyRegistry.GetBasicAttackSkill(family);
 
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
    }

    // =========================================================
    // KNOCKBACK — GDD v3.5 §3.1.1.1
    // =========================================================

    /// <summary>
    /// Override de Entity.ApplyKnockBack() — la mécanique (repoussement + mini-stun) vit dans
    /// Entity (générique Player/Mob/PNJ), ce override n'ajoute que le garde-fou spécifique au
    /// joueur : pas de knockback si déjà mort/stun/root.
    /// </summary>
    public override void ApplyKnockBack(Vector3 direction, float force)
    {
        if (isDead || statusEffects.isStunned || statusEffects.isShocked || statusEffects.isRooted) return;
        base.ApplyKnockBack(direction, force);
    }

    /// <summary>Agrège les effets On-Hit infligés de tout l'équipement + permanents débloqués.</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects()
    {
        var all = new List<OnHitDealtEffectEntry>();

        if (equippedWeaponInstance?.OnHitDealtEffects != null) all.AddRange(equippedWeaponInstance.OnHitDealtEffects);
        if (equippedArmorInstance?.OnHitDealtEffects  != null) all.AddRange(equippedArmorInstance.OnHitDealtEffects);
        if (equippedHelmetInstance?.OnHitDealtEffects != null) all.AddRange(equippedHelmetInstance.OnHitDealtEffects);
        if (equippedGlovesInstance?.OnHitDealtEffects != null) all.AddRange(equippedGlovesInstance.OnHitDealtEffects);
        if (equippedBootsInstance?.OnHitDealtEffects  != null) all.AddRange(equippedBootsInstance.OnHitDealtEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitDealtEffects != null) all.AddRange(j.OnHitDealtEffects);

        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitDealtEffects != null) all.AddRange(p.onHitDealtEffects);

        return all;
    }

    /// <summary>Agrège les effets On-Hit reçus de tout l'équipement + permanents débloqués.</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects()
    {
        var all = new List<OnHitReceivedEffectEntry>();

        if (equippedWeaponInstance?.OnHitReceivedEffects != null) all.AddRange(equippedWeaponInstance.OnHitReceivedEffects);
        if (equippedArmorInstance?.OnHitReceivedEffects  != null) all.AddRange(equippedArmorInstance.OnHitReceivedEffects);
        if (equippedHelmetInstance?.OnHitReceivedEffects != null) all.AddRange(equippedHelmetInstance.OnHitReceivedEffects);
        if (equippedGlovesInstance?.OnHitReceivedEffects != null) all.AddRange(equippedGlovesInstance.OnHitReceivedEffects);
        if (equippedBootsInstance?.OnHitReceivedEffects  != null) all.AddRange(equippedBootsInstance.OnHitReceivedEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitReceivedEffects != null) all.AddRange(j.OnHitReceivedEffects);

        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitReceivedEffects != null) all.AddRange(p.onHitReceivedEffects);

        return all;
    }

    protected override void Die()
    {
        // Consommé AVANT base.Die() — celui-ci wipe tous les effets actifs via
        // ClearAllEffects(), Revive y compris s'il n'est pas déjà retiré ici.
        float reviveDelay = 0f, reviveHP = 0f, reviveMana = 0f;
        bool hasRevive = statusEffects != null &&
            statusEffects.TryConsumeRevive(out reviveDelay, out reviveHP, out reviveMana);

        base.Die();
        GameEventBus.Publish(new PlayerDeathEvent
        {
            cause     = ElementType.Neutral,
            killer    = null,
            hpAtDeath = currentHP,
            context   = DeathContext.OpenWorld,
        });

        if (hasRevive)
            RespawnSystem.Instance?.TriggerDelayedRevive(reviveDelay, reviveHP, reviveMana);
        else
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

        // Buff/Debuff ne comptent PAS pour la fenêtre d'affinité — un soin/buff tagué Eau ne
        // "joue" pas de l'eau au sens combat, seuls les skills qui infligent réellement des
        // dégâts (Damage/Other, ex: DrainHP) font bouger le rang élémentaire.
        bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                               && skill.effectType != SkillEffectType.Debuff;

        // Stealth — casser UNIQUEMENT sur dégâts infligés (Damage/Other), pas sur un
        // Buff/Debuff seul (soigner/buffer un allié, ou même debuff un ennemi sans dégâts, ne
        // révèle pas) — décision explicite Florian, même distinction que countsForAffinity.
        if (countsForAffinity && statusEffects != null && statusEffects.isStealthed)
            statusEffects.RemoveBuff(BuffType.Stealth);

        if (countsForAffinity)
        {
            if (!skill.IsNeutral)
                foreach (var element in skill.elements)
                    elementalSystem.RegisterCast(element, isBasicAttack: isBasic);
            else
                elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic);
        }

        // RequestRecalculate() (pas juste stats.RecalculateStats()) — sinon le pass équipement
        // tourne seul, SANS jamais relancer ReapplyActiveModifiers() après : un buff actif sur
        // n'importe quelle stat se faisait effacer dès le skill suivant (attaque de base
        // incluse), car son contenu n'était jamais réappliqué par-dessus le recalcul équipement.
        RequestRecalculate();
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

    public void OnItemSold(string itemID, int aeris)
    {
        activityCounter.Increment("ITEMS_SOLD");
        activityCounter.Increment("AERIS_EARNED_TOTAL", aeris);
        GameEventBus.Publish(new ItemEvent { itemID = itemID, action = ItemAction.Sell, quantity = 1, aerisAmount = aeris });
    }

    public void OnHdVTransactionCompleted()  { activityCounter.Increment("HDV_TRANSACTIONS");  AddWorldReputation(1);  }
    public void OnHdVListingCancelled()      { activityCounter.Increment("HDV_CANCELLATIONS"); AddWorldReputation(-1); }
    public void OnPlayerMet(string playerID) { activityCounter.Increment(CounterKeys.PLAYERS_MET); GameEventBus.Publish(new SocialEvent { action = SocialAction.MeetPlayer, otherPlayerID = playerID }); }

    public void OnPetCaptured(MobData mob)                  { activityCounter.Increment("PETS_CAPTURED");    GameEventBus.Publish(new PetEvent { action = PetAction.Capture, mob = mob }); }
    public void OnAnimalCaressed(string id)                 { activityCounter.Increment("ANIMALS_CARESSED"); GameEventBus.Publish(new PetEvent { action = PetAction.Talk, npcID = id }); }
    public void OnServerConnect(bool isFirst)               { if (isFirst) activityCounter.Increment("FIRST_ON_SERVER"); GameEventBus.Publish(new ServerEvent { firstConnection = isFirst }); }

    // =========================================================
    // ACCESSEURS
    // =========================================================

    public ActivityCounter GetActivityCounter() => activityCounter;
    public ElementalSystem GetElementalSystem()  => elementalSystem;

    /// <summary>Points élémentaires EFFECTIFS (équipement + buffs) — lit la valeur poussée sur
    /// l'Entity, comme TOUTE autre stat (MaxHP, MeleeDefense...). AVANT ce fix, lisait
    /// `stats.GetElementalPoints()` — le dictionnaire SCRATCH interne de CharacterStats,
    /// rempli uniquement par RecalculateStats (esprits/StatPoints) et jamais mis à jour par
    /// un buff — un buff ElementalPoint modifiait bien l'Entity (SetElementalPoints, via
    /// StatusEffectSystem) mais restait invisible ici et dans CombatSystem/l'UI (qui passent
    /// tous deux par GetEffectiveElementPoints → ce getter).</summary>
    public float GetElementPoints(ElementType element)
        => GetElementalPoints(element);

    // =========================================================
    // SETTERS ENTITY spécifiques au joueur
    // Les setters génériques (SetMaxHP, SetMeleeDefense...) sont sur Entity.
    // =========================================================

}
