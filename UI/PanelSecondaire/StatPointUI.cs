using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// STATPOINTUI — Interface d'investissement des points de stats
// Path : Assets/Scripts/UI/StatPointUI.cs
// AetherTree GDD v3.5 — §3.2.1
//
// Affiche les 4 stats investissables (Attaque, Défense, Élémentaire, HP)
// avec rang actuel, barre de progression, coût prochain rang,
// paliers débloqués, et boutons +1 / +10.
//
// Chaque row affiche les stats RÉELLES actuelles (lues sur Entity),
// et un preview delta du prochain rang en vert avant investissement.
//
// Hierarchy attendue :
//   StatPointPanel (Canvas > Panel)
//   ├── Header
//   │   ├── Title (TMP)
//   │   └── AvailablePoints (TMP)     ← "Points disponibles : X"
//   ├── StatsContainer
//   │   ├── Row_Attack
//   │   ├── Row_Defense
//   │   ├── Row_Elemental
//   │   └── Row_HP
//   └── Footer
//       ├── InvestedSummary (TMP)
//       └── RespecButton (Button)
//
// Chaque StatRow prefab :
//   ├── Icon (Image)
//   ├── StatName (TMP)
//   ├── RankText (TMP)               ← "Rang 23 / 100"
//   ├── ProgressBar (Slider)
//   ├── CostText (TMP)               ← "Prochain : 2 pts"
//   ├── CurrentStatsText (TMP)       ← stats réelles actuelles
//   ├── PreviewText (TMP)            ← delta preview si +1 rang
//   ├── BonusText (TMP)              ← bonus paliers résumés
//   ├── MilestonesContainer
//   │   └── 10 MilestoneDot (Image)
//   ├── BtnPlus1 (Button)
//   └── BtnPlus10 (Button)
// =============================================================

public class StatPointUI : MonoBehaviour
{
    public static StatPointUI Instance { get; private set; }

    // ── Références Header ─────────────────────────────────────
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI _availablePointsText;
    [SerializeField] private TextMeshProUGUI _investedSummaryText;

    // ── Références Rows ───────────────────────────────────────
    [Header("Rows (dans l'ordre : Attack, Defense, Elemental, HP)")]
    [SerializeField] private StatRow _rowAttack;
    [SerializeField] private StatRow _rowDefense;
    [SerializeField] private StatRow _rowElemental;
    [SerializeField] private StatRow _rowHP;

    // ── Bouton Respec ─────────────────────────────────────────
    [Header("Footer")]
    [SerializeField] private Button _respecButton;
    [SerializeField] private TextMeshProUGUI _respecCostText;
    [SerializeField] private Button _closeButton;

    // ── Runtime ───────────────────────────────────────────────
    private StatPointSystem _sp;
    private Player          _player;

    // =========================================================
    // INNER CLASS — une ligne de stat
    // =========================================================

    [System.Serializable]
    public class StatRow
    {
        [Header("Identification")]
        public StatCategory category;

        [Header("Textes")]
        public TextMeshProUGUI rankText;         // "Rang 23 / 100"
        public TextMeshProUGUI costText;         // "Prochain : 2 pts"
        public TextMeshProUGUI currentStatsText; // stats réelles depuis Entity
        public TextMeshProUGUI previewText;      // delta preview si +1 rang
        public TextMeshProUGUI bonusText;        // bonus paliers résumés

        [Header("Barre de progression")]
        public Slider progressBar;

        [Header("Paliers (10 dots)")]
        public Image[] milestoneDots;

        [Header("Couleurs palier")]
        public Color colorReached   = new Color(0.3f, 0.85f, 0.4f);
        public Color colorUnreached = new Color(0.25f, 0.25f, 0.3f);

        [Header("Boutons")]
        public Button btnPlus1;
        public Button btnPlus10;

        [Header("État visuel")]
        public CanvasGroup rowGroup;

        // ── Couleurs texte preview ────────────────────────────
        private const string COLOR_GAIN    = "#5EE87A";  // vert gain
        private const string COLOR_CURRENT = "#E0D9C8";  // blanc cassé pour stats actuelles
        private const string COLOR_LABEL   = "#A09880";  // gris label

        // ── Refresh visuel de la ligne ────────────────────────
        public void Refresh(StatPointSystem sp, Player player)
        {
            if (sp == null) return;

            int  rank      = sp.GetRank(category);
            int  cost      = sp.GetNextRankCost(category);
            bool canInvest = sp.CanInvest(category);

            // Rang et barre
            if (rankText    != null) rankText.text    = $"Rang {rank} / 100";
            if (progressBar != null) progressBar.value = rank;

            // Coût
            if (costText != null)
            {
                costText.text = rank >= 100
                    ? "<color=#888>MAXIMUM</color>"
                    : $"Prochain : <b>{cost}</b> pt{(cost > 1 ? "s" : "")}";
            }

            // Stats actuelles depuis Entity
            if (currentStatsText != null)
                currentStatsText.text = BuildCurrentStatsString(sp, player, category);

            // Preview delta +1 rang
            if (previewText != null)
            {
                if (canInvest)
                    previewText.text = BuildPreviewString(sp, player, category);
                else
                    previewText.text = "";
            }

            // Bonus paliers
            if (bonusText != null)
                bonusText.text = BuildBonusString(sp, category);

            // Paliers (dots)
            if (milestoneDots != null)
            {
                for (int i = 0; i < milestoneDots.Length; i++)
                {
                    if (milestoneDots[i] == null) continue;
                    int milestoneRank = (i + 1) * 10;
                    milestoneDots[i].color = rank >= milestoneRank
                        ? colorReached
                        : colorUnreached;
                }
            }

            // Boutons
            if (btnPlus1  != null) btnPlus1.interactable  = canInvest;
            if (btnPlus10 != null) btnPlus10.interactable = canInvest;

            // Opacité globale
            if (rowGroup != null)
                rowGroup.alpha = canInvest ? 1f : 0.65f;
        }

        // =========================================================
        // STATS ACTUELLES — lues directement sur Entity
        // =========================================================

        private string BuildCurrentStatsString(StatPointSystem sp, Player player, StatCategory cat)
        {
            if (player == null) return "";

            var sb = new System.Text.StringBuilder();

            switch (cat)
            {
                case StatCategory.Attack:
                    // Dégâts min–max effectifs
                    int atkMin = Mathf.RoundToInt(player.AttackDamageMin);
                    int atkMax = Mathf.RoundToInt(player.AttackDamageMax);
                    AppendStat(sb, "Dmg", $"{atkMin}–{atkMax}");
                    if (player.Precision > 0f)
                        AppendStat(sb, "Préc.", $"{player.Precision:F0}");
                    if (player.CritChance > 0f)
                        AppendStat(sb, "CritChance", $"{player.CritChance * 100f:F1}%");
                    if (player.CritMultiplier > 1f)
                        AppendStat(sb, "CritMultiplier", $"{player.CritMultiplier:F2}");
                    break;

                case StatCategory.Defense:
                    AppendStat(sb, "Mêlée",  $"{player.MeleeDefense:F0}");
                    AppendStat(sb, "Dist.",   $"{player.RangedDefense:F0}");
                    AppendStat(sb, "Magie",   $"{player.MagicDefense:F0}");
                    if (player.Dodge > 0f)
                        AppendStat(sb, "Esq.", $"{player.Dodge:F0}");
                    if (player.CritDamageReduction > 0f)
                        AppendStat(sb, "−DmgCrit", $"{player.CritDamageReduction * 100f:F0}%");
                    break;

                case StatCategory.Elemental:
                    // Somme des points élémentaires all (même valeur pour tous via StatPoints)
                    float eleTotal = 0f;
                    int eleCount   = 0;
                    foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    {
                        float pts = player.GetElementalPoints(e);
                        if (pts > 0f) { eleTotal += pts; eleCount++; }
                    }
                    if (eleCount > 0)
                        AppendStat(sb, "Pts Élém.", $"{eleTotal / eleCount:F0} (all)");
                    // Résistances — affiche la moyenne des résistances élémentaires
                    float resistSum = 0f;
                    int   resistCnt = 0;
                    foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    {
                        float r = player.GetElementalResistance(e);
                        if (r != 0f) { resistSum += r; resistCnt++; }
                    }
                    if (resistCnt > 0)
                        AppendStat(sb, "Résist.", $"{resistSum / resistCnt * 100f:F1}% moy.");
                    break;

                case StatCategory.HP:
                    AppendStat(sb, "HP",  $"{player.MaxHP:F0}");
                    AppendStat(sb, "MP",  $"{player.MaxMana:F0}");
                    if (player.RegenHP > 0f)
                        AppendStat(sb, "Reg.HP",  $"{player.RegenHP:F1}/s");
                    if (player.RegenMana > 0f)
                        AppendStat(sb, "Reg.MP",  $"{player.RegenMana:F1}/s");
                    break;
            }

            return sb.Length > 0
                ? $"<color={COLOR_CURRENT}>{sb}</color>"
                : "<color=#666>—</color>";
        }

        // =========================================================
        // PREVIEW DELTA — calcule le gain d'un rang supplémentaire
        // sans modifier les données réelles
        // =========================================================

        private string BuildPreviewString(StatPointSystem sp, Player player, StatCategory cat)
        {
            if (player == null) return "";

            var sb = new System.Text.StringBuilder();

            // Gain linéaire fixe par rang (toujours +1 / +10)
            // + éventuel palier si le prochain rang est un multiple de 10
            int nextRank  = sp.GetRank(cat) + 1;
            bool isMilestone = (nextRank % 10 == 0);

            switch (cat)
            {
                case StatCategory.Attack:
                    // Linéaire : +1 ATK min et max
                    int deltaAtkMin = 1;
                    int deltaAtkMax = 1;
                    // Milestone bonus
                    if (isMilestone)
                    {
                        float milestoneFlat = GetMilestoneAttackFlat(nextRank);
                        deltaAtkMin += Mathf.RoundToInt(milestoneFlat);
                        deltaAtkMax += Mathf.RoundToInt(milestoneFlat);
                    }
                    AppendDelta(sb, "Damage", deltaAtkMin == deltaAtkMax
                        ? $"+{deltaAtkMin}"
                        : $"+{deltaAtkMin}–+{deltaAtkMax}");

                    if (isMilestone)
                    {
                        float milestonePrec = GetMilestonePrecision(nextRank);
                        float milestoneCrit = GetMilestoneCritChance(nextRank);
                        float milestoneMult = GetMilestoneCritMult(nextRank);
                        if (milestonePrec > 0f) AppendDelta(sb, "Préc.", $"+{milestonePrec:F0}");
                        if (milestoneCrit > 0f) AppendDelta(sb, "CritChance",  $"+{milestoneCrit * 100f:F0}%");
                        if (milestoneMult > 0f) AppendDelta(sb, "CritMultiplier", $"+{milestoneMult * 100f:F0}%");
                    }
                    break;

                case StatCategory.Defense:
                    // Linéaire : +1 sur les 3 défenses
                    AppendDelta(sb, "Déf.", "+1");
                    if (isMilestone)
                    {
                        float milestoneFlat = GetMilestoneDefenseFlat(nextRank);
                        if (milestoneFlat > 0f) AppendDelta(sb, "  Palier Déf.", $"+{milestoneFlat:F0}");
                        float milestoneDodge = GetMilestoneDodge(nextRank);
                        if (milestoneDodge > 0f) AppendDelta(sb, "Esq.", $"+{milestoneDodge:F0}");
                        float milestoneCDR  = GetMilestoneCritDmgReduc(nextRank);
                        if (milestoneCDR  > 0f) AppendDelta(sb, "ReducDmgCrit", $"+{milestoneCDR * 100f:F0}%");
                    }
                    break;

                case StatCategory.Elemental:
                    // Linéaire : +1 pts élémentaire all
                    AppendDelta(sb, "Pts Élém.", "+1");
                    if (isMilestone)
                    {
                        float milestoneEle = GetMilestoneElementalPts(nextRank);
                        if (milestoneEle > 0f) AppendDelta(sb, "  Palier", $"+{milestoneEle:F0}");
                        float milestoneCd  = GetMilestoneCooldown(nextRank);
                        if (milestoneCd  > 0f) AppendDelta(sb, "ReducCD", $"{milestoneCd * 100f:F0}%");
                        float milestoneRes = GetMilestoneResistEle(nextRank);
                        if (milestoneRes > 0f) AppendDelta(sb, "Résist.", $"+{milestoneRes * 100f:F0}%");
                    }
                    break;

                case StatCategory.HP:
                    // Linéaire : +10 HP et +10 MP
                    AppendDelta(sb, "HP", "+10");
                    AppendDelta(sb, "MP", "+10");
                    if (isMilestone)
                    {
                        float milestoneHP    = GetMilestoneMaxHP(nextRank);
                        float milestoneMP    = GetMilestoneMaxMP(nextRank);
                        float milestoneRegHP = GetMilestoneRegenHP(nextRank);
                        float milestoneRegMP = GetMilestoneRegenMP(nextRank);
                        if (milestoneHP    > 0f) AppendDelta(sb, " Palier HP", $"+{milestoneHP:F0}");
                        if (milestoneMP    > 0f) AppendDelta(sb, "MP",          $"+{milestoneMP:F0}");
                        if (milestoneRegHP > 0f) AppendDelta(sb, "Reg.HP",      $"+{milestoneRegHP:F1}/s");
                        if (milestoneRegMP > 0f) AppendDelta(sb, "Reg.MP",      $"+{milestoneRegMP:F1}/s");
                    }
                    break;
            }

            if (sb.Length == 0) return "";

            string milestoneBadge = isMilestone
                ? $" <color=#F5C542><b>PALIER {nextRank}</b></color>" : "";

            return $"<color={COLOR_LABEL}>+1 rang →</color> <color={COLOR_GAIN}>{sb}</color>{milestoneBadge}";
        }

        // ── Helpers delta bouton +1 ───────────────────────────
        private void AppendDelta(System.Text.StringBuilder sb, string label, string value)
        {
            if (sb.Length > 0) sb.Append("  ");
            sb.Append($"{label} {value}");
        }

        // ── Helper stats actuelles — "label : valeur" ─────────
        // Utilisé par BuildCurrentStatsString pour afficher les stats Entity.
        private void AppendStat(System.Text.StringBuilder sb, string label, string value)
        {
            if (sb.Length > 0) sb.Append("  <color=#666>|</color>  ");
            sb.Append($"<color={COLOR_LABEL}>{label}</color> <color={COLOR_CURRENT}>{value}</color>");
        }

        // ── Valeurs de milestone par rang — miroir de StatPointSystem ──
        // Attaque
        private float GetMilestoneAttackFlat(int r)
        {
            switch (r) { case 10: return 5f; case 40: return 5f; case 70: return 10f; case 100: return 30f; }
            return 0f;
        }
        private float GetMilestonePrecision(int r)
        {
            switch (r) { case 20: return 10f; case 40: return 10f; case 60: return 15f; case 70: return 15f; case 100: return 30f; }
            return 0f;
        }
        private float GetMilestoneCritChance(int r)
        {
            switch (r) { case 30: return 0.03f; case 80: return 0.03f; case 100: return 0.08f; }
            return 0f;
        }
        private float GetMilestoneCritMult(int r)
        {
            switch (r) { case 50: return 0.10f; case 90: return 0.20f; case 100: return 0.20f; }
            return 0f;
        }
        // Défense
        private float GetMilestoneDefenseFlat(int r)
        {
            switch (r) { case 10: return 10f; case 40: return 10f; case 60: return 20f; case 90: return 35f; case 100: return 25f; }
            return 0f;
        }
        private float GetMilestoneDodge(int r)
        {
            switch (r) { case 20: return 5f; case 40: return 5f; case 80: return 10f; case 100: return 30f; }
            return 0f;
        }
        private float GetMilestoneCritDmgReduc(int r)
        {
            switch (r) { case 30: return 0.02f; case 70: return 0.02f; case 100: return 0.06f; }
            return 0f;
        }
        // Élémentaire
        private float GetMilestoneElementalPts(int r)
        {
            switch (r) { case 10: return 5f; case 20: return 5f; case 30: return 5f; case 60: return 10f; case 70: return 10f; case 80: return 10f; case 90: return 10f; case 100: return 25f; }
            return 0f;
        }
        private float GetMilestoneCooldown(int r)
        {
            switch (r) { case 40: return 0.05f; case 100: return 0.10f; }
            return 0f;
        }
        private float GetMilestoneResistEle(int r)
        {
            switch (r) { case 50: return 0.03f; case 100: return 0.02f; }
            return 0f;
        }
        // HP
        private float GetMilestoneMaxHP(int r)
        {
            switch (r) { case 10: return 100f; case 30: return 200f; case 60: return 300f; case 90: return 500f; case 100: return 1000f; }
            return 0f;
        }
        private float GetMilestoneMaxMP(int r)
        {
            switch (r) { case 10: return 50f; case 50: return 200f; case 80: return 300f; case 90: return 200f; case 100: return 500f; }
            return 0f;
        }
        private float GetMilestoneRegenHP(int r)
        {
            switch (r) { case 20: return 2f; case 70: return 2f; case 100: return 2f; }
            return 0f;
        }
        private float GetMilestoneRegenMP(int r)
        {
            switch (r) { case 40: return 1f; case 70: return 1f; case 100: return 1f; }
            return 0f;
        }

        // ── Construit le résumé des bonus paliers actifs ──────
        private string BuildBonusString(StatPointSystem sp, StatCategory cat)
        {
            var parts = new System.Text.StringBuilder();

            switch (cat)
            {
                case StatCategory.Attack:
                    if (sp.bonusAttackFlat     > 0) Append(parts, $"Bonus : +{sp.bonusAttackFlat:F0} Dmg ");
                    if (sp.bonusPrecision      > 0) Append(parts, $"+{sp.bonusPrecision:F0} Préc.");
                    if (sp.bonusCritChance     > 0) Append(parts, $"+{sp.bonusCritChance * 100f:F0}% CritChance");
                    if (sp.bonusCritMultiplier > 0) Append(parts, $"+{sp.bonusCritMultiplier * 100f:F0}% CritDmg");
                    break;

                case StatCategory.Defense:
                    if (sp.bonusDefenseFlat      > 0) Append(parts, $"Bonus : +{sp.bonusDefenseFlat:F0} Déf. ");
                    if (sp.bonusDodge            > 0) Append(parts, $"+{sp.bonusDodge:F0} Esq.");
                    if (sp.bonusCritDmgReduction > 0) Append(parts, $"-{sp.bonusCritDmgReduction * 100f:F0}% DmgCrit reçus");
                    if (sp.bonusResistAllDef     > 0) Append(parts, $"+{sp.bonusResistAllDef * 100f:F0}% RésistElem.");
                    break;

                case StatCategory.Elemental:
                    if (sp.bonusElementalPoints   > 0) Append(parts, $"Bonus : +{sp.bonusElementalPoints:F0} Pts Élém. ");
                    if (sp.bonusCooldownReduction > 0) Append(parts, $"-{sp.bonusCooldownReduction * 100f:F0}% CD");
                    if (sp.bonusResistAllEle      > 0) Append(parts, $"+{sp.bonusResistAllEle * 100f:F0}% RésistElem.");
                    break;

                case StatCategory.HP:
                    if (sp.bonusMaxHP   > 0) Append(parts, $"Bonus : +{sp.bonusMaxHP:F0} HP ");
                    if (sp.bonusMaxMP   > 0) Append(parts, $"+{sp.bonusMaxMP:F0} MP");
                    if (sp.bonusRegenHP > 0) Append(parts, $"+{sp.bonusRegenHP:F1} Reg.HP/s");
                    if (sp.bonusRegenMP > 0) Append(parts, $"+{sp.bonusRegenMP:F1} Reg.MP/s");
                    break;
            }

            return parts.Length > 0
                ? $"<color=#A09880>{parts}</color>"
                : "<color=#444>Aucun palier débloqué</color>";
        }

        private void Append(System.Text.StringBuilder sb, string value)
        {
            if (sb.Length > 0) sb.Append("  •  ");
            sb.Append(value);
        }
    }

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        if (_player != null)
            _sp = _player.GetComponent<StatPointSystem>();

        BindRow(_rowAttack,    StatCategory.Attack);
        BindRow(_rowDefense,   StatCategory.Defense);
        BindRow(_rowElemental, StatCategory.Elemental);
        BindRow(_rowHP,        StatCategory.HP);

        if (_respecButton != null)
            _respecButton.onClick.AddListener(OnRespecClicked);

        if (_closeButton != null)
            _closeButton.onClick.AddListener(() => gameObject.SetActive(false));

        Refresh();
    }

    // =========================================================
    // BINDING BOUTONS
    // =========================================================

    private void BindRow(StatRow row, StatCategory cat)
    {
        if (row == null) return;
        if (row.btnPlus1  != null) row.btnPlus1.onClick.AddListener(()  => OnInvest(cat, 1));
        if (row.btnPlus10 != null) row.btnPlus10.onClick.AddListener(() => OnInvest(cat, 10));
    }

    // =========================================================
    // ACTIONS
    // =========================================================

    private void OnInvest(StatCategory cat, int amount)
    {
        if (_sp == null) return;
        _sp.InvestPoints(cat, amount);
        Refresh();
    }

    private void OnRespecClicked()
    {
        if (_sp == null) return;
        // TODO : ajouter confirmation ou coût Aeris ici
        _sp.Respec();
        Refresh();
    }

    // =========================================================
    // REFRESH GLOBAL
    // =========================================================

    /// <summary>
    /// Rafraîchit toute l'UI.
    /// Appeler après chaque InvestPoint, level up ou changement d'équipement.
    /// </summary>
    public void Refresh()
    {
        if (_sp == null)
        {
            _player = FindObjectOfType<Player>();
            if (_player != null) _sp = _player.GetComponent<StatPointSystem>();
            if (_sp == null) return;
        }

        // Header — points disponibles
        if (_availablePointsText != null)
        {
            int avail = _sp.availablePoints;
            _availablePointsText.text = avail > 0
                ? $"Points disponibles : <b><color=#F5C542>{avail}</color></b>"
                : "Points disponibles : <color=#666>0</color>";
        }

        // Rows — passe le player pour lire les stats Entity
        _rowAttack?.Refresh(_sp, _player);
        _rowDefense?.Refresh(_sp, _player);
        _rowElemental?.Refresh(_sp, _player);
        _rowHP?.Refresh(_sp, _player);

        // Footer — résumé investissement
        if (_investedSummaryText != null)
        {
            int total    = _sp.totalPointsEarned;
            int avail    = _sp.availablePoints;
            int invested = total - avail;
            _investedSummaryText.text =
                $"Investis : <b>{invested}</b> / {total}  "; 
        }

        if (_respecCostText != null)
            _respecCostText.text = "Respec (PNJ requis)";
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>
    /// Appelé par Player.OnLevelUp() ou depuis n'importe où
    /// pour forcer un refresh de l'UI.
    /// </summary>
    public void OnPlayerLevelUp() => Refresh();
}
