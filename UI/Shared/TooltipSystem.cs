using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// TOOLTIPSYSTEM — Tooltip centralisé au survol
// Path : Assets/Scripts/UI/UtilsUI/TooltipSystem.cs
// AetherTree GDD v31
//
// v31 : Ajout champ "Niveau requis" sur tous les panels équipement
//   → Blanc  = joueur a le niveau requis
//   → Rouge  = niveau insuffisant
//   Nécessite requiredLevel sur chaque SO (cf. EquipmentLevel_Patch.cs)
// =============================================================

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [Header("Tooltip — Racine")]
    public GameObject    tooltipPanel;
    public RectTransform tooltipRect;

    // ① WEAPON
    [Header("① Weapon Panel")]
    public GameObject      weaponPanel;
    public Image           weaponIcon;
    public TextMeshProUGUI weaponNameText;
    public TextMeshProUGUI weaponLevelText;       // ← NOUVEAU v31
    public TextMeshProUGUI weaponRarityText;
    public TextMeshProUGUI weaponTypeText;
    public TextMeshProUGUI weaponDamageText;
    public TextMeshProUGUI weaponPrecisionText;
    public TextMeshProUGUI weaponSpeedText;
    public TextMeshProUGUI weaponCritText;
    public TextMeshProUGUI weaponUpgradeText;
    public TextMeshProUGUI weaponRuneText;
    public GameObject      weaponBonusSection;
    public TextMeshProUGUI weaponBonusText;
    public GameObject      weaponStatusSection;
    public TextMeshProUGUI weaponStatusText;
    public GameObject      weaponResistSection;
    public TextMeshProUGUI weaponResistText;
    public GameObject      weaponOnHitSection;
    public TextMeshProUGUI weaponOnHitText;
    public TextMeshProUGUI weaponDescText;

    // ② ARMOR
    [Header("② Armor Panel")]
    public GameObject      armorPanel;
    public Image           armorIcon;
    public TextMeshProUGUI armorNameText;
    public TextMeshProUGUI armorLevelText;        // ← NOUVEAU v31
    public TextMeshProUGUI armorRarityText;
    public TextMeshProUGUI armorTypeText;
    public TextMeshProUGUI armorMeleeText;
    public TextMeshProUGUI armorRangedText;
    public TextMeshProUGUI armorMagicText;
    public TextMeshProUGUI armorDodgeText;
    public TextMeshProUGUI armorUpgradeText;
    public TextMeshProUGUI armorRuneText;
    public GameObject      armorBonusSection;
    public TextMeshProUGUI armorBonusText;
    public GameObject      armorStatusSection;
    public TextMeshProUGUI armorStatusText;
    public GameObject      armorResistSection;
    public TextMeshProUGUI armorResistText;
    public GameObject      armorOnHitSection;
    public TextMeshProUGUI armorOnHitText;
    public TextMeshProUGUI armorDescText;

    // ③ HELMET
    [Header("③ Helmet Panel")]
    public GameObject      helmetPanel;
    public Image           helmetIcon;
    public TextMeshProUGUI helmetNameText;
    public TextMeshProUGUI helmetLevelText;       // ← NOUVEAU v31
    public GameObject      helmetBonusSection;
    public TextMeshProUGUI helmetBonusText;
    public GameObject      helmetStatusSection;
    public TextMeshProUGUI helmetStatusText;
    public GameObject      helmetResistSection;
    public TextMeshProUGUI helmetResistText;
    public GameObject      helmetOnHitSection;
    public TextMeshProUGUI helmetOnHitText;
    public TextMeshProUGUI helmetDescText;

    // ④ GLOVES
    [Header("④ Gloves Panel")]
    public GameObject      glovesPanel;
    public Image           glovesIcon;
    public TextMeshProUGUI glovesNameText;
    public TextMeshProUGUI glovesLevelText;       // ← NOUVEAU v31
    public TextMeshProUGUI glovesFusionText;
    public TextMeshProUGUI glovesMeleeText;
    public TextMeshProUGUI glovesRangedText;
    public TextMeshProUGUI glovesMagicText;
    public GameObject      glovesResistElemSection;
    public TextMeshProUGUI glovesResistElemText;
    public GameObject      glovesBonusSection;
    public TextMeshProUGUI glovesBonusText;
    public GameObject      glovesStatusSection;
    public TextMeshProUGUI glovesStatusText;
    public GameObject      glovesDebuffResistSection;
    public TextMeshProUGUI glovesDebuffResistText;
    public GameObject      glovesOnHitSection;
    public TextMeshProUGUI glovesOnHitText;
    public TextMeshProUGUI glovesDescText;

    // ⑤ BOOTS
    [Header("⑤ Boots Panel")]
    public GameObject      bootsPanel;
    public Image           bootsIcon;
    public TextMeshProUGUI bootsNameText;
    public TextMeshProUGUI bootsLevelText;        // ← NOUVEAU v31
    public TextMeshProUGUI bootsFusionText;
    public TextMeshProUGUI bootsMeleeText;
    public TextMeshProUGUI bootsRangedText;
    public TextMeshProUGUI bootsMagicText;
    public GameObject      bootsResistElemSection;
    public TextMeshProUGUI bootsResistElemText;
    public GameObject      bootsBonusSection;
    public TextMeshProUGUI bootsBonusText;
    public GameObject      bootsStatusSection;
    public TextMeshProUGUI bootsStatusText;
    public GameObject      bootsDebuffResistSection;
    public TextMeshProUGUI bootsDebuffResistText;
    public GameObject      bootsOnHitSection;
    public TextMeshProUGUI bootsOnHitText;
    public TextMeshProUGUI bootsDescText;

    // ⑥ JEWELRY
    [Header("⑥ Jewelry Panel")]
    public GameObject      jewelryPanel;
    public Image           jewelryIcon;
    public TextMeshProUGUI jewelryNameText;
    public TextMeshProUGUI jewelryLevelText;      // ← NOUVEAU v31 (niveau requis pour équiper)
    public TextMeshProUGUI jewelrySlotText;
    public TextMeshProUGUI jewelryItemLevelText;  // niveau interne du bijou (JewelryLevel)
    public TextMeshProUGUI jewelryMeleeText;
    public TextMeshProUGUI jewelryRangedText;
    public TextMeshProUGUI jewelryMagicText;
    public TextMeshProUGUI jewelryGemsText;
    public GameObject      jewelryBonusSection;
    public TextMeshProUGUI jewelryBonusText;
    public GameObject      jewelryStatusSection;
    public TextMeshProUGUI jewelryStatusText;
    public GameObject      jewelryResistSection;
    public TextMeshProUGUI jewelryResistText;
    public GameObject      jewelryOnHitSection;
    public TextMeshProUGUI jewelryOnHitText;
    public TextMeshProUGUI jewelryDescText;

    // ⑦ SPIRIT
    [Header("⑦ Spirit Panel")]
    public GameObject      spiritPanel;
    public Image           spiritIcon;
    public TextMeshProUGUI spiritNameText;
    public TextMeshProUGUI spiritLevelReqText;    // ← NOUVEAU v31 (niveau requis)
    public TextMeshProUGUI spiritElementText;
    public TextMeshProUGUI spiritLevelText;
    public TextMeshProUGUI spiritPointsText;
    public TextMeshProUGUI spiritXPText;
    public TextMeshProUGUI spiritDescText;

    // ⑧ RUNE
    [Header("⑧ Rune Panel")]
    public GameObject      runePanel;
    public Image           runeIcon;
    public TextMeshProUGUI runeNameText;
    public TextMeshProUGUI runeLevelReqText;      // ← NOUVEAU v31 (niveau requis pour insérer)
    public TextMeshProUGUI runeCategoryText;
    public TextMeshProUGUI runeLevelText;
    public TextMeshProUGUI runeRarityText;
    public TextMeshProUGUI runeStatsText;
    public TextMeshProUGUI runeIdentifiedText;
    public TextMeshProUGUI runeDescText;

    // ⑨ GEM
    [Header("⑨ Gem Panel")]
    public GameObject      gemPanel;
    public Image           gemIcon;
    public TextMeshProUGUI gemNameText;
    public TextMeshProUGUI gemLevelText;
    public TextMeshProUGUI gemStatText;
    public TextMeshProUGUI gemDescText;

    // ⑩ SKILL
    [Header("⑩ Skill Panel")]
    public GameObject      skillPanel;
    public Image           skillIcon;
    public TextMeshProUGUI skillNameText;
    public TextMeshProUGUI skillTypeText;
    public TextMeshProUGUI skillCooldownText;
    public TextMeshProUGUI skillManaCostText;
    public TextMeshProUGUI skillDamageText;
    public TextMeshProUGUI skillElementText;
    public TextMeshProUGUI skillDescText;

    // ⑭ PERMANENT SKILL (bonus permanent débloquable) — simple : icône+nom+description,
    // le texte explicatif (rédigé par le designer) porte l'effet, pas de breakdown auto.
    [Header("⑭ Permanent Skill Panel")]
    public GameObject      permanentSkillPanel;
    public Image           permanentSkillIcon;
    public TextMeshProUGUI permanentSkillNameText;
    public TextMeshProUGUI permanentSkillDescText;

    // ⑮ PASSIVE SKILL (proc conditionnel — trigger + effets très variables selon l'asset,
    // ex: "regen 500 HP tous les 10 coups", "invulnérable 5s après un coup mortel"...) —
    // même choix que Permanent : icône+nom+description, pas de breakdown générique possible
    // vu la diversité des combinaisons trigger/effets.
    [Header("⑮ Passive Skill Panel")]
    public GameObject      passiveSkillPanel;
    public Image           passiveSkillIcon;
    public TextMeshProUGUI passiveSkillNameText;
    public TextMeshProUGUI passiveSkillDescText;

    // ⑪ CONSUMABLE
    [Header("⑪ Consumable Panel")]
    public GameObject      consumablePanel;
    public Image           consumableIcon;
    public TextMeshProUGUI consumableNameText;
    public TextMeshProUGUI consumableTypeText;
    public TextMeshProUGUI consumableBuffText;
    public TextMeshProUGUI consumableCooldownText;
    public TextMeshProUGUI consumableDescText;

    // ⑫ RESOURCE
    [Header("⑫ Resource Panel")]
    public GameObject      resourcePanel;
    public Image           resourceIcon;
    public TextMeshProUGUI resourceNameText;
    public TextMeshProUGUI resourceTypeText;
    public TextMeshProUGUI resourceQuantityText;
    public TextMeshProUGUI resourceValueText;
    public TextMeshProUGUI resourceDescText;

    // ⑬ STATUS EFFECT
    [Header("⑬ Status Effect Panel")]
    public GameObject      statusEffectPanel;
    public Image           seIcon;
    public TextMeshProUGUI seNameText;
    public TextMeshProUGUI seTypeText;
    public TextMeshProUGUI seDurationText;
    public TextMeshProUGUI seDescText;

    [Header("Offset souris")]
    public Vector2 offset = new Vector2(16f, -16f);

    private Canvas        _canvas;
    private Player        _player;
    private RectTransform _canvasRect;
    private GameObject[]  _allPanels;

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _canvas     = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();
        _canvasRect = _canvas?.transform as RectTransform;
        _allPanels  = new GameObject[]
        {
            weaponPanel, armorPanel, helmetPanel, glovesPanel, bootsPanel,
            jewelryPanel, spiritPanel, runePanel, gemPanel,
            skillPanel, permanentSkillPanel, passiveSkillPanel, consumablePanel, resourcePanel, statusEffectPanel
        };
        HideTooltip();
        _player = FindObjectOfType<Player>();
    }

    // =========================================================
    // API — ITEM (dispatch automatique)
    // =========================================================

    public void ShowItemTooltip(InventoryItem item)
    {
        if (item == null || tooltipPanel == null) return;
        if      (item.WeaponInstance     != null) ShowWeapon(item.WeaponInstance);
        else if (item.ArmorInstance      != null) ShowArmor(item.ArmorInstance);
        else if (item.HelmetInstance     != null) ShowHelmet(item.HelmetInstance);
        else if (item.GlovesInstance     != null) ShowGloves(item.GlovesInstance);
        else if (item.BootsInstance      != null) ShowBoots(item.BootsInstance);
        else if (item.JewelryInstance    != null) ShowJewelry(item.JewelryInstance);
        else if (item.SpiritInstance     != null) ShowSpirit(item.SpiritInstance);
        else if (item.RuneInstance       != null) ShowRune(item.RuneInstance);
        else if (item.GemInstance        != null) ShowGem(item.GemInstance);
        else if (item.ConsumableInstance != null) ShowConsumable(item.ConsumableInstance);
        else if (item.ResourceInstance   != null) ShowResource(item.ResourceInstance);
    }

    // =========================================================
    // HELPER — Niveau requis coloré
    // Blanc = joueur a le niveau | Rouge = niveau insuffisant
    // =========================================================

    private string RuneLevelTag(RuneInstance r)
    {
        bool canInsert = true;
        if (r.Category == RuneCategory.Weapon)
        {
            var weapon = _player?.equippedWeaponInstance;
            if (weapon?.data != null) canInsert = r.CanInsertInto(weapon.data.requiredLevel);
        }
        else
        {
            var armor = _player?.equippedArmorInstance;
            if (armor?.data != null) canInsert = r.CanInsertInto(armor.data.requiredLevel);
        }
        string color = canInsert ? "#FFFFFF" : "#FF4444";
        return $"<color={color}>Niveau : {r.LevelLabel}</color>";
    }

    private string LevelTag(int requiredLevel)
    {
        int playerLevel = _player != null ? _player.level : 0;
        string color    = playerLevel >= requiredLevel ? "#FFFFFF" : "#FF4444";
        return $"<color={color}>Niveau requis : {requiredLevel}</color>";
    }

    // =========================================================
    // ① WEAPON
    // =========================================================

    private void ShowWeapon(WeaponInstance w)
    {
        ShowOnly(weaponPanel);
        SetIcon(weaponIcon, w.Icon);
        SetText(weaponNameText,      w.DisplayNameRich);
        SetText(weaponLevelText,     w.data != null ? LevelTag(w.data.requiredLevel) : "");
        SetText(weaponRarityText,    w.RarityLabel);
        SetText(weaponTypeText,      w.WeaponType.ToString());
        SetText(weaponDamageText,    $"Dégâts : {Mathf.RoundToInt(w.FinalDamageMin)} – {Mathf.RoundToInt(w.FinalDamageMax)}");
        SetText(weaponPrecisionText, $"Précision : {Mathf.RoundToInt(w.FinalPrecision)}");
        SetText(weaponSpeedText,     $"Vitesse : {w.AttackSpeed:F1} att/s");
        SetText(weaponCritText,      $"Critique : {w.CritChance * 100f:F0}% / x{w.CritMultiplier:F2}");
        SetText(weaponRuneText, w.equippedRune != null
            ? (w.equippedRune.isIdentified ? $"Rune : {w.equippedRune.Label}" : "Rune : ???")
            : "Rune : aucune");
        ShowSection(weaponBonusSection,  weaponBonusText,  BuildBonuses(w.Bonuses));
        ShowSection(weaponResistSection, weaponResistText, BuildDebuffResist(w.DebuffResistances));
        ShowSection(weaponOnHitSection,  weaponOnHitText,  BuildOnHitEffects(w.OnHitReceivedEffects));
        SetText(weaponDescText, FormatDesc(w.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ② ARMOR
    // =========================================================

    private void ShowArmor(ArmorInstance a)
    {
        ShowOnly(armorPanel);
        SetIcon(armorIcon, a.Icon);
        SetText(armorNameText,    a.DisplayNameRich);
        SetText(armorLevelText,   a.data != null ? LevelTag(a.data.requiredLevel) : "");
        SetText(armorRarityText,  a.RarityLabel);
        SetText(armorTypeText,    a.ArmorType.ToString());
        SetText(armorMeleeText,   $"Déf. mêlée : {Mathf.RoundToInt(a.FinalMeleeDefense)}");
        SetText(armorRangedText,  $"Déf. distance : {Mathf.RoundToInt(a.FinalRangedDefense)}");
        SetText(armorMagicText,   $"Déf. magie : {Mathf.RoundToInt(a.FinalMagicDefense)}");
        SetText(armorDodgeText,   $"Esquive : {Mathf.RoundToInt(a.FinalDodge)}");
        SetText(armorRuneText, a.equippedRune != null
            ? (a.equippedRune.isIdentified ? $"Rune : {a.equippedRune.Label}" : "Rune : ???")
            : "Rune : aucune");
        ShowSection(armorBonusSection,  armorBonusText,  BuildBonuses(a.Bonuses));
        ShowSection(armorResistSection, armorResistText, BuildDebuffResist(a.DebuffResistances));
        ShowSection(armorOnHitSection,  armorOnHitText,  BuildOnHitEffects(a.OnHitReceivedEffects));
        SetText(armorDescText, FormatDesc(a.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ③ HELMET
    // =========================================================

    private void ShowHelmet(HelmetInstance h)
    {
        ShowOnly(helmetPanel);
        SetIcon(helmetIcon, h.Icon);
        SetText(helmetNameText,  h.HelmetName);
        SetText(helmetLevelText, h.data != null ? LevelTag(h.data.requiredLevel) : "");
        ShowSection(helmetBonusSection,  helmetBonusText,  BuildBonuses(h.Bonuses));
        ShowSection(helmetResistSection, helmetResistText, BuildDebuffResist(h.DebuffResistances));
        ShowSection(helmetOnHitSection,  helmetOnHitText,  BuildOnHitEffects(h.OnHitReceivedEffects));
        SetText(helmetDescText, FormatDesc(h.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ④ GLOVES
    // =========================================================

    private void ShowGloves(GlovesInstance g)
    {
        ShowOnly(glovesPanel);
        SetIcon(glovesIcon, g.Icon);
        SetText(glovesNameText,   g.GlovesName);
        SetText(glovesLevelText,  g.data != null ? LevelTag(g.data.requiredLevel) : "");
        SetText(glovesFusionText, $"Fusion : {g.FusionLabel}");
        SetText(glovesMeleeText,  $"Déf. mêlée : {Mathf.RoundToInt(g.MeleeDefense)}");
        SetText(glovesRangedText, $"Déf. distance : {Mathf.RoundToInt(g.RangedDefense)}");
        SetText(glovesMagicText,  $"Déf. magie : {Mathf.RoundToInt(g.MagicDefense)}");
        ShowSection(glovesResistElemSection,   glovesResistElemText,   BuildElemResist(g.resistFire, g.resistWater, g.resistLightning, g.resistEarth, g.resistNature, g.resistDarkness, g.resistLight));
        ShowSection(glovesBonusSection,        glovesBonusText,        BuildBonuses(g.Bonuses));
        ShowSection(glovesDebuffResistSection, glovesDebuffResistText, BuildDebuffResist(g.DebuffResistances));
        ShowSection(glovesOnHitSection,        glovesOnHitText,        BuildOnHitEffects(g.OnHitReceivedEffects));
        SetText(glovesDescText, FormatDesc(g.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑤ BOOTS
    // =========================================================

    private void ShowBoots(BootsInstance b)
    {
        ShowOnly(bootsPanel);
        SetIcon(bootsIcon, b.Icon);
        SetText(bootsNameText,   b.BootsName);
        SetText(bootsLevelText,  b.data != null ? LevelTag(b.data.requiredLevel) : "");
        SetText(bootsFusionText, $"Fusion : {b.FusionLabel}");
        SetText(bootsMeleeText,  $"Déf. mêlée : {Mathf.RoundToInt(b.MeleeDefense)}");
        SetText(bootsRangedText, $"Déf. distance : {Mathf.RoundToInt(b.RangedDefense)}");
        SetText(bootsMagicText,  $"Déf. magie : {Mathf.RoundToInt(b.MagicDefense)}");
        ShowSection(bootsResistElemSection,   bootsResistElemText,   BuildElemResist(b.resistFire, b.resistWater, b.resistLightning, b.resistEarth, b.resistNature, b.resistDarkness, b.resistLight));
        ShowSection(bootsBonusSection,        bootsBonusText,        BuildBonuses(b.Bonuses));
        ShowSection(bootsDebuffResistSection, bootsDebuffResistText, BuildDebuffResist(b.DebuffResistances));
        ShowSection(bootsOnHitSection,        bootsOnHitText,        BuildOnHitEffects(b.OnHitReceivedEffects));
        SetText(bootsDescText, FormatDesc(b.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑥ JEWELRY
    // =========================================================

    private void ShowJewelry(JewelryInstance j)
    {
        ShowOnly(jewelryPanel);
        SetIcon(jewelryIcon, j.Icon);
        SetText(jewelryNameText,      j.JewelryName);
        SetText(jewelryLevelText,     j.data != null ? LevelTag(j.data.requiredLevel) : "");
        SetText(jewelrySlotText,      j.Slot.ToString());
        SetText(jewelryItemLevelText, $"Niveau bijou : {j.MaxGemLevel}");
        SetText(jewelryMeleeText,     j.MeleeDefense  > 0f ? $"Déf. mêlée : {j.MeleeDefense}"    : "");
        SetText(jewelryRangedText,    j.RangedDefense > 0f ? $"Déf. distance : {j.RangedDefense}" : "");
        SetText(jewelryMagicText,     j.MagicDefense  > 0f ? $"Déf. magie : {j.MagicDefense}"     : "");
        var sbGems = new System.Text.StringBuilder();
        if (j.gemSlots != null)
            for (int i = 0; i < j.gemSlots.Length; i++)
                sbGems.AppendLine(j.gemSlots[i].IsEmpty
                    ? $"Slot {i+1} : vide"
                    : $"Slot {i+1} : {j.gemSlots[i].gem.Label}");
        SetText(jewelryGemsText, sbGems.ToString().TrimEnd());
        ShowSection(jewelryBonusSection,  jewelryBonusText,  BuildBonuses(j.Bonuses));
        ShowSection(jewelryResistSection, jewelryResistText, BuildDebuffResist(j.DebuffResistances));
        ShowSection(jewelryOnHitSection,  jewelryOnHitText,  BuildOnHitEffects(j.OnHitReceivedEffects));
        SetText(jewelryDescText, FormatDesc(j.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑦ SPIRIT
    // =========================================================

    private void ShowSpirit(SpiritInstance s)
    {
        ShowOnly(spiritPanel);
        SetIcon(spiritIcon, s.Icon);
        SetText(spiritNameText,     s.SpiritName);
        SetText(spiritLevelReqText, s.data != null ? LevelTag(s.data.requiredLevel) : "");
        SetText(spiritElementText,  s.Element.GetLabel());
        SetText(spiritLevelText,    $"Niveau : {s.level} / {s.MaxLevel}");
        SetText(spiritPointsText,   $"Points élémentaires : {s.TotalElementalPoints}");
        SetText(spiritXPText,       s.IsMaxLevel
            ? "Niveau maximum atteint"
            : $"XP : {s.currentXP} / {s.XPRequired}");
        SetText(spiritDescText, FormatDesc(s.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑧ RUNE
    // =========================================================

    private void ShowRune(RuneInstance r)
    {
        ShowOnly(runePanel);
        SetIcon(runeIcon, r.Icon);
        SetText(runeNameText,       r.RuneName);
        SetText(runeLevelReqText, RuneLevelTag(r));
        SetText(runeCategoryText,   r.Category.ToString());
        SetText(runeLevelText,      r.LevelLabel);
        SetText(runeRarityText,     r.RarityLabel);
        SetText(runeIdentifiedText, r.isIdentified ? "" : "Non identifiée — voir l'Antiquaire");
        SetText(runeStatsText,      r.isIdentified && r.bonuses?.Count > 0
            ? BuildBonuses(r.bonuses)
            : (!r.isIdentified ? "???" : ""));
        SetText(runeDescText, FormatDesc(r.data?.description));
        Show();
    }

    // =========================================================
    // ⑨ GEM
    // =========================================================

    private void ShowGem(GemInstance g)
    {
        ShowOnly(gemPanel);
        SetIcon(gemIcon, g.Icon);
        SetText(gemNameText,  g.GemName);
        SetText(gemLevelText, $"Niveau : {g.GemLevel}");
        SetText(gemStatText,  g.isRevealed
            ? $"{g.rolledStat} : +{g.rolledValue:F2}"
            : "??? — insérer dans un bijou pour révéler");
        SetText(gemDescText, FormatDesc(g.data?.description));
        Show();
    }

    // =========================================================
    // ⑩ SKILL
    // =========================================================

    public void ShowSkillTooltip(SkillData skill)
    {
        if (skill == null || tooltipPanel == null) return;
        ShowOnly(skillPanel);
        SetIcon(skillIcon, skill.icon);
        SetText(skillNameText,     skill.skillName.Get(LocalizationManager.CurrentLanguage));
        SetText(skillTypeText,     skill.skillType.ToString());
        SetText(skillCooldownText, skill.cooldown > 0f ? $"CD : {skill.cooldown:F1}s" : "Passif");
        string costStr = "";
        if (skill.manaCost > 0f) costStr += $"Mana : {skill.manaCost}";
        if (skill.hpCost   > 0f) costStr += (costStr.Length > 0 ? " | " : "") + $"HP : {skill.hpCost}";
        if (skill.goldCost > 0)  costStr += (costStr.Length > 0 ? " | " : "") + $"Aeris : {skill.goldCost}";
        SetText(skillManaCostText, costStr);
        SetText(skillDamageText,   skill.effectType == SkillEffectType.Damage
            ? $"x{skill.damageMultiplier:F2}" : "");
        string elemLabel = "";
        if (skill.elements != null)
            foreach (var e in skill.elements)
                if (e != ElementType.Neutral) elemLabel += e.GetLabel() + " ";
        SetText(skillElementText, elemLabel.TrimEnd());
        SetText(skillDescText,    FormatDesc(skill.description.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑭ PERMANENT SKILL — bonus permanent débloquable (Antre...).
    // Uniquement icône+nom+description : le texte (rédigé par le designer) porte
    // l'effet, pas de breakdown auto — c'est juste un ajout de stats.
    // =========================================================

    public void ShowPermanentSkillTooltip(PermanentSkillData permanent)
    {
        if (permanent == null || tooltipPanel == null) return;
        ShowOnly(permanentSkillPanel);
        SetIcon(permanentSkillIcon, permanent.icon);
        SetText(permanentSkillNameText, permanent.skillName.Get(LocalizationManager.CurrentLanguage));
        SetText(permanentSkillDescText, FormatDesc(permanent.description.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑮ PASSIVE SKILL — proc conditionnel (trigger + effets, PassiveSkillData).
    // Combinaisons trigger/effets trop variables pour un breakdown générique
    // (ex: "regen 500 HP tous les 10 coups", "invulnérable 5s après un coup
    // mortel"...) — icône+nom+description uniquement, comme Permanent.
    // =========================================================

    public void ShowPassiveSkillTooltip(PassiveSkillData passive)
    {
        if (passive == null || tooltipPanel == null) return;
        ShowOnly(passiveSkillPanel);
        SetIcon(passiveSkillIcon, passive.icon);
        SetText(passiveSkillNameText, passive.skillName.Get(LocalizationManager.CurrentLanguage));
        SetText(passiveSkillDescText, FormatDesc(passive.description.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑪ CONSUMABLE
    // =========================================================

    private void ShowConsumable(ConsumableInstance c)
    {
        ShowOnly(consumablePanel);
        SetIcon(consumableIcon, c.Icon);
        SetText(consumableNameText,     c.Name);
        SetText(consumableTypeText,     c.data?.consumableType.ToString() ?? "");
        SetText(consumableBuffText,     c.data?.buffEffect?.description.Get(LocalizationManager.CurrentLanguage) ?? "");
        SetText(consumableCooldownText, c.data?.cooldown > 0f ? $"CD : {c.data.cooldown}s"        : "");
        SetText(consumableDescText,     FormatDesc(c.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑫ RESOURCE
    // =========================================================

    private void ShowResource(ResourceInstance r)
    {
        ShowOnly(resourcePanel);
        SetIcon(resourceIcon, r.Icon);
        SetText(resourceNameText,     r.Name);
        SetText(resourceTypeText,     r.Type.ToString());
        SetText(resourceQuantityText, $"Quantité : {r.quantity} / {r.MaxStack}");
        SetText(resourceValueText,    $"Valeur : {r.SellPrice} a");
        SetText(resourceDescText,     FormatDesc(r.data?.description?.Get(LocalizationManager.CurrentLanguage)));
        Show();
    }

    // =========================================================
    // ⑬ STATUS EFFECT
    // =========================================================

    public void ShowStatusEffectTooltip(StatusEffectUIEntry entry)
    {
        if (entry == null || tooltipPanel == null) return;
        ShowOnly(statusEffectPanel);
        string color = entry.isDebuff ? "#FF4444" : "#44FF88";
        SetIcon(seIcon, entry.icon);
        SetText(seNameText,     $"<color={color}>{entry.key}</color>");
        SetText(seTypeText,     entry.isDebuff ? "Debuff" : "Buff");
        SetText(seDurationText, entry.totalDuration > 0f
            ? $"Durée : {entry.remainingTime:F1}s / {entry.totalDuration:F1}s" : "");
        SetText(seDescText, entry.data != null
            ? FormatDesc(entry.data.description.Get(LocalizationManager.CurrentLanguage))
            : "");
        Show();
    }

    // =========================================================
    // CACHER
    // =========================================================

    public void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    // =========================================================
    // UPDATE — Suivi souris
    // =========================================================

    private void Update()
    {
        if (tooltipPanel != null && tooltipPanel.activeSelf)
            PositionTooltip();
    }

    private void PositionTooltip()
    {
        if (tooltipRect == null || _canvasRect == null) return;
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, Input.mousePosition, cam, out Vector2 localPoint);
        localPoint += offset;
        Vector2 size = tooltipRect.sizeDelta, cs = _canvasRect.sizeDelta;
        localPoint.x = Mathf.Clamp(localPoint.x, -cs.x / 2f, cs.x / 2f - size.x);
        localPoint.y = Mathf.Clamp(localPoint.y, -cs.y / 2f + size.y, cs.y / 2f);
        tooltipRect.anchoredPosition = localPoint;
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private void ShowOnly(GameObject target)
    {
        if (_allPanels == null) return;
        foreach (var p in _allPanels) if (p != null) p.SetActive(p == target);
    }

    private void Show() { tooltipPanel.SetActive(true); PositionTooltip(); }

    private void ShowSection(GameObject section, TextMeshProUGUI label, string content)
    {
        if (section == null) return;
        bool has = !string.IsNullOrEmpty(content);
        section.SetActive(has);
        if (label != null && has) label.text = content;
    }

    private string FormatDesc(string desc)
        => string.IsNullOrEmpty(desc) ? "" : $"<i>{desc}</i>";

    private string BuildBonuses(List<StatBonus> bonuses)
    {
        if (bonuses == null || bonuses.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var b in bonuses)
        {
            bool isRatio = b.mode == ModifierType.Percent
                        || b.statType == StatType.CritChance || b.statType == StatType.CritMultiplier
                        || b.statType.ToString().StartsWith("Resist")
                        || b.statType.ToString().StartsWith("Element");
            sb.AppendLine(isRatio
                ? $"{b.statType} : +{b.value * 100f:F0}%"
                : $"{b.statType} : +{b.value:F0}");
        }
        return sb.ToString().TrimEnd();
    }

    private string BuildStatusEffects(List<StatusEffectEntry> effects)
    {
        if (effects == null || effects.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var e in effects)
            if (e?.effect != null) sb.AppendLine($"{e.effect.effectName.Get(LocalizationManager.CurrentLanguage)} : {e.chance * 100f:F0}%");
        return sb.ToString().TrimEnd();
    }

    private string BuildDebuffResist(List<DebuffResistanceEntry> resistances)
    {
        if (resistances == null || resistances.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var r in resistances)
            sb.AppendLine($"Résist. {r.debuffType} : {r.resistChance * 100f:F0}%");
        return sb.ToString().TrimEnd();
    }

    private string BuildOnHitEffects(List<OnHitReceivedEffectEntry> effects)
    {
        if (effects == null || effects.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var e in effects)
            if (e?.effect != null) sb.AppendLine($"{e.effect.effectName} : {e.EffectiveChance * 100f:F0}%");
        return sb.ToString().TrimEnd();
    }

    private string BuildElemResist(float fire, float water, float lightning,
                                   float earth, float nature, float darkness, float light)
    {
        var sb = new System.Text.StringBuilder();
        if (fire      > 0f) sb.AppendLine($"Feu : {fire      * 100f:F0}%");
        if (water     > 0f) sb.AppendLine($"Eau : {water     * 100f:F0}%");
        if (lightning > 0f) sb.AppendLine($"Foudre : {lightning * 100f:F0}%");
        if (earth     > 0f) sb.AppendLine($"Terre : {earth   * 100f:F0}%");
        if (nature    > 0f) sb.AppendLine($"Nature : {nature * 100f:F0}%");
        if (darkness  > 0f) sb.AppendLine($"Ténèbres : {darkness * 100f:F0}%");
        if (light     > 0f) sb.AppendLine($"Lumière : {light * 100f:F0}%");
        return sb.ToString().TrimEnd();
    }

    private void SetText(TextMeshProUGUI label, string value)
        { if (label != null) label.text = value ?? ""; }

    private void SetIcon(Image img, Sprite sprite)
        { if (img == null) return; img.sprite = sprite; img.enabled = sprite != null; }

}
