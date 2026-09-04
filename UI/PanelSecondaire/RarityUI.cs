using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// RARITYUI.CS — Panneau du pari de rareté (r-2 → r+7)
// Path : Assets/Scripts/UI/PanelSecondaire/RarityUI.cs
// GDD v3.6 — §3.4.8, ouvert depuis PNJ Rareté (Entities/PNJ.cs)
//
// Calqué sur ForgeUI (même canalisation, même schéma de slot/drag&drop) —
// une seule différence de contenu : 1 seule ligne de ressource + Aeris (pas
// de 2e ressource/item spécial ici, GDD §3.4.8 n'en prévoit pas), et un
// résultat à 3 issues au lieu de 2 (Amélioration/Stagnation/Destruction).
// Le jet + la mutation de rareté sont délégués à RaritySystem.
//
// L'item staged est retiré de InventorySystem DÈS SetStaged() (RarityDropSlot rejette déjà
// tout ce qui n'est pas dans l'inventaire — jamais un équipement porté) — lisibilité +
// remis en inventaire si le slot est remplacé, si le panel ferme sans lancer le pari, ou si
// le joueur re-glisse l'item hors du slot (StagedItemDragSource). À la résolution : Destroyed
// = déjà retiré, rien à faire de plus ; tout autre résultat = l'item survit (rareté
// éventuellement mutée en place par RaritySystem) et revient en inventaire.
// =============================================================

public class RarityUI : MonoBehaviour
{
    public static RarityUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;

    [Header("Slot (glisser arme/armure ici)")]
    public Image slotIcon;
    public Sprite emptySlotSprite;

    [Header("Info objet")]
    public TextMeshProUGUI itemNameText;

    [Header("Ressources — 2 lignes fixes (Aeris + ressource, GDD §3.4.8 n'a pas d'item spécial)")]
    public TextMeshProUGUI resourceLineAeris;
    public TextMeshProUGUI resourceLine1;

    [Header("Action")]
    public Button gambleButton;
    public TextMeshProUGUI resultText;

    [Header("Fermeture")]
    public Button closeButton;

    private const string ColorOk  = "#4CDB57"; // vert — même famille que Renforcée (RarityTier)
    private const string ColorBad = "#E0455F"; // rouge — même famille qu'Ancestrale (RarityTier)

    private InventoryItem _staged;
    private Player        _player;
    private bool          _channeling;

    /// <summary>True pendant la canalisation du pari — RarityDropSlot ignore les drops
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
        if (gambleButton != null) gambleButton.onClick.AddListener(StartGambleChannel);
        if (closeButton  != null) closeButton.onClick.AddListener(Close);
        // Drag inversé — re-glisser l'item hors du slot pour le remettre en inventaire.
        slotIcon?.GetComponent<StagedItemDragSource>()?.Init(() => _staged, OnStagedReturnedToInventory);
        Close();
    }

    /// <summary>Ouvre aussi l'inventaire — le joueur doit pouvoir glisser un item
    /// dans le slot sans avoir à l'ouvrir séparément.</summary>
    public void Open()
    {
        if (panel != null) panel.SetActive(true);
        if (titleText != null) titleText.text = "Rareté";
        InventoryUI.Instance?.Open();
        SetStaged(null);
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé — voir
    /// OnChannelCancelled) avant de fermer, remet l'item staged en inventaire s'il y en a
    /// un (fermer sans lancer le pari ne doit jamais faire "disparaître" l'objet), puis
    /// ferme aussi l'inventaire ouvert avec le panel.</summary>
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

    /// <summary>Appelé par RarityDropSlot.OnDrop() — accepte uniquement Weapon/Armor déjà
    /// vérifiés présents dans l'inventaire (jamais équipé, voir RarityDropSlot). Retire
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
            SetResourceLine(null, 0);
            if (gambleButton != null) gambleButton.interactable = false;
            return;
        }

        int rarityRank = hasWeapon ? _staged.WeaponInstance.rarityRank : _staged.ArmorInstance.rarityRank;

        if (slotIcon != null) slotIcon.sprite = _staged.Icon;
        SetText(itemNameText, _staged.DisplayNameRich);

        if (RaritySystem.Instance == null)
        {
            SetText(resultText, "RaritySystem introuvable en scène.");
            SetAerisLine(0);
            SetResourceLine(null, 0);
            if (gambleButton != null) gambleButton.interactable = false;
            return;
        }

        var table = RaritySystem.Instance.table;
        if (table == null)
        {
            SetText(resultText, "RarityGambleTableData non assignée sur RaritySystem.");
            SetAerisLine(0);
            SetResourceLine(null, 0);
            if (gambleButton != null) gambleButton.interactable = false;
            return;
        }

        var tier = table.GetTier(rarityRank);
        if (tier == null)
        {
            SetText(resultText, "Rareté maximale atteinte.");
            SetAerisLine(0);
            SetResourceLine(null, 0);
            if (gambleButton != null) gambleButton.interactable = false;
            return;
        }

        // Coût FIXE (table), indépendant du tier — seule l'issue du pari varie par rareté.
        bool aerisOk = SetAerisLine(table.aerisCost);
        bool line1Ok = SetResourceLine(table.requiredResource, table.requiredResourceAmount);

        if (gambleButton != null) gambleButton.interactable = aerisOk && line1Ok;
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

    /// <summary>Remplit la ligne ressource (nom + possédé/requis, rouge/vert), la masque si
    /// `resource` est null. Retourne true si la quantité possédée suffit (ou si rien requis).</summary>
    private bool SetResourceLine(ResourceData resource, int needed)
    {
        if (resourceLine1 == null) return true;

        if (resource == null)
        {
            resourceLine1.gameObject.SetActive(false);
            return true;
        }

        resourceLine1.gameObject.SetActive(true);
        int  have   = InventorySystem.Instance?.GetResourceCount(resource) ?? 0;
        bool enough = have >= needed;
        string hex  = enough ? ColorOk : ColorBad;
        string name = resource.displayName.Get(LocalizationManager.CurrentLanguage);
        resourceLine1.text = $"<color={hex}>{name}  {have}/{needed}</color>";
        return enough;
    }

    /// <summary>Lance la canalisation — bouton déjà non-interactable si les conditions
    /// (ressources/tier) ne sont pas réunies, donc pas de re-vérification ici. Le jet et
    /// la consommation ne se produisent qu'à la fin de la barre (ResolveGamble),
    /// jamais au clic — annuler avant la fin (OnChannelCancelled) ne consomme rien.</summary>
    private void StartGambleChannel()
    {
        if (_staged == null || _player == null || _channeling) return;

        _channeling = true;
        if (gambleButton != null) gambleButton.interactable = false;
        SetText(resultText, "");

        ProgressBarUI.Instance?.StartProgress(
            label:        "Pari en cours...",
            duration:     RaritySystem.Instance?.table?.channelDuration ?? 2.5f,
            onComplete:   ResolveGamble,
            onCancel:     OnChannelCancelled,
            type:         ProgressBarUI.BarType.Craft,
            followTarget: _player.transform
        );
    }

    private void ResolveGamble()
    {
        _channeling = false;

        var stagedItem = _staged;
        RarityGambleResult result;
        if (stagedItem?.WeaponInstance != null)
            result = RaritySystem.Instance?.TryGambleWeapon(stagedItem.WeaponInstance, _player) ?? RarityGambleResult.InvalidTarget;
        else if (stagedItem?.ArmorInstance != null)
            result = RaritySystem.Instance?.TryGambleArmor(stagedItem.ArmorInstance, _player) ?? RarityGambleResult.InvalidTarget;
        else
            result = RarityGambleResult.InvalidTarget;

        // L'item a été retiré de l'inventaire dès sa mise en scène (SetStaged) — ici on
        // décide de son sort final : Destroyed = déjà retiré, rien de plus à faire (perdu).
        // Tout autre résultat = l'item survit (rareté éventuellement mutée en place par
        // RaritySystem) et doit revenir dans l'inventaire.
        if (result != RarityGambleResult.Destroyed && stagedItem != null)
            InventorySystem.Instance?.AddItem(stagedItem);

        _staged = null;

        SetText(resultText, result switch
        {
            RarityGambleResult.Improved        => "Amélioration !",
            RarityGambleResult.Stagnated       => "Aucun changement — ressources consommées.",
            RarityGambleResult.Destroyed       => "Objet détruit...",
            RarityGambleResult.MissingResource => "Ressources manquantes.",
            RarityGambleResult.MaxRarity       => "Rareté maximale atteinte.",
            _                                   => "",
        });

        Refresh();
        CharacterPanelUI.Instance?.Refresh();
        InventoryUI.Instance?.RefreshGrid();
    }

    private void OnChannelCancelled()
    {
        _channeling = false;
        SetText(resultText, "Pari annulé.");
        Refresh();
    }

    private void SetText(TextMeshProUGUI label, string value)
        { if (label != null) label.text = value ?? ""; }
}
