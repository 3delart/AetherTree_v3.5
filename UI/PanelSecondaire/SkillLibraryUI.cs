using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Linq;

// =============================================================
// SKILLLIBRARYUI.CS — Grimoire / Bibliothèque de compétences
// AetherTree GDD v30 — Section 11.4 (touche K)
//
// Flow déblocage :
//   UnlockManager → MailboxSystem → player.UnlockSkill()
//   → SkillLibraryUI.RefreshIfOpen()
//
// Journal d'obtention :
//   UnlockManager.GetRecordForSkill(skill) → UnlockRecord
//   → date, level, timePlayed au moment du déblocage
//
// Hiérarchie UI attendue :
//   SkillLibraryPanel
//     SkillLibraryTitlePanel / SkillLibraryCloseButton
//     SkillLibraryTabs : BasicAttackTab | ActifTab | UltimeTab | PassifTab | Permanents
//     SkillLibraryFilterPanel : FilterElementButtonAll | FilterTagButtonAll
//     SkillPanel > SkillLibrary (ScrollRect) > Viewport > Content
//     SkillInfosPanel
//       SkillInfoTitle : SkillIcon | SkillName | SkillElement | SkillTags
//       SkillDetails   : SkillMpPanel | SkillCDPanel | SkillRangePanel
//       SkillDescription > DescriptionText
//       SkillJournal > SkillJournalText | Date | Level | Time
// =============================================================

public class SkillLibraryUI : MonoBehaviour
{
    public static SkillLibraryUI Instance { get; private set; }

    // =========================================================
    // RÉFÉRENCES UI
    // =========================================================
    [Header("Panel principal")]
    public GameObject panel;
    public Button     closeButton;

    [Header("Onglets")]
    public Button tabBasicAttack;
    public Button tabActifs;
    public Button tabUltimes;
    public Button tabPassifs;
    public Button tabPermanents;

    [Header("Filtres élémentaires")]
    [Tooltip("Conteneur horizontal pour les boutons filtre éléments")]
    public Transform elementFilterBar;
    public Button    filterTousElements;

    [Header("Filtres tags")]
    [Tooltip("Conteneur horizontal pour les boutons filtre tags")]
    public Transform tagFilterBar;
    public Button    filterTousTags;

    [Header("Prefab bouton filtre — Button + TextMeshProUGUI enfant")]
    public GameObject filterButtonPrefab;

    [Header("Grille de skills")]
    public Transform  skillGridContent;
    public GameObject skillEntryPrefab;

    [Header("Panel Détail — Header")]
    public Image           detailIcon;
    public TextMeshProUGUI detailName;
    public TextMeshProUGUI detailElement;
    public TextMeshProUGUI detailTags;

    [Header("Panel Détail — Stats")]
    public TextMeshProUGUI statMpValue;
    public TextMeshProUGUI statCdValue;
    public TextMeshProUGUI statRangeValue;

    [Header("Panel Détail — Description")]
    public TextMeshProUGUI descText;

    [Header("Panel Détail — Journal")]
    public TextMeshProUGUI journalDate;
    public TextMeshProUGUI journalLevel;
    public TextMeshProUGUI journalTime;

    // =========================================================
    // ÉTAT INTERNE
    // =========================================================
    private SkillLibraryTab      currentTab     = SkillLibraryTab.BasicAttack;
    private HashSet<ElementType> activeElements = new HashSet<ElementType>();
    private HashSet<SkillTag>    activeTags     = new HashSet<SkillTag>();
    private bool   _isOpen = false;
    private Player _player;

    private Dictionary<ElementType, Button> elementButtons = new Dictionary<ElementType, Button>();
    private Dictionary<SkillTag,    Button> tagButtons     = new Dictionary<SkillTag,    Button>();

    private static readonly Color TAB_ACTIVE      = new Color(0.35f, 0.22f, 0.65f);
    private static readonly Color TAB_INACTIVE    = new Color(0.15f, 0.15f, 0.25f);
    private static readonly Color FILTER_ACTIVE   = new Color(0.4f,  0.25f, 0.75f);
    private static readonly Color FILTER_INACTIVE = new Color(0.2f,  0.2f,  0.2f);

    // =========================================================
    // INIT
    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();

        // ── Listeners onglets ────────────────────────────────
        tabBasicAttack?.onClick.AddListener(() => SwitchTab(SkillLibraryTab.BasicAttack));
        tabActifs?.onClick    .AddListener(() => SwitchTab(SkillLibraryTab.Actifs));
        tabUltimes?.onClick   .AddListener(() => SwitchTab(SkillLibraryTab.Ultimes));
        tabPassifs?.onClick   .AddListener(() => SwitchTab(SkillLibraryTab.Passifs));
        tabPermanents?.onClick.AddListener(() => SwitchTab(SkillLibraryTab.Permanents));

        closeButton?.onClick.AddListener(Toggle);

        filterTousElements?.onClick.AddListener(ClearElementFilters);
        filterTousTags?.onClick    .AddListener(ClearTagFilters);

        RefreshTabVisuals();
        UpdateFilterTousVisual(filterTousElements, activeElements.Count);
        UpdateFilterTousVisual(filterTousTags,     activeTags.Count);

        ClearDetail();
    }

    private void Update()
    {
        if (GameControls.OpenSkillLibrary) Toggle();
    }

    // =========================================================
    // OPEN / CLOSE / TOGGLE
    // =========================================================
    public void Open()
    {
        _isOpen = true;
        if (_player == null) _player = FindObjectOfType<Player>();
        if (panel != null) panel.SetActive(true);
        ClearDetail();

        // Pas de coroutine — refresh direct
        var tabToLoad = currentTab;
        currentTab = (SkillLibraryTab)(-1);
        SwitchTab(tabToLoad);
    }


    public void Close()
    {
        _isOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    public void Toggle()
    {
        if (_isOpen) Close(); else Open();
    }

    /// <summary>Rafraîchit si le panel est ouvert. Appelé par MailboxSystem après déblocage d'un skill.</summary>
    public void RefreshIfOpen()
    {
        if (_isOpen) { RebuildFilterBars(); RefreshGrid(); }
    }

    public bool IsOpen => _isOpen;

    // =========================================================
    // ONGLETS
    // =========================================================
    private void SwitchTab(SkillLibraryTab tab)
    {
        currentTab = tab;
        ClearElementFilters();  // remet les filtres à zéro au changement d'onglet
        ClearTagFilters();
        RefreshTabVisuals();
        RebuildFilterBars();
        RefreshGrid();
        ClearDetail();
    }

    private void RefreshTabVisuals()
    {
        if (tabBasicAttack != null) tabBasicAttack.image.color = currentTab == SkillLibraryTab.BasicAttack ? TAB_ACTIVE : TAB_INACTIVE;
        if (tabActifs      != null) tabActifs.image.color      = currentTab == SkillLibraryTab.Actifs      ? TAB_ACTIVE : TAB_INACTIVE;
        if (tabUltimes     != null) tabUltimes.image.color     = currentTab == SkillLibraryTab.Ultimes     ? TAB_ACTIVE : TAB_INACTIVE;
        if (tabPassifs     != null) tabPassifs.image.color     = currentTab == SkillLibraryTab.Passifs     ? TAB_ACTIVE : TAB_INACTIVE;
        if (tabPermanents  != null) tabPermanents.image.color  = currentTab == SkillLibraryTab.Permanents  ? TAB_ACTIVE : TAB_INACTIVE;
    }

    // =========================================================
    // BARRES DE FILTRES — générées dynamiquement depuis les skills débloqués
    // =========================================================
    private void RebuildFilterBars()
    {
        if (_player == null) return;
        RebuildElementBar();
        RebuildTagBar();
    }

    private void RebuildElementBar()
    {
        if (elementFilterBar == null || filterButtonPrefab == null) return;

        // Uniquement les éléments présents dans l'onglet courant
        var presentElements = new HashSet<ElementType>();
        foreach (var skill in GetSkillsForCurrentTabUnfiltered())
        {
            if (skill == null) continue;
            foreach (var e in skill.elements) presentElements.Add(e);
        }

        var toRemove = elementButtons.Keys.Where(e => !presentElements.Contains(e)).ToList();
        foreach (var e in toRemove)
        {
            if (elementButtons[e] != null) Destroy(elementButtons[e].gameObject);
            elementButtons.Remove(e);
            activeElements.Remove(e);
        }

        foreach (var element in presentElements)
        {
            if (elementButtons.ContainsKey(element)) continue;
            var btn = CreateFilterButton(elementFilterBar, element.ToString(), ElementData.GetColor(element));
            var captured = element;
            btn.onClick.AddListener(() => ToggleElementFilter(captured, btn));
            elementButtons[element] = btn;
        }

        UpdateFilterTousVisual(filterTousElements, activeElements.Count);
    }

    private void RebuildTagBar()
    {
        if (tagFilterBar == null || filterButtonPrefab == null) return;

        // Uniquement les tags présents dans l'onglet courant
        var presentTags = new HashSet<SkillTag>();
        foreach (var skill in GetSkillsForCurrentTabUnfiltered())
        {
            if (skill == null) continue;
            foreach (var t in skill.tags) presentTags.Add(t);
        }

        var toRemove = tagButtons.Keys.Where(t => !presentTags.Contains(t)).ToList();
        foreach (var t in toRemove)
        {
            if (tagButtons[t] != null) Destroy(tagButtons[t].gameObject);
            tagButtons.Remove(t);
            activeTags.Remove(t);
        }

        foreach (var tag in presentTags)
        {
            if (tagButtons.ContainsKey(tag)) continue;
            var btn = CreateFilterButton(tagFilterBar, tag.ToString(), Color.white);
            var captured = tag;
            btn.onClick.AddListener(() => ToggleTagFilter(captured, btn));
            tagButtons[tag] = btn;
        }

        UpdateFilterTousVisual(filterTousTags, activeTags.Count);
    }

    private Button CreateFilterButton(Transform parent, string label, Color color)
    {
        var go  = Instantiate(filterButtonPrefab, parent);
        var btn = go.GetComponent<Button>();
        var txt = go.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text        = label;
        if (btn != null) btn.image.color = FILTER_INACTIVE;
        return btn;
    }

    // =========================================================
    // FILTRES ÉLÉMENTS
    // =========================================================
    private void ToggleElementFilter(ElementType element, Button btn)
    {
        if (activeElements.Contains(element)) { activeElements.Remove(element); btn.image.color = FILTER_INACTIVE; }
        else                                  { activeElements.Add(element);    btn.image.color = FILTER_ACTIVE;   }
        UpdateFilterTousVisual(filterTousElements, activeElements.Count);
        RefreshGrid();
    }

    private void ClearElementFilters()
    {
        activeElements.Clear();
        foreach (var kvp in elementButtons) kvp.Value.image.color = FILTER_INACTIVE;
        UpdateFilterTousVisual(filterTousElements, 0);
    }

    // =========================================================
    // FILTRES TAGS
    // =========================================================
    private void ToggleTagFilter(SkillTag tag, Button btn)
    {
        if (activeTags.Contains(tag)) { activeTags.Remove(tag); btn.image.color = FILTER_INACTIVE; }
        else                          { activeTags.Add(tag);    btn.image.color = FILTER_ACTIVE;   }
        UpdateFilterTousVisual(filterTousTags, activeTags.Count);
        RefreshGrid();
    }

    private void ClearTagFilters()
    {
        activeTags.Clear();
        foreach (var kvp in tagButtons) kvp.Value.image.color = FILTER_INACTIVE;
        UpdateFilterTousVisual(filterTousTags, 0);
    }

    private void UpdateFilterTousVisual(Button btn, int activeCount)
    {
        if (btn == null) return;
        btn.image.color = activeCount == 0 ? FILTER_ACTIVE : FILTER_INACTIVE;
    }

    // =========================================================
    // GRILLE DE SKILLS
    // =========================================================
private void RefreshGrid()
{
    if (skillGridContent == null) return;
    foreach (Transform child in skillGridContent) Destroy(child.gameObject);
    if (_player == null) return;

    // ── Onglet Permanents — source différente ─────────────────
    if (currentTab == SkillLibraryTab.Permanents)
    {
        RefreshGridPermanents();
        return;
    }

    // ── Autres onglets — source unlockedSkills ────────────────
    List<SkillData> skills = GetSkillsForTab();
    skills = SortByFilterPriority(skills);
    foreach (var skill in skills)
        SpawnSkillEntry(skill);

    LayoutRebuilder.ForceRebuildLayoutImmediate(skillGridContent as RectTransform);
}

private void RefreshGridPermanents()
{
    if (_player?.unlockedPermanents == null) return;
    foreach (var permanent in _player.unlockedPermanents)
    {
        if (permanent == null) continue;
        SpawnPermanentEntry(permanent);
    }
    LayoutRebuilder.ForceRebuildLayoutImmediate(skillGridContent as RectTransform);
}

private void SpawnPermanentEntry(PermanentSkillData permanent)
{
    if (skillEntryPrefab == null) return;
    var entry = Instantiate(skillEntryPrefab, skillGridContent);

    var img = entry.GetComponent<Image>();
    if (img != null)
    {
        img.sprite = permanent.icon;
        img.color  = permanent.icon != null ? Color.white : new Color(0.3f, 0.3f, 0.3f, 0.8f);
    }

    // Tooltip
    var tooltip = entry.GetComponent<TooltipTrigger>() ?? entry.AddComponent<TooltipTrigger>();
    // TODO : ShowPermanentTooltip si tu ajoutes un panel dédié dans TooltipSystem
    // Pour l'instant : description textuelle dans le detail panel au clic

    var btn = entry.GetComponent<Button>();
    if (btn != null)
    {
        var captured = permanent;
        btn.onClick.AddListener(() => ShowDetailPermanent(captured));
    }
}

private void ShowDetailPermanent(PermanentSkillData permanent)
{
    if (permanent == null) { ClearDetail(); return; }

    if (detailIcon    != null) { detailIcon.sprite = permanent.icon; detailIcon.enabled = permanent.icon != null; }
    if (detailName    != null) detailName.text    = permanent.skillName;
    if (detailElement != null) detailElement.text = $"Permanent  ·  {permanent.category}";
    if (detailTags    != null) detailTags.text    = "";
    if (statMpValue   != null) statMpValue.text   = "—";
    if (statCdValue   != null) statCdValue.text   = "—";
    if (statRangeValue!= null) statRangeValue.text= "—";
    if (descText      != null) descText.text      = !string.IsNullOrEmpty(permanent.description)
                                                    ? permanent.description
                                                    : permanent.GetBonusSummary();
    if (journalDate   != null) journalDate.text   = "—";
    if (journalLevel  != null) journalLevel.text  = "—";
    if (journalTime   != null) journalTime.text   = "—";
}

    /// <summary>
    /// Retourne les skills de l'onglet courant SANS appliquer les filtres élément/tag.
    /// Utilisé par RebuildFilterBars() pour savoir quels filtres proposer.
    /// </summary>
    private List<SkillData> GetSkillsForCurrentTabUnfiltered()
    {
        var result = new List<SkillData>();
        if (_player?.unlockedSkills == null) return result;

        foreach (var skill in _player.unlockedSkills)
        {
            if (skill == null) continue;
            if (MatchesCurrentTab(skill)) result.Add(skill);
        }
        return result;
    }

    private List<SkillData> GetSkillsForTab()
    {
        var result = new List<SkillData>();
        if (_player?.unlockedSkills == null) return result;

        foreach (var skill in _player.unlockedSkills)
        {
            if (skill == null) continue;
            if (!MatchesCurrentTab(skill)) continue;
            if (activeElements.Count > 0 && !MatchesElementFilter(skill)) continue;
            if (activeTags.Count     > 0 && !MatchesTagFilter(skill))     continue;

            result.Add(skill);
        }
        return result;
    }

    /// <summary>
    /// Détermine si un skill appartient à l'onglet courant.
    /// BasicAttack : skillType == BasicAttack OU tag BasicAttack présent (compatibilité)
    /// </summary>
    private bool MatchesCurrentTab(SkillData skill)
    {
        return currentTab switch
        {
            // Un skill est "BasicAttack" si son type est BasicAttack OU s'il porte le tag BasicAttack
            SkillLibraryTab.BasicAttack => skill.skillType == SkillType.BasicAttack
                                        || skill.HasTag(SkillTag.BasicAttack),
            SkillLibraryTab.Actifs      => skill.skillType == SkillType.Active
                                        && !skill.HasTag(SkillTag.BasicAttack),
            SkillLibraryTab.Ultimes     => skill.skillType == SkillType.Ultimate,
            SkillLibraryTab.Passifs     => skill.skillType == SkillType.PassiveUtility,
            SkillLibraryTab.Permanents  => false,
            _                           => false
        };
    }

    private bool MatchesElementFilter(SkillData skill)
    {
        foreach (var f in activeElements)
            if (skill.HasElement(f)) return true;
        return false;
    }

    private bool MatchesTagFilter(SkillData skill)
    {
        foreach (var t in activeTags)
            if (skill.HasTag(t)) return true;
        return false;
    }

    private List<SkillData> SortByFilterPriority(List<SkillData> skills)
    {
        if (activeElements.Count == 0 && activeTags.Count == 0) return skills;

        return skills.OrderByDescending(s =>
        {
            int score = 0;

            if (activeElements.Count > 0)
            {
                if (s.IsCombo)
                {
                    int matches = s.elements.Count(e => activeElements.Contains(e));
                    if (matches == s.elements.Count) score += 3;
                    else if (matches > 0)            score += 2;
                }
                else if (activeElements.Contains(s.PrimaryElement)) score += 1;
            }

            if (activeTags.Count > 0)
            {
                int tagMatches = s.tags != null ? s.tags.Count(t => activeTags.Contains(t)) : 0;
                if (tagMatches == activeTags.Count) score += 3;
                else if (tagMatches > 0)            score += 1;
            }

            return score;
        }).ToList();
    }

    // =========================================================
    // SPAWN SKILL ENTRY
    // =========================================================
    private void SpawnSkillEntry(SkillData skill)
    {
        if (skillEntryPrefab == null || skill == null) return;

        GameObject entry = Instantiate(skillEntryPrefab, skillGridContent);

        // ── Icône ─────────────────────────────────────────────
        var rootImage = entry.GetComponent<Image>();
        if (rootImage != null)
        {
            if (skill.icon != null)
            {
                rootImage.sprite = skill.icon;
                rootImage.color  = Color.white;
            }
            else
            {
                rootImage.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            }
        }

        // ── Clic → affiche le détail ──────────────────────────
        var btn = entry.GetComponent<Button>();
        if (btn != null)
        {
            var captured = skill;
            btn.onClick.AddListener(() => ShowDetail(captured));
        }

        // ── Drag & Drop → SkillBar ────────────────────────────
        var drag = entry.GetComponent<SkillDragSource>();
        if (drag == null) drag = entry.AddComponent<SkillDragSource>();
        drag.skill = skill;

        // Tooltip au survol
        var tooltipTrigger = entry.GetComponent<TooltipTrigger>();
        if (tooltipTrigger == null) tooltipTrigger = entry.AddComponent<TooltipTrigger>();
        tooltipTrigger.SetSkill(skill);
    }

    // =========================================================
    // PANEL DÉTAIL
    // =========================================================
    private void ShowDetail(SkillData skill)
    {
        if (skill == null) { ClearDetail(); return; }

        // ── Icône ─────────────────────────────────────────────
        if (detailIcon != null)
        {
            detailIcon.sprite  = skill.icon;
            detailIcon.enabled = skill.icon != null;
        }

        // ── Nom ───────────────────────────────────────────────
        if (detailName != null)
            detailName.text = skill.skillName;

        // ── Type · Éléments ───────────────────────────────────
        if (detailElement != null)
        {
            string typeLabel = skill.skillType switch
            {
                SkillType.BasicAttack    => "Attaque de base",
                SkillType.Active         => "Actif",
                SkillType.Ultimate       => "Ultime",
                SkillType.PassiveUtility => "Passif",
                _ => ""
            };
            string elemLabel = skill.IsNeutral
                ? "Neutre"
                : string.Join(" + ", skill.elements);

            detailElement.text = $"{typeLabel}  ·  {elemLabel}";
        }

        // ── Tags ──────────────────────────────────────────────
        if (detailTags != null)
            detailTags.text = (skill.tags != null && skill.tags.Count > 0)
                ? string.Join("  ·  ", skill.tags)
                : "";

        // ── Stats ─────────────────────────────────────────────
        if (statMpValue    != null) statMpValue.text    = skill.manaCost > 0  ? $"{skill.manaCost}" : "—";
        if (statCdValue    != null) statCdValue.text    = skill.cooldown > 0  ? $"{skill.cooldown}s" : "—";
        if (statRangeValue != null) statRangeValue.text = skill.range    > 0  ? $"{skill.range}m" : "—";

        // ── Description ───────────────────────────────────────
        if (descText != null)
            descText.text = !string.IsNullOrEmpty(skill.description)
                ? skill.description
                : "Aucune description.";

        // ── Journal d'obtention ───────────────────────────────
        UnlockRecord record = UnlockManager.Instance?.GetRecordForSkill(skill);
        if (record != null)
        {
            if (journalDate  != null) journalDate.text  = record.GetDateString();
            if (journalLevel != null) journalLevel.text = record.GetLevelString();
            if (journalTime  != null) journalTime.text  = record.GetTimeString();
        }
        else
        {
            if (journalDate  != null) journalDate.text  = "Départ";
            if (journalLevel != null) journalLevel.text = "Niveau 1";
            if (journalTime  != null) journalTime.text  = "—";
        }
    }

    private void ClearDetail()
    {
        if (detailIcon     != null) { detailIcon.sprite = null; detailIcon.enabled = false; }
        if (detailName     != null) detailName.text     = "";
        if (detailElement  != null) detailElement.text  = "";
        if (detailTags     != null) detailTags.text     = "";
        if (statMpValue    != null) statMpValue.text    = "—";
        if (statCdValue    != null) statCdValue.text    = "—";
        if (statRangeValue != null) statRangeValue.text = "—";
        if (descText       != null) descText.text       = "";
        if (journalDate    != null) journalDate.text    = "—";
        if (journalLevel   != null) journalLevel.text   = "—";
        if (journalTime    != null) journalTime.text    = "—";
    }
}

// =============================================================
// ENUM — onglets de la bibliothèque
// =============================================================
public enum SkillLibraryTab { BasicAttack, Actifs, Ultimes, Passifs, Permanents }
