using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// PNJWINDOWUI — Fenêtre PNJ partagée à onglets automatiques
// Path : Assets/Scripts/UI/PanelSecondaire/PNJWindowUI.cs
// AetherTree GDD v3.6 — §13.2
//
// Ouverte via DialogueAction.OpenPNJWindow (remplace OpenShop/OpenForge/
// OpenRarity pour tout PNJ composable) — toujours sur l'onglet Boutique par
// défaut (PNJTypeExtensions.GetTabs() le place toujours en premier), les
// autres onglets se changent depuis l'intérieur de cette fenêtre.
//
// N'embarque PAS ShopUI/ForgeUI/RarityUI comme enfants — ce sont toujours
// des panels top-level séparés, cette fenêtre se contente de montrer/cacher
// le bon (Open()/Close() existants, logique interne inchangée) selon
// l'onglet sélectionné. Les onglets sans écran construit affichent un
// placeholder partagé.
// =============================================================

public class PNJWindowUI : MonoBehaviour
{
    public static PNJWindowUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public Button          closeButton;

    [Header("Onglets")]
    public Transform  tabsContent;
    public GameObject tabButtonPrefab;

    [Header("Placeholder — onglets sans écran construit")]
    public GameObject      placeholderPanel;
    public TextMeshProUGUI placeholderText;

    private readonly List<GameObject> _tabButtons = new List<GameObject>();

    private PNJData  _pnjData;
    private Player   _player;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        Close();
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(PNJData pnjData, Player player)
    {
        if (pnjData == null || player == null) return;
        _pnjData = pnjData;
        _player  = player;

        if (panel != null) panel.SetActive(true);
        SetText(titleText, pnjData.pnjName);

        BuildTabs();

        var tabs = pnjData.pnjType.GetTabs();
        if (tabs.Count > 0) ShowTab(tabs[0]); // toujours Boutique — 1er de la liste
    }

    public void Close()
    {
        ShopUI.Instance?.CloseShop();
        ForgeUI.Instance?.Close();
        RarityUI.Instance?.Close();
        CraftPanelUI.Instance?.Close();
        FusionUI.Instance?.Close();
        if (placeholderPanel != null) placeholderPanel.SetActive(false);
        if (panel != null) panel.SetActive(false);

        ClearTabs();
        _pnjData = null;
        _player  = null;
    }

    // =========================================================
    // ONGLETS — 1 prefab, 1 clone par entrée de GetTabs()
    // =========================================================

    private void BuildTabs()
    {
        ClearTabs();
        if (tabButtonPrefab == null || tabsContent == null || _pnjData == null) return;

        foreach (PNJTabID tab in _pnjData.pnjType.GetTabs())
        {
            var go = Instantiate(tabButtonPrefab, tabsContent);
            _tabButtons.Add(go);

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = GetTabLabel(tab);

            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
            var capturedTab = tab;
            btn?.onClick.AddListener(() => ShowTab(capturedTab));
        }
    }

    private void ClearTabs()
    {
        foreach (var go in _tabButtons) if (go != null) Destroy(go);
        _tabButtons.Clear();
    }

    private string GetTabLabel(PNJTabID tab) => tab switch
    {
        PNJTabID.Boutique           => "Boutique",
        PNJTabID.CraftEquipement    => "Craft Équipement",
        PNJTabID.Upgrade            => "Upgrade",
        PNJTabID.Pari               => "Pari",
        PNJTabID.Cuisiner           => "Cuisiner",
        PNJTabID.Fusion             => "Fusion",
        PNJTabID.Bricoler           => "Bricoler",
        PNJTabID.Gemmes             => "Gemmes",
        PNJTabID.CraftCasque        => "Craft",
        PNJTabID.Identification     => "Identification",
        PNJTabID.CraftIntermediaire => "Craft",
        PNJTabID.CraftGantsBottes   => "Craft",
        PNJTabID.CraftBijoux        => "Craft",
        _                           => tab.ToString(),
    };

    // =========================================================
    // CONTENU — montre le panel de l'onglet actif, cache les autres
    // =========================================================

    public void ShowTab(PNJTabID tab)
    {
        bool showShop   = tab == PNJTabID.Boutique;
        bool showForge  = tab == PNJTabID.Upgrade;
        bool showRarity = tab == PNJTabID.Pari;
        bool showCraft  = tab == PNJTabID.CraftEquipement || tab == PNJTabID.Cuisiner ||
                           tab == PNJTabID.Bricoler || tab == PNJTabID.CraftCasque ||
                           tab == PNJTabID.CraftIntermediaire || tab == PNJTabID.CraftGantsBottes ||
                           tab == PNJTabID.CraftBijoux;
        bool showPlaceholder = !showShop && !showForge && !showRarity && !showCraft;

        if (showShop) ShopUI.Instance?.OpenShop(_pnjData, _player);
        else          ShopUI.Instance?.CloseShop();

        if (showForge) ForgeUI.Instance?.Open();
        else            ForgeUI.Instance?.Close();

        if (showRarity) RarityUI.Instance?.Open();
        else             RarityUI.Instance?.Close();

        if (showCraft) CraftPanelUI.Instance?.Open(_pnjData, _player, ToStation(tab));
        else            CraftPanelUI.Instance?.Close();

        if (placeholderPanel != null) placeholderPanel.SetActive(showPlaceholder);
        if (showPlaceholder) SetText(placeholderText, $"{GetTabLabel(tab)} — bientôt disponible");

        // Chaque sous-panel ouvre/ferme l'inventaire comme effet de bord (Close() de l'un peut
        // annuler l'Open() d'un autre appelé juste avant) — force l'état correct en dernier,
        // une fois pour toutes, indépendamment de l'ordre des appels ci-dessus.
        InventoryUI.Instance?.Open();

        // La toute 1ère activation d'un panel UI dans la scène peut ne pas s'afficher avant la
        // fin de la frame (Unity n'a pas encore calculé son layout/canvas) — force le rebuild
        // immédiatement au lieu d'attendre le cycle naturel, évite le "rien ne s'affiche" au
        // premier clic sur un onglet qui n'a encore jamais été montré depuis le lancement.
        Canvas.ForceUpdateCanvases();
    }

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }

    /// <summary>PNJTabID (noms FR, onglets UI) et CraftStationType (noms EN, technique/Inspector)
    /// sont deux enums distincts, pas ordinal-compatibles — mapping par nom.</summary>
    private CraftStationType ToStation(PNJTabID tab) => tab switch
    {
        PNJTabID.CraftEquipement    => CraftStationType.CraftWeaponArmor,
        PNJTabID.Cuisiner           => CraftStationType.CraftFoodPotion,
        PNJTabID.Bricoler           => CraftStationType.CraftDecor,
        PNJTabID.CraftCasque        => CraftStationType.CraftHelmet,
        PNJTabID.CraftIntermediaire => CraftStationType.CraftIntermediate,
        PNJTabID.CraftGantsBottes   => CraftStationType.CraftGlovesBoots,
        PNJTabID.CraftBijoux        => CraftStationType.CraftJewelry,
        _                           => CraftStationType.CraftIntermediate,
    };
}
