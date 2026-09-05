using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CHARACTERPANELUI.CS — Panel personnage v2
// Path : Assets/Scripts/UI/CharacterPanelUI.cs
// AetherTree GDD v3.5
//
// Toutes les valeurs sont lues directement sur les propriétés
// publiques de Entity / Player / CharacterStats / StatPointSystem.
// Aucun stub fictif — tout est branché sur le vrai code.
//
// Panel_Detail — 4 sources par section :
//   Row_Base        → weapon/armor base rollée + CharacterData (HP/Mana/Regen)
//   Row_Equipment   → tout ce qui vient des équipements (armor,helmet,gloves,boots,jewelry,spirit,rune)
//   Row_StatPoints  → bonus StatPointSystem (linéaires + paliers)
//   Row_Passive     → skills permanents (unlockedPermanents)
// =============================================================

/// <summary>
/// Référence un prefab DetailRow instancié dans Panel_Detail.
/// Structure prefab :
///   Root (H. Layout) → Dot (Image) + SourceName (TMP) + Contributions (H. Layout)
///     └─ Contributions → Contrib_0…N → chaque Contrib = H. Layout avec Label TMP + Value TMP
/// Assigner depuis l'Inspector : root, sourceName, dot, valueLabels (les TMP Value dans l'ordre).
/// </summary>
[System.Serializable]
public class DetailRowUI
{
    public GameObject        root;
    public TextMeshProUGUI   sourceName;
    public Image             dot;
    /// <summary>TMP Value de chaque Contrib, dans l'ordre.</summary>
    public TextMeshProUGUI[] valueLabels;
}

public class CharacterPanelUI : MonoBehaviour
{
    public static CharacterPanelUI Instance { get; private set; }

    private Player          _player;
    private ElementalSystem _elemental;

    // =========================================================
    // HEADER JOUEUR
    // =========================================================
    [Header("Header joueur")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;
    public Slider          xpSlider;

    // =========================================================
    // VITALS HP / MANA
    // =========================================================
    [Header("Vitals HP / Mana")]
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI manaText;
    public Slider          hpSlider;
    public Slider          manaSlider;

    // =========================================================
    // ÉQUIPEMENTS — 10 slots stats + 2 cosm
    // =========================================================
    [Header("Equipment slots — Stats (10)")]
    public Image slotWeapon;
    public Image slotArmor;
    public Image slotHelmet;
    public Image slotGloves;
    public Image slotBoots;
    public Image slotRing;
    public Image slotNecklace;
    public Image slotBracelet;
    public Image slotSpirit;
    [FormerlySerializedAs("slotCard")]
    public Image slotTalisman;

    [Header("Equipment slots — Cosmétiques (2)")]
    public Image slotCosmeticHead;
    public Image slotCosmeticBody;

    [Tooltip("Sprite vide optionnel — grisé si null")]
    public Sprite emptySlotSprite;

    private static readonly Color EmptySlotColor = new Color(72, 72, 72, 0.05f);
    private static readonly Color CosmEmptyColor = new Color(72, 72, 72, 0.05f);

    // =========================================================
    // RÉPUTATION
    // =========================================================
    [Header("Réputation")]
    public TextMeshProUGUI worldReputationText;
    public TextMeshProUGUI pvpReputationText;
    public TextMeshProUGUI pvpKillsText;
    public TextMeshProUGUI pvpDeathsText;

    // =========================================================
    // TABS
    // =========================================================
    [Header("Tabs")]
    public GameObject panelResume;
    public GameObject panelDetail;
    public Button     tabResumeBtn;
    public Button     tabDetailBtn;

    // =========================================================
    // CARD ATTACK
    // =========================================================
    [Header("Card Attack")]
    public TextMeshProUGUI weaponNameText;
    public TooltipTrigger  weaponTooltipTrigger;
    public TextMeshProUGUI weaponDamageText;
    public TextMeshProUGUI weaponAccuracyText;
    public TextMeshProUGUI critChanceText;
    public TextMeshProUGUI critMultText;

    // =========================================================
    // CARD DEFENSE
    // =========================================================
    [Header("Card Defense")]
    public TextMeshProUGUI armorNameText;
    public TooltipTrigger  armorTooltipTrigger;
    public TextMeshProUGUI meleeDefenseText;
    public TextMeshProUGUI rangedDefenseText;
    public TextMeshProUGUI magicDefenseText;
    public TextMeshProUGUI dodgeText;
    public TextMeshProUGUI critDmgReductText;

    // =========================================================
    // CARD ELEMENTAL
    // =========================================================
    [Header("Card Elemental")]
    public TextMeshProUGUI elementActifText;
    public TextMeshProUGUI elementRankText;
    public TextMeshProUGUI elementAffinityText;
    public TextMeshProUGUI elementPtsText;
    public TextMeshProUGUI cooldownReductText;

    // =========================================================
    // CARD VITALITY
    // =========================================================
    [Header("Card Vitality")]
    public TextMeshProUGUI maxHPText;
    public TextMeshProUGUI maxMPText;
    public TextMeshProUGUI regenHPText;
    public TextMeshProUGUI regenMPText;

    // =========================================================
    // CARD RESISTANCES (8 éléments)
    // =========================================================
    [Header("Card Resistances")]
    public TextMeshProUGUI resistNeutralText;
    public TextMeshProUGUI resistFireText;
    public TextMeshProUGUI resistWaterText;
    public TextMeshProUGUI resistEarthText;
    public TextMeshProUGUI resistNatureText;
    public TextMeshProUGUI resistLightningText;
    public TextMeshProUGUI resistDarknessText;
    public TextMeshProUGUI resistLightText;

    // =========================================================
    // DETAIL — Section Attack
    // Contribs (5) : Min / Max / Précision / CritChance / CritDmg
    // =========================================================
    [Header("Detail · Section Attack")]
    public TextMeshProUGUI detailAttackTotal;
    public DetailRowUI     detailAttackBase;
    public DetailRowUI     detailAttackEquipment;
    public DetailRowUI     detailAttackStatPoints;
    public DetailRowUI     detailAttackPassive;

    // =========================================================
    // DETAIL — Section Defense
    // Contribs (5) : Mêlée / Distance / Magie / Dodge / CritDmgReduc
    // =========================================================
    [Header("Detail · Section Defense")]
    public TextMeshProUGUI detailDefenseTotal;
    public DetailRowUI     detailDefenseBase;
    public DetailRowUI     detailDefenseEquipment;
    public DetailRowUI     detailDefenseStatPoints;
    public DetailRowUI     detailDefensePassive;

    // =========================================================
    // DETAIL — Section Elemental
    // Contribs (2) : ElemPts / CDReduc
    // =========================================================
    [Header("Detail · Section Elemental")]
    public TextMeshProUGUI detailElementalTotal;
    public DetailRowUI     detailElementalBase;
    public DetailRowUI     detailElementalEquipment;
    public DetailRowUI     detailElementalStatPoints;
    public DetailRowUI     detailElementalPassive;

    // =========================================================
    // DETAIL — Section Vitality
    // Contribs (4) : HP / MP / RegenHP / RegenMP
    // =========================================================
    [Header("Detail · Section Vitality")]
    public TextMeshProUGUI detailVitalityTotal;
    public DetailRowUI     detailVitalityBase;
    public DetailRowUI     detailVitalityEquipment;
    public DetailRowUI     detailVitalityStatPoints;
    public DetailRowUI     detailVitalityPassive;

    // =========================================================
    // DETAIL — Section Resistances
    // Contribs (8) : Neutral/Fire/Water/Earth/Nature/Lightning/Darkness/Light
    // =========================================================
    [Header("Detail · Section Resistances")]
    public TextMeshProUGUI detailResistTotal;
    public DetailRowUI     detailResistBase;
    public DetailRowUI     detailResistEquipment;
    public DetailRowUI     detailResistStatPoints;
    public DetailRowUI     detailResistPassive;

    // =========================================================
    // FERMETURE
    // =========================================================
    [Header("Fermeture")]
    public Button closeButton;

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player    = FindObjectOfType<Player>();
        _elemental = _player?.GetComponent<ElementalSystem>();

        if (_player == null)
            Debug.LogWarning("[CharacterPanelUI] Player introuvable !");

        closeButton?.onClick.AddListener(Close);
        tabResumeBtn?.onClick.AddListener(() => ShowTab(true));
        tabDetailBtn?.onClick.AddListener(() => ShowTab(false));

        ShowTab(true);
        Resubscribe();
        gameObject.SetActive(false);
    }

    private void OnDestroy() => GameEventBus.OnStatsChanged -= OnStatsChangedHandler;

    public void Resubscribe()
    {
        GameEventBus.OnStatsChanged -= OnStatsChangedHandler;
        GameEventBus.OnStatsChanged += OnStatsChangedHandler;
    }

    private void OnStatsChangedHandler(StatsChangedEvent e) => Refresh();

    // =========================================================
    // TABS
    // =========================================================

    public void ShowTab(bool showResume)
    {
        panelResume?.SetActive(showResume);
        panelDetail?.SetActive(!showResume);
    }

    // =========================================================
    // REFRESH COMPLET
    // =========================================================

    public void Refresh()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;
        if (_elemental == null) _elemental = _player.GetComponent<ElementalSystem>();

        RefreshHeader();
        RefreshVitals();
        RefreshEquipmentSlots();
        RefreshReputation();

        RefreshCardAttack();
        RefreshCardDefense();
        RefreshCardElemental();
        RefreshCardVitality();
        RefreshCardResistances();

        RefreshDetailAttack();
        RefreshDetailDefense();
        RefreshDetailElemental();
        RefreshDetailVitality();
        RefreshDetailResistances();
    }

    // =========================================================
    // HEADER
    // =========================================================

    private void RefreshHeader()
    {
        SetText(nameText,  _player.entityName);
        SetText(titleText, _player.activeTitle);
        SetText(levelText, $"Lv. {_player.level}");
        SetText(xpText,    $"{_player.xpCombat} / {_player.xpToNextLevel} XP");

        if (xpSlider != null && _player.xpToNextLevel > 0)
            xpSlider.value = (float)_player.xpCombat / _player.xpToNextLevel;
    }

    // =========================================================
    // VITALS
    // =========================================================

    private void RefreshVitals()
    {
        SetText(hpText,   $"{Mathf.CeilToInt(_player.CurrentHP)} / {Mathf.CeilToInt(_player.MaxHP)}");
        SetText(manaText, $"{Mathf.CeilToInt(_player.CurrentMana)} / {Mathf.CeilToInt(_player.MaxMana)}");

        if (hpSlider   != null && _player.MaxHP   > 0f) hpSlider.value   = _player.HPPercent;
        if (manaSlider != null && _player.MaxMana > 0f) manaSlider.value = _player.ManaPercent;
    }

    // =========================================================
    // RÉPUTATION
    // =========================================================

    private void RefreshReputation()
    {
        SetText(worldReputationText, $"{_player.worldReputation}  (rang {_player.worldReputationRank})");
        SetText(pvpReputationText,   $"{_player.pvpReputation}  (rang {_player.pvpReputationRank})");

        var ac = _player.GetActivityCounter();
        if (ac != null)
        {
            SetText(pvpKillsText,  ac.Get("PVP_KILLS").ToString());
            SetText(pvpDeathsText, ac.Get("PVP_DEATHS").ToString());
        }
    }

    // =========================================================
    // CARD ATTACK
    // Valeurs finales lues sur Entity (post-RecalculateStats).
    // =========================================================

    private void RefreshCardAttack()
    {
        var w = _player.equippedWeaponInstance;
        SetText(weaponNameText,     w != null ? w.DisplayNameRich : "—");
        if (weaponTooltipTrigger != null)
        {
            if (w != null) weaponTooltipTrigger.SetItem(new InventoryItem(w));
            else weaponTooltipTrigger.Clear();
        }
        SetText(weaponDamageText,   $"{Mathf.RoundToInt(_player.AttackDamageMin)} – {Mathf.RoundToInt(_player.AttackDamageMax)}");
        SetText(weaponAccuracyText, $"{Mathf.RoundToInt(_player.Precision)}");
        SetText(critChanceText,     $"{_player.CritChance * 100f:F1}%");
        // CritMultiplier sur Entity = valeur finale poussée par RecalculateStats
        SetText(critMultText,       $"×{_player.CritMultiplier:F2}");
    }

    // =========================================================
    // CARD DEFENSE
    // =========================================================

    private void RefreshCardDefense()
    {
        var a = _player.equippedArmorInstance;
        SetText(armorNameText,     a?.data != null ? a.DisplayNameRich : "—");
        if (armorTooltipTrigger != null)
        {
            if (a?.data != null) armorTooltipTrigger.SetItem(new InventoryItem(a));
            else armorTooltipTrigger.Clear();
        }
        SetText(meleeDefenseText,  $"{Mathf.RoundToInt(_player.MeleeDefense)}");
        SetText(rangedDefenseText, $"{Mathf.RoundToInt(_player.RangedDefense)}");
        SetText(magicDefenseText,  $"{Mathf.RoundToInt(_player.MagicDefense)}");
        SetText(dodgeText,         $"{Mathf.RoundToInt(_player.Dodge)}");
        SetText(critDmgReductText, $"{_player.CritDamageReduction * 100f:F1}%");
    }

    // =========================================================
    // CARD ELEMENTAL
    // =========================================================

    private void RefreshCardElemental()
    {
        if (_elemental == null) return;

        ElementType dom      = _elemental.GetDominantElement();
        float       affinity = _elemental.GetAffinity(dom);
        int         rank     = _elemental.GetElementRank(dom);
        // Points élémentaires lus sur Entity (SetElementalPoints appelé par RecalculateStats)
        // Dans RefreshCardElemental et RefreshDetailElemental
        float ptsBase = _player.GetElementalPoints(dom);
        float ptsTotal = _elemental.GetEffectiveElementPoints(dom);

        if (elementActifText != null)
        {
            elementActifText.text  = dom.GetLabel();
            elementActifText.color = dom.GetColor();
        }

        SetText(elementRankText,     $"Rang {rank}");
        SetText(elementAffinityText, $"{affinity * 100f:F0}%");
        SetText(elementPtsText,      $"{Mathf.RoundToInt(ptsTotal)} pts");
        // cooldownReduction vit dans CharacterStats (non sur Entity — lue par SkillSystem)
        SetText(cooldownReductText,  $"{_player.stats.cooldownReduction * 100f:F1}%");
    }

    // =========================================================
    // CARD VITALITY
    // =========================================================

    private void RefreshCardVitality()
    {
        SetText(maxHPText,   $"{Mathf.CeilToInt(_player.MaxHP)}");
        SetText(maxMPText,   $"{Mathf.CeilToInt(_player.MaxMana)}");
        SetText(regenHPText, $"{_player.RegenHP:F1} /s");
        SetText(regenMPText, $"{_player.RegenMana:F1} /s");
    }

    // =========================================================
    // CARD RESISTANCES
    // Lues sur Entity.GetElementalResistance() — post-RecalculateStats.
    // =========================================================

    private void RefreshCardResistances()
    {
        SetResistText(resistNeutralText,   _player.GetElementalResistance(ElementType.Neutral));
        SetResistText(resistFireText,      _player.GetElementalResistance(ElementType.Fire));
        SetResistText(resistWaterText,     _player.GetElementalResistance(ElementType.Water));
        SetResistText(resistEarthText,     _player.GetElementalResistance(ElementType.Earth));
        SetResistText(resistNatureText,    _player.GetElementalResistance(ElementType.Nature));
        SetResistText(resistLightningText, _player.GetElementalResistance(ElementType.Lightning));
        SetResistText(resistDarknessText,  _player.GetElementalResistance(ElementType.Darkness));
        SetResistText(resistLightText,     _player.GetElementalResistance(ElementType.Light));
    }

    private void SetResistText(TextMeshProUGUI lbl, float value)
    {
        if (lbl == null) return;
        lbl.text  = $"{value * 100f:F0}%";
        lbl.color = value > 0f ? Color.green : value < 0f ? Color.red : Color.white;
    }

    // =========================================================
    // PANEL DÉTAIL
    // =========================================================

    // ── Section Attack ────────────────────────────────────────

    private void RefreshDetailAttack()
    {
        var w  = _player.equippedWeaponInstance;
        var sp = _player.statPoints;

        SetText(detailAttackTotal,
            $"{Mathf.RoundToInt(_player.AttackDamageMin)}–{Mathf.RoundToInt(_player.AttackDamageMax)}" +
            $"  Prec {Mathf.RoundToInt(_player.Precision)}" +
            $"  CritChance {_player.CritChance * 100f:F1}%" +
            $"  CritDamage{_player.CritMultiplier:F2}");

        // Row_Base — stats rollées arme seule (FinalDamageMin/Max depuis WeaponInstance)
        if (w != null)
            SetDetailRow(detailAttackBase, "Base",
                ("MIN",  $"{Mathf.RoundToInt(w.FinalDamageMin)}"),
                ("MAX",  $"{Mathf.RoundToInt(w.FinalDamageMax)}"),
                ("PREC", $"{Mathf.RoundToInt(w.FinalPrecision)}"),
                ("CRIT", $"{w.CritChance * 100f:F1}%"),
                ("×DMG", $"×{w.CritMultiplier:F2}"));
        else
            SetDetailRow(detailAttackBase, "Base",
                ("MIN","—"),("MAX","—"),("PREC","—"),("CRIT","—"),("×DMG","—"));

        // Row_Equipment — BonusAttack + Précision + CritChance + CritMult depuis config.bonuses
        var eAcc = new AtkAccum();
        AccumulateAttackBonuses(_player, eAcc);
        SetDetailRow(detailAttackEquipment, "Équipement",
            ("ATK",  FmtFlatPct(eAcc.atk, eAcc.atkPct)),
            ("PREC", eAcc.prec != 0f ? $"+{Mathf.RoundToInt(eAcc.prec)}" : "0"),
            ("CRIT", eAcc.crit != 0f ? $"+{eAcc.crit * 100f:F1}%"        : "0%"),
            ("×DMG", eAcc.critMult != 0f ? $"+{eAcc.critMult:F2}"        : "0"),
            ("",""));  // 5e contrib vide (colonnes alignées)

        // Row_StatPoints
        if (sp != null)
            SetDetailRow(detailAttackStatPoints, "Stat Points",
                ("ATK",  $"+{Mathf.RoundToInt(sp.linearBonusAttack + sp.bonusAttackFlat)}"),
                ("PREC", $"+{Mathf.RoundToInt(sp.bonusPrecision)}"),
                ("CRIT", $"+{sp.bonusCritChance * 100f:F1}%"),
                ("×DMG", $"+{sp.bonusCritMultiplier:F2}"),
                ("",""));
        else
            SetDetailRow(detailAttackStatPoints, "Stat Points",
                ("ATK","0"),("PREC","0"),("CRIT","0%"),("×DMG","0"),("",""));

        // Row_Passive — unlockedPermanents
        var pAcc = new AtkAccum();
        AccumulatePermanentAttack(_player, pAcc);
        SetDetailRow(detailAttackPassive, "Passifs",
            ("ATK",  FmtFlatPct(pAcc.atk, pAcc.atkPct)),
            ("PREC", pAcc.prec != 0f ? $"+{Mathf.RoundToInt(pAcc.prec)}" : "0"),
            ("CRIT", pAcc.crit != 0f ? $"+{pAcc.crit * 100f:F1}%"        : "0%"),
            ("×DMG", pAcc.critMult != 0f ? $"+{pAcc.critMult:F2}"        : "0"),
            ("",""));
    }

    // ── Section Defense ───────────────────────────────────────

    private void RefreshDetailDefense()
    {
        var  armor = _player.equippedArmorInstance;
        var  sp    = _player.statPoints;

        SetText(detailDefenseTotal,
            $"Mêl {Mathf.RoundToInt(_player.MeleeDefense)}" +
            $"  Dist {Mathf.RoundToInt(_player.RangedDefense)}" +
            $"  Mag {Mathf.RoundToInt(_player.MagicDefense)}" +
            $"  Esq {Mathf.RoundToInt(_player.Dodge)}" + 
            $"  CritDamageReduction {_player.CritDamageReduction * 100f:F1}%");

        // Row_Base — armure rollée seule
        float bMel = armor?.FinalMeleeDefense  ?? 0f;
        float bRng = armor?.FinalRangedDefense ?? 0f;
        float bMag = armor?.FinalMagicDefense  ?? 0f;
        float bDod = armor?.FinalDodge         ?? 0f;
        SetDetailRow(detailDefenseBase, "Base",
            ("MÊL",   $"{Mathf.RoundToInt(bMel)}"),
            ("DIST",  $"{Mathf.RoundToInt(bRng)}"),
            ("MAG",   $"{Mathf.RoundToInt(bMag)}"),
            ("ESQ",   $"{Mathf.RoundToInt(bDod)}"),
            ("C-R",   "0%"));

        // Row_Equipment — défenses fixes casque/gants/bottes/bijoux + config.bonuses
        var eAcc = new DefAccum();
        AccumulateDefenseBonuses(_player, eAcc);
        SetDetailRow(detailDefenseEquipment, "Équipement",
            ("MÊL",  FmtFlatPct(eAcc.mel, eAcc.melPct)),
            ("DIST", FmtFlatPct(eAcc.rng, eAcc.rngPct)),
            ("MAG",  FmtFlatPct(eAcc.mag, eAcc.magPct)),
            ("ESQ",  FmtFlatPct(eAcc.dod, eAcc.dodPct)),
            ("C-R",  eAcc.critR != 0f ? $"+{eAcc.critR * 100f:F1}%" : "0%"));

        // Row_StatPoints
        if (sp != null)
        {
            float spDef = sp.linearBonusDefense + sp.bonusDefenseFlat;
            SetDetailRow(detailDefenseStatPoints, "Stat Points",
                ("MÊL",  $"+{Mathf.RoundToInt(spDef)}"),
                ("DIST", $"+{Mathf.RoundToInt(spDef)}"),
                ("MAG",  $"+{Mathf.RoundToInt(spDef)}"),
                ("ESQ",  $"+{Mathf.RoundToInt(sp.bonusDodge)}"),
                ("C-R",  $"+{sp.bonusCritDmgReduction * 100f:F1}%"));
        }
        else
            SetDetailRow(detailDefenseStatPoints, "Stat Points",
                ("MÊL","0"),("DIST","0"),("MAG","0"),("ESQ","0"),("C-R","0%"));

        var pAcc = new DefAccum();
        AccumulatePermanentDefense(_player, pAcc);
        SetDetailRow(detailDefensePassive, "Passifs",
            ("MÊL",  FmtFlatPct(pAcc.mel, pAcc.melPct)),
            ("DIST", FmtFlatPct(pAcc.rng, pAcc.rngPct)),
            ("MAG",  FmtFlatPct(pAcc.mag, pAcc.magPct)),
            ("ESQ",  FmtFlatPct(pAcc.dod, pAcc.dodPct)),
            ("C-R",  pAcc.critR != 0f ? $"+{pAcc.critR * 100f:F1}%" : "0%"));
    }

    // ── Section Elemental ─────────────────────────────────────

    private void RefreshDetailElemental()
    {
        if (_elemental == null) return;

        ElementType dom  = _elemental.GetDominantElement();
        float       pts  = _player.GetElementalPoints(dom);
        float       aff  = _elemental.GetAffinity(dom);
        int         rank = _elemental.GetElementRank(dom);
        float       cdr  = _player.stats.cooldownReduction;

        SetText(detailElementalTotal,
            $"Element Actif : {_elemental.GetDominantElement()} " +
            $"ElementalPoints {Mathf.RoundToInt(_player.GetElementalPoints(dom))}" +
            $"  CD-Reduct {cdr * 100f:F1}%");

        // Row_Base — esprit actif (TotalElementalPoints = cumul de tous les niveaux)
        float spiritPts = 0f;
        if (_player.equippedSpiritInstances?.Count > 0)
        {
            var s = _player.equippedSpiritInstances[0];
            if (s?.data != null) spiritPts = s.TotalElementalPoints;
        }
        SetDetailRow(detailElementalBase, "Base",
            ("PTS", $"{Mathf.RoundToInt(spiritPts)}"),
            ("CDR", "0%"));

        // Row_Equipment — PointsFire/All depuis config.bonuses de tous les équipements
        float eqPts = 0f, eqCDR = 0f; // CDR non porté par équipements dans la codebase actuelle
        AccumulateElementalBonuses(_player, dom, ref eqPts, ref eqCDR);
        SetDetailRow(detailElementalEquipment, "Équipement",
            ("PTS", eqPts != 0f ? $"+{Mathf.RoundToInt(eqPts)}" : "0"),
            ("CDR", "0%"));

        // Row_StatPoints
        var sp = _player.statPoints;
        if (sp != null)
            SetDetailRow(detailElementalStatPoints, "Stat Points",
                ("PTS", $"+{Mathf.RoundToInt(sp.linearBonusElemental + sp.bonusElementalPoints)}"),
                ("CDR", $"+{sp.bonusCooldownReduction * 100f:F1}%"));
        else
            SetDetailRow(detailElementalStatPoints, "Stat Points", ("PTS","0"),("CDR","0%"));

        float pPts = 0f, pCDR = 0f;
        AccumulatePermanentElemental(_player, dom, ref pPts, ref pCDR);
        SetDetailRow(detailElementalPassive, "Passifs",
            ("PTS", pPts != 0f ? $"+{Mathf.RoundToInt(pPts)}" : "0"),
            ("CDR", pCDR != 0f ? $"+{pCDR * 100f:F1}%"        : "0%"));
    }

    // ── Section Vitality ──────────────────────────────────────

    private void RefreshDetailVitality()
    {
        SetText(detailVitalityTotal,
            $"HP Max {Mathf.CeilToInt(_player.MaxHP)}  MP Max {Mathf.CeilToInt(_player.MaxMana)}" +
            $"  Regen HP {_player.RegenHP:F1}  Regen MP {_player.RegenMana:F1}");

        // Row_Base — CharacterData base + progression niveau
        var   cd       = _player.characterData;
        int   lv       = Mathf.Max(1, _player.level);
        float baseHP   = cd != null ? cd.GetBaseHP  (_player.weaponCategory) + cd.GetHPPerLevel  (_player.weaponCategory) * (lv - 1) : 500f;
        float baseMana = cd != null ? cd.GetBaseMana(_player.weaponCategory) + cd.GetManaPerLevel(_player.weaponCategory) * (lv - 1) : 100f;
        float baseRgHP   = cd != null ? cd.baseRegenHP   : 1f;
        float baseRgMana = cd != null ? cd.baseRegenMana : 0.5f;
        SetDetailRow(detailVitalityBase, "Base",
            ("HP",   $"{Mathf.RoundToInt(baseHP)}"),
            ("MP",   $"{Mathf.RoundToInt(baseMana)}"),
            ("RgHP", $"{baseRgHP:F1}"),
            ("RgMP", $"{baseRgMana:F1}"));

        // Row_Equipment — BonusHP / BonusMana / BonusRegenHP / BonusRegenMana depuis config.bonuses
        float eHP = 0f, eMP = 0f, eRgHP = 0f, eRgMP = 0f;
        AccumulateVitalityBonuses(_player, ref eHP, ref eMP, ref eRgHP, ref eRgMP);
        SetDetailRow(detailVitalityEquipment, "Équipement",
            ("HP",   eHP   != 0f ? $"+{Mathf.RoundToInt(eHP)}"  : "0"),
            ("MP",   eMP   != 0f ? $"+{Mathf.RoundToInt(eMP)}"  : "0"),
            ("RgHP", eRgHP != 0f ? $"+{eRgHP:F1}"               : "0"),
            ("RgMP", eRgMP != 0f ? $"+{eRgMP:F1}"               : "0"));

        // Row_StatPoints
        var sp = _player.statPoints;
        if (sp != null)
            SetDetailRow(detailVitalityStatPoints, "Stat Points",
                ("HP",   $"+{Mathf.RoundToInt(sp.linearBonusHP + sp.bonusMaxHP)}"),
                ("MP",   $"+{Mathf.RoundToInt(sp.linearBonusMP + sp.bonusMaxMP)}"),
                ("RgHP", $"+{sp.bonusRegenHP:F1}"),
                ("RgMP", $"+{sp.bonusRegenMP:F1}"));
        else
            SetDetailRow(detailVitalityStatPoints, "Stat Points",
                ("HP","0"),("MP","0"),("RgHP","0"),("RgMP","0"));

        float pHP = 0f, pMP = 0f, pRgHP = 0f, pRgMP = 0f;
        AccumulatePermanentVitality(_player, ref pHP, ref pMP, ref pRgHP, ref pRgMP);
        SetDetailRow(detailVitalityPassive, "Passifs",
            ("HP",   pHP   != 0f ? $"+{Mathf.RoundToInt(pHP)}"  : "0"),
            ("MP",   pMP   != 0f ? $"+{Mathf.RoundToInt(pMP)}"  : "0"),
            ("RgHP", pRgHP != 0f ? $"+{pRgHP:F1}"               : "0"),
            ("RgMP", pRgMP != 0f ? $"+{pRgMP:F1}"               : "0"));
    }

    // ── Section Resistances ───────────────────────────────────

    private void RefreshDetailResistances()
    {
        SetText(detailResistTotal,
            $"    {_player.GetElementalResistance(ElementType.Neutral) * 100f:F0}%" +
            $"         {_player.GetElementalResistance(ElementType.Fire) * 100f:F0}%" +
            $"          {_player.GetElementalResistance(ElementType.Water) * 100f:F0}%" +
            $"         {_player.GetElementalResistance(ElementType.Lightning) * 100f:F0}%" +
            $"         {_player.GetElementalResistance(ElementType.Earth) * 100f:F0}%" +
            $"            {_player.GetElementalResistance(ElementType.Nature) * 100f:F0}%" +
            $"          {_player.GetElementalResistance(ElementType.Darkness) * 100f:F0}%" +
            $"         {_player.GetElementalResistance(ElementType.Light) * 100f:F0}%");

        // Row_Base — résistances propres des instances gants + bottes (fusionnées)
        var gl = _player.equippedGlovesInstance;
        var bo = _player.equippedBootsInstance;
        SetDetailRow(detailResistBase, "Base",
            ("Neutral", Fmt(GetBaseResist(gl, bo, ElementType.Neutral))),
            ("Fire", Fmt(GetBaseResist(gl, bo, ElementType.Fire))),
            ("Water", Fmt(GetBaseResist(gl, bo, ElementType.Water))),
            ("Lightning", Fmt(GetBaseResist(gl, bo, ElementType.Lightning))),
            ("Earth", Fmt(GetBaseResist(gl, bo, ElementType.Earth))),
            ("Nature", Fmt(GetBaseResist(gl, bo, ElementType.Nature))),

            ("Darkness", Fmt(GetBaseResist(gl, bo, ElementType.Darkness))),
            ("Light", Fmt(GetBaseResist(gl, bo, ElementType.Light))));

        // Row_Equipment — ResistXxx / ResistAll depuis config.bonuses
        var eqR = new Dictionary<ElementType, float>();
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType))) eqR[e] = 0f;
        AccumulateEquipmentResist(_player, eqR);
        SetDetailRow(detailResistEquipment, "Équipement",
            ("Neutral", FmtPlus(eqR[ElementType.Neutral])),
            ("Fire", FmtPlus(eqR[ElementType.Fire])),
            ("Water", FmtPlus(eqR[ElementType.Water])),
            ("Lightning", FmtPlus(eqR[ElementType.Lightning])),
            ("Earth", FmtPlus(eqR[ElementType.Earth])),
            ("Nature", FmtPlus(eqR[ElementType.Nature])),
            ("Darkness", FmtPlus(eqR[ElementType.Darkness])),
            ("Light", FmtPlus(eqR[ElementType.Light])));

        // Row_StatPoints — TotalResistAllFromPoints s'applique à tous les éléments
        var  sp  = _player.statPoints;
        float spR = sp != null ? sp.TotalResistAllFromPoints : 0f;
        SetDetailRow(detailResistStatPoints, "Stat Points",
            ("Neutral", FmtPlus(spR)), 
            ("Fire", FmtPlus(spR)), 
            ("Water", FmtPlus(spR)),
            ("Lightning", FmtPlus(spR)),
            ("Earth", FmtPlus(spR)), 
            ("Nature", FmtPlus(spR)), 
            ("Darkness", FmtPlus(spR)), 
            ("Light", FmtPlus(spR)));

        var pR = new Dictionary<ElementType, float>();
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType))) pR[e] = 0f;
        AccumulatePermanentResist(_player, pR);
        SetDetailRow(detailResistPassive, "Passifs",
            ("Neutral", FmtPlus(pR[ElementType.Neutral])),
            ("Fire", FmtPlus(pR[ElementType.Fire])),
            ("Water", FmtPlus(pR[ElementType.Water])),
            ("Lightning", FmtPlus(pR[ElementType.Lightning])),
            ("Earth", FmtPlus(pR[ElementType.Earth])),
            ("Nature", FmtPlus(pR[ElementType.Nature])),
            ("Darkness", FmtPlus(pR[ElementType.Darkness])),
            ("Light", FmtPlus(pR[ElementType.Light])));
    }

    // =========================================================
    // ACCUMULATEURS — lisent config.bonuses source par source
    // =========================================================

    /// <summary>Accumulateur Row_Equipment/Row_Passive — section Attaque. Sépare Flat et Percent
    /// par stat (mirroring CharacterStats.AddToAcc/FinalOf, en display-only : ce panel ne
    /// recompose pas le (Base+Flat)×(1+Percent) final, juste la contribution brute PAR
    /// CATÉGORIE, voir FmtFlatPct). Precision/CritChance/CritMultiplier restent en addition pure
    /// — seul BonusAttack a besoin de la distinction Flat/Percent ici (voir Fix 6 finding).</summary>
    private class AtkAccum
    {
        public float atk, atkPct;
        public float prec, crit, critMult;
    }

    /// <summary>Idem AtkAccum pour la section Défense. AllDefense (voir ReadDefList) fan-out sur
    /// les 3 champs mel/rng/mag — jamais un bucket séparé, même contrat que
    /// CharacterStats.AccumulateBonus. CritDmgReduction reste en addition pure.</summary>
    private class DefAccum
    {
        public float mel, melPct;
        public float rng, rngPct;
        public float mag, magPct;
        public float dod, dodPct;
        public float critR;
    }

    private static void AccumulateAttackBonuses(Player p, AtkAccum acc)
    {
        // On lit config.bonuses de tous les équipements (hors stats rollées de l'arme)
        ReadAtkList(p.equippedWeaponInstance?.Bonuses,  acc);
        ReadAtkList(p.equippedArmorInstance?.Bonuses,   acc);
        ReadAtkList(p.equippedHelmetInstance?.Bonuses,  acc);
        ReadAtkList(p.equippedGlovesInstance?.Bonuses,  acc);
        ReadAtkList(p.equippedBootsInstance?.Bonuses,   acc);
        if (p.equippedJewelryInstances != null)
            foreach (var j in p.equippedJewelryInstances) ReadAtkList(j?.Bonuses, acc);
        if (p.equippedSpiritInstances != null)
            foreach (var s in p.equippedSpiritInstances)
            {
                ReadAtkList(s?.Bonuses, acc);
                if (s?.data != null)
                    for (int lv = 1; lv <= s.level; lv++)
                    {
                        var ms = s.data.GetMilestone(lv);
                        if (ms != null) ReadAtkList(ms.bonuses, acc);
                    }
            }
        ReadAtkList(p.equippedWeaponInstance?.equippedRune?.bonuses, acc);
        ReadAtkList(p.equippedArmorInstance?.equippedRune?.bonuses,  acc);
    }

    private static void ReadAtkList(List<StatBonus> bonuses, AtkAccum acc)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            switch (b.statType)
            {
                case StatType.BonusAttack:
                    if (b.mode == ModifierType.Percent) acc.atkPct += b.value;
                    else                                acc.atk    += b.value;
                    break;
                case StatType.Precision:      acc.prec     += b.value; break;
                case StatType.CritChance:     acc.crit     += b.value; break;
                case StatType.CritMultiplier: acc.critMult += b.value; break;
            }
    }

    private static void AccumulateDefenseBonuses(Player p, DefAccum acc)
    {
        // Défenses fixes sur instances (casque, gants, bottes, bijoux) — toujours flat, pas de mode
        if (p.equippedHelmetInstance != null) { acc.mel += p.equippedHelmetInstance.MeleeDefense; acc.rng += p.equippedHelmetInstance.RangedDefense; acc.mag += p.equippedHelmetInstance.MagicDefense; }
        if (p.equippedGlovesInstance != null) { acc.mel += p.equippedGlovesInstance.MeleeDefense; acc.rng += p.equippedGlovesInstance.RangedDefense; acc.mag += p.equippedGlovesInstance.MagicDefense; }
        if (p.equippedBootsInstance  != null) { acc.mel += p.equippedBootsInstance.MeleeDefense;  acc.rng += p.equippedBootsInstance.RangedDefense;  acc.mag += p.equippedBootsInstance.MagicDefense; }
        if (p.equippedJewelryInstances != null)
            foreach (var j in p.equippedJewelryInstances)
            { if (j == null) continue; acc.mel += j.MeleeDefense; acc.rng += j.RangedDefense; acc.mag += j.MagicDefense; }

        // config.bonuses défense sur tous les équipements
        ReadDefList(p.equippedWeaponInstance?.Bonuses, acc);
        ReadDefList(p.equippedArmorInstance?.Bonuses,  acc);
        ReadDefList(p.equippedHelmetInstance?.Bonuses, acc);
        ReadDefList(p.equippedGlovesInstance?.Bonuses, acc);
        ReadDefList(p.equippedBootsInstance?.Bonuses,  acc);
        if (p.equippedJewelryInstances != null)
            foreach (var j in p.equippedJewelryInstances) ReadDefList(j?.Bonuses, acc);
        if (p.equippedSpiritInstances != null)
            foreach (var s in p.equippedSpiritInstances)
            {
                ReadDefList(s?.Bonuses, acc);
                if (s?.data != null)
                    for (int lv = 1; lv <= s.level; lv++)
                    {
                        var ms = s.data.GetMilestone(lv);
                        if (ms != null) ReadDefList(ms.bonuses, acc);
                    }
            }
        ReadDefList(p.equippedWeaponInstance?.equippedRune?.bonuses, acc);
        ReadDefList(p.equippedArmorInstance?.equippedRune?.bonuses,  acc);
    }

    private static void ReadDefList(List<StatBonus> bonuses, DefAccum acc)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            switch (b.statType)
            {
                case StatType.MeleeDefense:
                    if (b.mode == ModifierType.Percent) acc.melPct += b.value; else acc.mel += b.value;
                    break;
                case StatType.RangedDefense:
                    if (b.mode == ModifierType.Percent) acc.rngPct += b.value; else acc.rng += b.value;
                    break;
                case StatType.MagicDefense:
                    if (b.mode == ModifierType.Percent) acc.magPct += b.value; else acc.mag += b.value;
                    break;
                case StatType.AllDefense:
                    // Jamais stockée comme cible finale elle-même — répartie sur les 3 défenses
                    // réelles, même contrat que CharacterStats.AccumulateBonus.
                    if (b.mode == ModifierType.Percent)
                    { acc.melPct += b.value; acc.rngPct += b.value; acc.magPct += b.value; }
                    else
                    { acc.mel += b.value; acc.rng += b.value; acc.mag += b.value; }
                    break;
                case StatType.Dodge:
                    if (b.mode == ModifierType.Percent) acc.dodPct += b.value; else acc.dod += b.value;
                    break;
                case StatType.CritDmgReduction: acc.critR += b.value; break;
            }
    }

    private static void AccumulateElementalBonuses(Player p, ElementType dom,
        ref float pts, ref float cdr)
    {
        StatType domType = ElementToPtsStatType(dom);
        var all = BuildAllBonusLists(p);
        foreach (var list in all)
        {
            if (list == null) continue;
            foreach (var b in list)
                if (b.statType == StatType.PointsAll || b.statType == domType)
                    pts += b.value;
        }
    }

    private static void AccumulateVitalityBonuses(Player p,
        ref float hp, ref float mp, ref float rgHP, ref float rgMP)
    {
        var all = BuildAllBonusLists(p);
        foreach (var list in all)
        {
            if (list == null) continue;
            foreach (var b in list)
                switch (b.statType)
                {
                    case StatType.BonusHP:        hp   += b.value; break;
                    case StatType.BonusMana:      mp   += b.value; break;
                    case StatType.BonusRegenHP:   rgHP += b.value; break;
                    case StatType.BonusRegenMana: rgMP += b.value; break;
                }
        }
    }

    private static void AccumulateEquipmentResist(Player p, Dictionary<ElementType, float> r)
    {
        var all = BuildAllBonusLists(p);
        foreach (var list in all)
        {
            if (list == null) continue;
            foreach (var b in list)
                switch (b.statType)
                {
                    case StatType.ResistFire:      r[ElementType.Fire]      += b.value; break;
                    case StatType.ResistWater:     r[ElementType.Water]     += b.value; break;
                    case StatType.ResistEarth:     r[ElementType.Earth]     += b.value; break;
                    case StatType.ResistNature:    r[ElementType.Nature]    += b.value; break;
                    case StatType.ResistLightning: r[ElementType.Lightning] += b.value; break;
                    case StatType.ResistDarkness:  r[ElementType.Darkness]  += b.value; break;
                    case StatType.ResistLight:     r[ElementType.Light]     += b.value; break;
                    case StatType.ResistAll:
                        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                            r[e] += b.value;
                        break;
                }
        }
    }

    /// <summary>Construit la liste de toutes les config.bonuses de l'équipement du joueur.</summary>
    private static List<List<StatBonus>> BuildAllBonusLists(Player p)
    {
        var all = new List<List<StatBonus>>();
        all.Add(p.equippedWeaponInstance?.Bonuses);
        all.Add(p.equippedArmorInstance?.Bonuses);
        all.Add(p.equippedHelmetInstance?.Bonuses);
        all.Add(p.equippedGlovesInstance?.Bonuses);
        all.Add(p.equippedBootsInstance?.Bonuses);
        if (p.equippedJewelryInstances != null)
            foreach (var j in p.equippedJewelryInstances) all.Add(j?.Bonuses);
        if (p.equippedSpiritInstances != null)
            foreach (var s in p.equippedSpiritInstances)
            {
                all.Add(s?.Bonuses);
                if (s?.data != null)
                    for (int lv = 1; lv <= s.level; lv++)
                    {
                        var ms = s.data.GetMilestone(lv);
                        if (ms != null) all.Add(ms.bonuses);
                    }
            }
        all.Add(p.equippedWeaponInstance?.equippedRune?.bonuses);
        all.Add(p.equippedArmorInstance?.equippedRune?.bonuses);
        return all;
    }

    private static float GetBaseResist(GlovesInstance gl, BootsInstance bo, ElementType e)
        => (gl?.GetResistance(e) ?? 0f) + (bo?.GetResistance(e) ?? 0f);

    // ── Permanents ────────────────────────────────────────────

    private static void AccumulatePermanentAttack(Player p, AtkAccum acc)
    {
        if (p.unlockedPermanents == null) return;
        foreach (var perm in p.unlockedPermanents)
            ReadAtkList(perm?.bonuses, acc);
    }

    private static void AccumulatePermanentDefense(Player p, DefAccum acc)
    {
        if (p.unlockedPermanents == null) return;
        foreach (var perm in p.unlockedPermanents)
            ReadDefList(perm?.bonuses, acc);
    }

    private static void AccumulatePermanentElemental(Player p, ElementType dom,
        ref float pts, ref float cdr)
    {
        if (p.unlockedPermanents == null) return;
        StatType domType = ElementToPtsStatType(dom);
        foreach (var perm in p.unlockedPermanents)
        {
            if (perm?.bonuses == null) continue;
            foreach (var b in perm.bonuses)
                if (b.statType == StatType.PointsAll || b.statType == domType)
                    pts += b.value;
        }
    }

    private static void AccumulatePermanentVitality(Player p,
        ref float hp, ref float mp, ref float rgHP, ref float rgMP)
    {
        if (p.unlockedPermanents == null) return;
        foreach (var perm in p.unlockedPermanents)
        {
            if (perm?.bonuses == null) continue;
            foreach (var b in perm.bonuses)
                switch (b.statType)
                {
                    case StatType.BonusHP:        hp   += b.value; break;
                    case StatType.BonusMana:      mp   += b.value; break;
                    case StatType.BonusRegenHP:   rgHP += b.value; break;
                    case StatType.BonusRegenMana: rgMP += b.value; break;
                }
        }
    }

    private static void AccumulatePermanentResist(Player p, Dictionary<ElementType, float> r)
    {
        if (p.unlockedPermanents == null) return;
        foreach (var perm in p.unlockedPermanents)
        {
            if (perm?.bonuses == null) continue;
            foreach (var b in perm.bonuses)
                switch (b.statType)
                {
                    case StatType.ResistFire:      r[ElementType.Fire]      += b.value; break;
                    case StatType.ResistWater:     r[ElementType.Water]     += b.value; break;
                    case StatType.ResistEarth:     r[ElementType.Earth]     += b.value; break;
                    case StatType.ResistNature:    r[ElementType.Nature]    += b.value; break;
                    case StatType.ResistLightning: r[ElementType.Lightning] += b.value; break;
                    case StatType.ResistDarkness:  r[ElementType.Darkness]  += b.value; break;
                    case StatType.ResistLight:     r[ElementType.Light]     += b.value; break;
                    case StatType.ResistAll:
                        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                            r[e] += b.value;
                        break;
                }
        }
    }

    private static StatType ElementToPtsStatType(ElementType e)
    {
        switch (e)
        {
            case ElementType.Fire:      return StatType.PointsFire;
            case ElementType.Water:     return StatType.PointsWater;
            case ElementType.Lightning: return StatType.PointsLightning;
            case ElementType.Earth:     return StatType.PointsEarth;
            case ElementType.Nature:    return StatType.PointsNature;
            case ElementType.Darkness:  return StatType.PointsDarkness;
            case ElementType.Light:     return StatType.PointsLight;
            default:                    return StatType.PointsAll;
        }
    }

    // =========================================================
    // ÉQUIPEMENTS
    // =========================================================

    private void RefreshEquipmentSlots()
    {
        if (_player == null) return;

        SetSlotIcon(slotWeapon,  _player.equippedWeaponInstance?.data  != null ? _player.equippedWeaponInstance.Icon  : null, false);
        SetSlotIcon(slotArmor,   _player.equippedArmorInstance?.data   != null ? _player.equippedArmorInstance.Icon   : null, false);
        SetSlotIcon(slotHelmet,  _player.equippedHelmetInstance?.data  != null ? _player.equippedHelmetInstance.Icon  : null, false);
        SetSlotIcon(slotGloves,  _player.equippedGlovesInstance?.data  != null ? _player.equippedGlovesInstance.Icon  : null, false);
        SetSlotIcon(slotBoots,   _player.equippedBootsInstance?.data   != null ? _player.equippedBootsInstance.Icon   : null, false);
        SetSlotIcon(slotSpirit,  _player.equippedSpiritInstances?.Count > 0 && _player.equippedSpiritInstances[0]?.data != null
                                 ? _player.equippedSpiritInstances[0].Icon : null, false);

        Sprite ring = null, necklace = null, bracelet = null;
        if (_player.equippedJewelryInstances != null)
            foreach (var j in _player.equippedJewelryInstances)
            {
                if (j == null) continue;
                if (j.Slot == JewelrySlot.Ring)     ring     = j.Icon;
                if (j.Slot == JewelrySlot.Necklace) necklace = j.Icon;
                if (j.Slot == JewelrySlot.Bracelet) bracelet = j.Icon;
            }
        SetSlotIcon(slotRing,     ring,     false);
        SetSlotIcon(slotNecklace, necklace, false);
        SetSlotIcon(slotBracelet, bracelet, false);

        SetSlotIcon(slotTalisman, _player.equippedTalismanInstance?.data != null ? _player.equippedTalismanInstance.Icon : null, false);

        // Cosmétiques — brancher equippedCosmeticHead / Body quand disponible sur Player
        SetSlotIcon(slotCosmeticHead, _player.equippedCosmeticHeadInstance?.data != null ? _player.equippedCosmeticHeadInstance.Icon : null, true);
        SetSlotIcon(slotCosmeticBody, _player.equippedCosmeticBodyInstance?.data != null ? _player.equippedCosmeticBodyInstance.Icon : null, true);

        // Tooltips
        slotWeapon?.GetComponent<TooltipTrigger>()?.SetItem(_player.equippedWeaponInstance  != null ? new InventoryItem(_player.equippedWeaponInstance)  : null);
        slotArmor?.GetComponent<TooltipTrigger>()?.SetItem( _player.equippedArmorInstance   != null ? new InventoryItem(_player.equippedArmorInstance)   : null);
        slotHelmet?.GetComponent<TooltipTrigger>()?.SetItem(_player.equippedHelmetInstance  != null ? new InventoryItem(_player.equippedHelmetInstance)  : null);
        slotGloves?.GetComponent<TooltipTrigger>()?.SetItem(_player.equippedGlovesInstance  != null ? new InventoryItem(_player.equippedGlovesInstance)  : null);
        slotBoots?.GetComponent<TooltipTrigger>()?.SetItem( _player.equippedBootsInstance   != null ? new InventoryItem(_player.equippedBootsInstance)   : null);
        if (_player.equippedSpiritInstances?.Count > 0)
            slotSpirit?.GetComponent<TooltipTrigger>()?.SetItem(new InventoryItem(_player.equippedSpiritInstances[0]));
        if (_player.equippedJewelryInstances != null)
            foreach (var j in _player.equippedJewelryInstances)
            {
                if (j == null) continue;
                if (j.Slot == JewelrySlot.Ring)     slotRing?.GetComponent<TooltipTrigger>()?.SetItem(new InventoryItem(j));
                if (j.Slot == JewelrySlot.Necklace) slotNecklace?.GetComponent<TooltipTrigger>()?.SetItem(new InventoryItem(j));
                if (j.Slot == JewelrySlot.Bracelet) slotBracelet?.GetComponent<TooltipTrigger>()?.SetItem(new InventoryItem(j));
            }
    }

    private void SetSlotIcon(Image img, Sprite icon, bool isCosmetic)
    {
        if (img == null) return;
        img.enabled = true;
        img.sprite  = icon ?? emptySlotSprite;
        img.color   = icon != null ? Color.white : isCosmetic ? CosmEmptyColor : EmptySlotColor;
    }

    // =========================================================
    // TOGGLE PANEL
    // =========================================================

    public void Toggle() { bool n = !gameObject.activeSelf; gameObject.SetActive(n); if (n) Refresh(); }
    public void Open()   { gameObject.SetActive(true);  Refresh(); }
    public void Close()  { gameObject.SetActive(false); }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private void SetDetailRow(DetailRowUI row, string source,
                              params (string lbl, string val)[] entries)
    {
        if (row?.root == null) return;
        SetText(row.sourceName, source);
        if (row.valueLabels == null) return;
        for (int i = 0; i < row.valueLabels.Length; i++)
        {
            if (row.valueLabels[i] == null) continue;
            row.valueLabels[i].text = i < entries.Length && entries[i].lbl != ""
                ? $"<color=#6a7592>{entries[i].lbl}</color>  {entries[i].val}"
                : "";
        }
    }

    private void SetText(TextMeshProUGUI lbl, string value)
    {
        if (lbl != null) lbl.text = value ?? "";
    }

    private static string Fmt(float v)     => $"{v * 100f:F0}%";
    private static string FmtPlus(float v) => v != 0f ? $"+{v * 100f:F0}%" : "0%";

    /// <summary>Formate une contribution Flat+Percent pour une même stat dans une row de
    /// breakdown (Row_Equipment/Row_Passive) — un bonus Percent ne doit JAMAIS s'afficher comme
    /// s'il était Flat (voir Fix 5/6 review finale). `flat` : nombre entier direct (ex: +15
    /// MeleeDefense) ; `pct` : décimal (0.10 = +10%), affiché entre parenthèses à côté du flat
    /// s'il y en a un, seul sinon. Les deux à 0 → "0".</summary>
    private static string FmtFlatPct(float flat, float pct)
    {
        bool hasFlat = flat != 0f;
        bool hasPct  = pct  != 0f;
        if (!hasFlat && !hasPct) return "0";
        string flatStr = $"+{Mathf.RoundToInt(flat)}";
        string pctStr  = $"+{pct * 100f:F0}%";
        if (hasFlat && hasPct) return $"{flatStr} ({pctStr})";
        return hasFlat ? flatStr : pctStr;
    }
}

// StatsChangedEvent défini dans GameEvents.cs
