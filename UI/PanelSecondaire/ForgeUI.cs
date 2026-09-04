using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// FORGEUI.CS — Panneau d'amélioration (+0 → +10)
// Path : Assets/Scripts/UI/PanelSecondaire/ForgeUI.cs
// GDD v3.6 — §5.14, ouvert depuis PNJ Blacksmith (Entities/PNJ.cs)
//
// Un seul slot, accepte Arme OU Armure venant UNIQUEMENT de l'inventaire —
// un équipement porté (CharacterPanel) est rejeté par ForgeDropSlot. L'item
// est retiré de InventorySystem DÈS SetStaged() (lisibilité + rend
// impossible de le poser ailleurs), remis en inventaire si le slot est
// remplacé, si le panel ferme sans lancer l'upgrade, ou si le joueur
// re-glisse l'item hors du slot (StagedItemDragSource). L'upgrade ne
// détruit jamais l'item (succès = amélioré, échec = ressources perdues
// mais objet inchangé) — il revient donc TOUJOURS en inventaire après
// résolution, contrairement à RarityUI où Destroyed est possible.
//
// Affiche les ressources requises pour le palier SUIVANT (2 lignes fixes —
// le GDD n'a jamais plus de 1 ressource de base + 1 item spécial par
// palier), rouge/vert selon ce que possède le joueur. Bouton actif
// seulement si tout est vert. Le jet de succès/consommation est délégué à
// UpgradeSystem — ce script ne fait qu'afficher et déclencher l'appel.
// =============================================================

public class ForgeUI : MonoBehaviour
{
    public static ForgeUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;

    [Header("Slot (glisser arme/armure ici)")]
    public Image slotIcon;
    public Sprite emptySlotSprite;

    [Header("Info objet")]
    // Niveau fusionné DANS le même texte que le nom ("Sacrée Épée Courte (+5)") —
    // un champ séparé se ferait décaler/cacher selon la longueur du nom.
    public TextMeshProUGUI itemNameText;

    [Header("Ressources — 3 lignes fixes (Aeris + base + item spécial, jamais plus au GDD)")]
    public TextMeshProUGUI resourceLineAeris;
    public TextMeshProUGUI resourceLine1;
    public TextMeshProUGUI resourceLine2;

    [Header("Action")]
    public Button upgradeButton;
    public TextMeshProUGUI resultText;

    [Header("Fermeture")]
    public Button closeButton;

    private const string ColorOk  = "#4CDB57"; // vert — même famille que Renforcée (RarityTier)
    private const string ColorBad = "#E0455F"; // rouge — même famille qu'Ancestrale (RarityTier)

    private InventoryItem _staged;
    private Player        _player;
    private bool          _channeling;

    /// <summary>True pendant la canalisation d'upgrade — ForgeDropSlot ignore les drops
    /// tant que c'est actif (pas de changement d'objet en cours de résolution).</summary>
    public bool IsChanneling => _channeling;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        if (upgradeButton != null) upgradeButton.onClick.AddListener(StartUpgradeChannel);
        if (closeButton   != null) closeButton.onClick.AddListener(Close);
        // Drag inversé — re-glisser l'item hors du slot pour le remettre en inventaire.
        slotIcon?.GetComponent<StagedItemDragSource>()?.Init(() => _staged, OnStagedReturnedToInventory);
        Close();
    }

    /// <summary>Ouvre aussi l'inventaire — le joueur doit pouvoir glisser un item
    /// dans le slot sans avoir à l'ouvrir séparément.</summary>
    public void Open()
    {
        if (panel != null) panel.SetActive(true);
        if (titleText != null) titleText.text = "Forge";
        InventoryUI.Instance?.Open();
        SetStaged(null);
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé — voir
    /// OnChannelCancelled) avant de fermer, remet l'item staged en inventaire s'il y en a
    /// un (fermer sans lancer l'upgrade ne doit jamais faire "disparaître" l'objet), puis
    /// ferme aussi l'inventaire ouvert avec la forge.</summary>
    public void Close()
    {
        if (_channeling) ProgressBarUI.Instance?.Cancel();
        if (_staged != null)
        {
            InventorySystem.Instance?.AddItem(_staged);
            _staged = null;
        }
        if (panel != null) panel.SetActive(false);
        InventoryUI.Instance?.Close();
    }

    /// <summary>Appelé par ForgeDropSlot.OnDrop() — accepte uniquement Weapon/Armor déjà
    /// vérifiés présents dans l'inventaire (jamais équipé, voir ForgeDropSlot). Retire
    /// l'item dès qu'il est posé (lisibilité + rend impossible de le reposer ailleurs — il
    /// n'est plus dans InventorySystem._items), remet l'ancien staged en inventaire avant
    /// de le remplacer.</summary>
    public void SetStaged(InventoryItem item)
    {
        if (_staged != null && _staged != item)
            InventorySystem.Instance?.AddItem(_staged);

        _staged = item;

        if (_staged != null)
            InventorySystem.Instance?.RemoveItem(_staged);

        SetText(resultText, "");
        Refresh();
    }

    /// <summary>Callback de StagedItemDragSource — le joueur a re-glissé l'item du slot vers
    /// une cellule d'inventaire (le retrait fait par SetStaged est annulé ici).</summary>
    private void OnStagedReturnedToInventory()
    {
        if (_staged == null) return;
        InventorySystem.Instance?.AddItem(_staged);
        _staged = null;
        SetText(resultText, "");
        Refresh();
    }

    private void Refresh()
    {
        bool hasWeapon = _staged?.WeaponInstance != null;
        bool hasArmor  = _staged?.ArmorInstance  != null;

        if (!hasWeapon && !hasArmor)
        {
            if (slotIcon != null) slotIcon.sprite = emptySlotSprite;
            SetText(itemNameText, "");
            SetAerisLine(0);
            SetResourceLine(resourceLine1, null, 0);
            SetResourceLine(resourceLine2, null, 0);
            if (upgradeButton != null) upgradeButton.interactable = false;
            return;
        }

        int currentLevel = hasWeapon ? _staged.WeaponInstance.upgradeLevel : _staged.ArmorInstance.upgradeLevel;

        if (slotIcon != null) slotIcon.sprite = _staged.Icon;
        SetText(itemNameText, _staged.DisplayNameRich);

        if (UpgradeSystem.Instance == null)
        {
            SetText(resultText, "UpgradeSystem introuvable en scène.");
            SetAerisLine(0);
            SetResourceLine(resourceLine1, null, 0);
            SetResourceLine(resourceLine2, null, 0);
            if (upgradeButton != null) upgradeButton.interactable = false;
            return;
        }

        var table = UpgradeSystem.Instance.table;
        if (table == null)
        {
            SetText(resultText, "UpgradeTableData non assignée sur UpgradeSystem.");
            SetAerisLine(0);
            SetResourceLine(resourceLine1, null, 0);
            SetResourceLine(resourceLine2, null, 0);
            if (upgradeButton != null) upgradeButton.interactable = false;
            return;
        }

        var tier = table.GetTier(currentLevel);
        if (tier == null)
        {
            SetText(resultText, "Niveau maximum atteint.");
            SetAerisLine(0);
            SetResourceLine(resourceLine1, null, 0);
            SetResourceLine(resourceLine2, null, 0);
            if (upgradeButton != null) upgradeButton.interactable = false;
            return;
        }

        bool aerisOk = SetAerisLine(tier.aerisCost);
        bool line1Ok = SetResourceLine(resourceLine1, tier.requiredResource, tier.requiredResourceAmount);
        bool line2Ok = SetResourceLine(resourceLine2, tier.requiredSpecialItem, tier.requiredSpecialItemAmount);

        if (upgradeButton != null) upgradeButton.interactable = aerisOk && line1Ok && line2Ok;
    }

    /// <summary>Ligne Aeris — masquée si le palier ne coûte rien (aerisCost <= 0).
    /// Retourne true si le joueur a assez (ou si rien requis).</summary>
    private bool SetAerisLine(int cost)
    {
        if (resourceLineAeris == null) return true;

        if (cost <= 0)
        {
            resourceLineAeris.gameObject.SetActive(false);
            return true;
        }

        resourceLineAeris.gameObject.SetActive(true);
        int  have   = AerisSystem.Instance?.Aeris ?? 0;
        bool enough = have >= cost;
        string hex  = enough ? ColorOk : ColorBad;
        resourceLineAeris.text = $"<color={hex}>Aeris  {have}/{cost}</color>";
        return enough;
    }

    /// <summary>Remplit une ligne ressource (nom + possédé/requis, rouge/vert), la masque si
    /// `resource` est null. Retourne true si la quantité possédée suffit (ou si rien requis).</summary>
    private bool SetResourceLine(TextMeshProUGUI line, ResourceData resource, int needed)
    {
        if (line == null) return true;

        if (resource == null)
        {
            line.gameObject.SetActive(false);
            return true;
        }

        line.gameObject.SetActive(true);
        int  have   = InventorySystem.Instance?.GetResourceCount(resource) ?? 0;
        bool enough = have >= needed;
        string hex  = enough ? ColorOk : ColorBad;
        string name = resource.displayName.Get(LocalizationManager.CurrentLanguage);
        line.text   = $"<color={hex}>{name}  {have}/{needed}</color>";
        return enough;
    }

    /// <summary>Lance la canalisation — bouton déjà non-interactable si les conditions
    /// (ressources/tier) ne sont pas réunies, donc pas de re-vérification ici. Le jet et
    /// la consommation ne se produisent qu'à la fin de la barre (ResolveUpgrade),
    /// jamais au clic — annuler avant la fin (OnChannelCancelled) ne consomme rien.</summary>
    private void StartUpgradeChannel()
    {
        if (_staged == null || _player == null || _channeling) return;

        _channeling = true;
        if (upgradeButton != null) upgradeButton.interactable = false;
        SetText(resultText, "");

        ProgressBarUI.Instance?.StartProgress(
            label:        "Amélioration...",
            duration:     UpgradeSystem.Instance?.table?.channelDuration ?? 2.5f,
            onComplete:   ResolveUpgrade,
            onCancel:     OnChannelCancelled,
            type:         ProgressBarUI.BarType.Craft,
            followTarget: _player.transform
        );
    }

    private void ResolveUpgrade()
    {
        _channeling = false;

        var stagedItem = _staged;
        UpgradeResult result;
        if (stagedItem?.WeaponInstance != null)
            result = UpgradeSystem.Instance?.TryUpgradeWeapon(stagedItem.WeaponInstance, _player) ?? UpgradeResult.InvalidTarget;
        else if (stagedItem?.ArmorInstance != null)
            result = UpgradeSystem.Instance?.TryUpgradeArmor(stagedItem.ArmorInstance, _player) ?? UpgradeResult.InvalidTarget;
        else
            result = UpgradeResult.InvalidTarget;

        // Upgrade ne détruit jamais l'item (succès = amélioré, échec = ressources perdues
        // mais objet inchangé) — il revient toujours à l'inventaire, retiré depuis sa mise
        // en scène (SetStaged).
        if (stagedItem != null)
            InventorySystem.Instance?.AddItem(stagedItem);

        _staged = null;

        SetText(resultText, result switch
        {
            UpgradeResult.Success         => "Succès !",
            UpgradeResult.Failure         => "Échec — ressources consommées, objet inchangé.",
            UpgradeResult.MissingResource => "Ressources manquantes.",
            UpgradeResult.MaxLevel        => "Niveau maximum atteint.",
            _                             => "",
        });

        Refresh();
        CharacterPanelUI.Instance?.Refresh();
        InventoryUI.Instance?.RefreshGrid();
    }

    private void OnChannelCancelled()
    {
        _channeling = false;
        SetText(resultText, "Amélioration annulée.");
        Refresh();
    }

    private void SetText(TextMeshProUGUI label, string value)
        { if (label != null) label.text = value ?? ""; }
}
