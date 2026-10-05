using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// =============================================================
// SKILLDRAGDROP.CS — Drag & Drop skill : Library → SkillBar / PassifBar
// Path : Assets/Scripts/UI/SkillDragDrop.cs
// AetherTree GDD v30
//
// Trois composants :
//   SkillDragSource    — sur chaque entrée SkillData de SkillLibraryUI (Actifs/Ultimes)
//   PassiveDragSource  — sur chaque entrée PassiveSkillData de SkillLibraryUI (Passifs)
//   SkillDropTarget    — sur chaque slot de SkillBarUI ET PassifBarUI
//
// Compatibilité par skillType (GDD §8.1) :
//   SlotType.BasicAttack → SkillType.BasicAttack uniquement
//   SlotType.Active      → SkillType.Active uniquement
//   SlotType.Ultimate    → SkillType.Ultimate uniquement
//   SlotType.Passive     → PassiveSkillData (pas de sous-type à matcher, tout drop valide)
// + compatibleWeapons (tous les slots SkillBar, pas Passive) : le skill doit aussi matcher
//   l'arme actuellement équipée (SkillData.IsCompatibleWith) — demande Florian 2026-10-03.
//
// Setup automatique :
//   SkillBarUI.Awake()  → SlotType auto selon index (0=BasicAttack, 1-8=Active, 9=Ultimate)
//   PassifBarUI.Awake() → SlotType.Passive sur chaque slot
//
// Protection CD / combat (2026-10-05, Florian) — aucune modification de barre (swap, drop
// Library, retrait) n'est permise si :
//   1. Player.CombatActive == true (vrai au cast/dégâts subis, faux après combatExitDelay sans
//      action — signal déjà existant, voir Entities/Player.cs).
//   2. Le SLOT concerné est en cooldown — SkillBar.GetCooldownRemaining(slot) pour les slots
//      actifs/BasicAttack/Ultimate, PassiveSkillSystem.GetCooldownRemaining(passive) pour les
//      slots passifs. EXCEPTION explicite : un passif PassiveTriggerType.OnInterval réutilise
//      son cooldown comme intervalle de proc permanent ("toutes les 60s") — son cooldown est
//      donc quasi-TOUJOURS > 0 tant qu'il est équipé, et un verrou naïf le rendrait indéplaçable
//      pour de bon. Exempté explicitement de la règle CD (reste soumis à la règle combat).
// =============================================================

// =============================================================
// SLOT TYPE — détermine ce qu'un slot accepte
// =============================================================
public enum SlotType
{
    BasicAttack, // Slot 0 SkillBar
    Active,      // Slots 1-8 SkillBar
    Ultimate,    // Slot 9 SkillBar
    Passive,     // Slots P1/P2/P3 PassifBar — PassiveSkillData, pas SkillData
}

// =============================================================
// SKILLDRAGSOURCE
// =============================================================
public class SkillDragSource : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public SkillData skill;

    private static SkillData _dragging;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (skill == null) return;
        // Exige la SkillLibrary ouverte — même geste "volontaire" que le clic droit (voir
        // SkillSlotUI.OnPointerClick) : no-op pour les entrées DANS la Library (toujours ouverte
        // si visibles), mais bloque un drag accidentel depuis un slot SkillBar en plein combat —
        // demande Florian.
        if (SkillLibraryUI.Instance == null || !SkillLibraryUI.Instance.IsOpen) return;
        if (SkillDropTarget.IsPlayerInCombat()) return; // pas de changement de barre en combat
        _dragging = skill;
        Color tint = skill.icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        DragGhost.Begin(skill.icon, tint, new Vector2(48f, 48f), eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragGhost.Move(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = null;
        DragGhost.End();
    }

    public static SkillData CurrentDragging => _dragging;
}

// =============================================================
// PASSIVEDRAGSOURCE — mirroring SkillDragSource, pour PassiveSkillData
// (drag depuis l'onglet Passifs de SkillLibraryUI vers PassifBarUI)
// =============================================================
public class PassiveDragSource : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public PassiveSkillData passive;

    private static PassiveSkillData _dragging;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (passive == null) return;
        if (SkillLibraryUI.Instance == null || !SkillLibraryUI.Instance.IsOpen) return;
        if (SkillDropTarget.IsPlayerInCombat()) return; // pas de changement de barre en combat
        _dragging = passive;
        Color tint = passive.icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        DragGhost.Begin(passive.icon, tint, new Vector2(48f, 48f), eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragGhost.Move(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = null;
        DragGhost.End();
    }

    public static PassiveSkillData CurrentDragging => _dragging;
}

// =============================================================
// SKILLDROP TARGET
// =============================================================
public class SkillDropTarget : MonoBehaviour,
    IDropHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public int      slotIndex = -1;
    [HideInInspector] public SlotType slotType  = SlotType.Active;

    private Image  _slotImage;
    private Color  _originalColor;
    private Player _player;

    private static readonly Color HighlightValid   = new Color(0.4f, 0.9f, 0.4f, 0.6f);
    private static readonly Color HighlightInvalid = new Color(0.9f, 0.3f, 0.3f, 0.6f);

    // Drag ORIGINE depuis un slot déjà rempli (échange slot↔slot, SkillBar et PassifBar) — même
    // garde "Library ouverte" que SkillDragSource/PassiveDragSource (Florian, 2026-10-05 : garder
    // le garde-fou contre un drag accidentel en combat). Un seul slot-source actif à la fois,
    // mutuellement exclusif avec SkillDragSource/PassiveDragSource.CurrentDragging (une seule
    // séquence de drag possible pour un pointeur donné).
    private static SkillDropTarget _draggingSlot;

    private void Awake()
    {
        _slotImage = GetComponent<Image>();
        if (_slotImage != null) _originalColor = _slotImage.color;
    }

    // ── Protection CD / combat (voir commentaire d'en-tête du fichier) ────────
    private static Player _cachedPlayer;

    public static bool IsPlayerInCombat()
    {
        if (_cachedPlayer == null) _cachedPlayer = FindObjectOfType<Player>();
        return _cachedPlayer != null && _cachedPlayer.CombatActive;
    }

    /// <summary>True si CE slot ne peut pas être modifié maintenant — combat actif (bloque tout),
    /// ou cooldown en cours (actif : SkillBar.GetCooldownRemaining(slot) ; passif : CD du passif
    /// équipé, SAUF OnInterval, exempté — voir commentaire d'en-tête).</summary>
    private bool IsLocked()
    {
        if (IsPlayerInCombat()) return true;
        if (slotIndex < 0) return false;

        if (slotType == SlotType.Passive)
        {
            var passive = PassifBarUI.Instance?.GetPassifAtSlot(slotIndex);
            if (passive == null) return false;
            if (passive.triggerType == PassiveTriggerType.OnInterval) return false;
            return PassiveSkillSystem.Instance != null
                && PassiveSkillSystem.Instance.GetCooldownRemaining(passive) > 0f;
        }

        return SkillBar.Instance != null && SkillBar.Instance.GetCooldownRemaining(slotIndex) > 0f;
    }

    // ── Drag ORIGINE (échange slot↔slot) ───────────────────────
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (SkillLibraryUI.Instance == null || !SkillLibraryUI.Instance.IsOpen) return;
        if (slotIndex < 0) return;
        if (IsLocked()) return;

        Sprite icon;
        if (slotType == SlotType.Passive)
        {
            var passive = PassifBarUI.Instance?.GetPassifAtSlot(slotIndex);
            if (passive == null) return;
            icon = passive.icon;
        }
        else
        {
            var skill = SkillBar.Instance?.GetSkillAtSlot(slotIndex);
            if (skill == null) return;
            icon = skill.icon;
        }

        _draggingSlot = this;
        Color tint = icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        DragGhost.Begin(icon, tint, new Vector2(48f, 48f), eventData);
    }

    public void OnDrag(PointerEventData eventData) => DragGhost.Move(eventData);

    public void OnEndDrag(PointerEventData eventData)
    {
        _draggingSlot = null;
        DragGhost.End();
    }

    /// <summary>True si l'échange source→dest est valide (SkillBar↔SkillBar ou Passif↔Passif,
    /// jamais mélangé, chaque skill compatible avec le slot DE L'AUTRE) — lecture seule, utilisé
    /// par OnPointerEnter (highlight) ET HandleSlotSwap (exécution), pour ne jamais afficher un
    /// highlight vert qui refuserait au drop.</summary>
    private static bool WouldSwapSucceed(SkillDropTarget source, SkillDropTarget dest)
    {
        if (source == null || dest == null || source == dest) return false;
        if (source.slotIndex < 0 || dest.slotIndex < 0) return false;
        if (source.IsLocked() || dest.IsLocked()) return false; // combat/CD — voir en-tête fichier

        bool sourcePassive = source.slotType == SlotType.Passive;
        bool destPassive   = dest.slotType   == SlotType.Passive;
        if (sourcePassive != destPassive) return false; // pas de mélange Actif/Passif

        if (sourcePassive)
        {
            var a = PassifBarUI.Instance?.GetPassifAtSlot(source.slotIndex);
            var b = PassifBarUI.Instance?.GetPassifAtSlot(dest.slotIndex);
            WeaponType family = dest.GetEquippedWeaponFamily();
            if (a != null && !a.IsCompatibleWith(family)) return false;
            if (b != null && !b.IsCompatibleWith(family)) return false;
            return true;
        }

        var skillA = SkillBar.Instance?.GetSkillAtSlot(source.slotIndex);
        var skillB = SkillBar.Instance?.GetSkillAtSlot(dest.slotIndex);
        if (skillA != null && !dest.IsCompatible(skillA))   return false;
        if (skillB != null && !source.IsCompatible(skillB)) return false;
        return true;
    }

    /// <summary>Échange le contenu de deux slots — annule tout (tout ou rien) si WouldSwapSucceed
    /// renvoie false, pas de perte silencieuse d'un skill.</summary>
    private static void HandleSlotSwap(SkillDropTarget source, SkillDropTarget dest)
    {
        if (!WouldSwapSucceed(source, dest)) return;

        if (source.slotType == SlotType.Passive)
        {
            // SwapPassifs, PAS 2x SetPassifAtSlot — les deux passifs restent équipés (juste
            // relocalisés), SetPassifAtSlot révoquerait à tort leurs buffs actifs au passage
            // (voir son commentaire, PassifBarUI.cs — Florian, 2026-10-05).
            PassifBarUI.Instance?.SwapPassifs(source.slotIndex, dest.slotIndex);
            return;
        }

        var skillA = SkillBar.Instance?.GetSkillAtSlot(source.slotIndex);
        var skillB = SkillBar.Instance?.GetSkillAtSlot(dest.slotIndex);
        SkillBar.Instance?.SetSkillAtSlot(dest.slotIndex,   skillA);
        SkillBar.Instance?.SetSkillAtSlot(source.slotIndex, skillB);
    }

    // ── Compatibilité slot / skill ─────────────────────────────
    /// <summary>
    /// SkillType (slot BasicAttack/Active/Ultimate matche le sous-type du skill) ET
    /// compatibleWeapons (arme actuellement équipée) — demande Florian : un skill lié à une
    /// arme ne doit s'équiper dans AUCUN slot SkillBar si l'arme en main ne matche pas, pas
    /// seulement le slot BasicAttack auto-géré par Player.RefreshSlot0(). Ne concerne QUE les
    /// slots SkillBar — SlotType.Passive traité à part dans OnDrop/OnPointerEnter (drag
    /// PassiveSkillData, pas SkillData, aucun sous-type ni arme à matcher).
    /// </summary>
    private bool IsCompatible(SkillData skill)
    {
        if (skill == null) return false;

        bool typeMatch = slotType switch
        {
            SlotType.BasicAttack => skill.skillType == SkillType.BasicAttack,
            SlotType.Active      => skill.skillType == SkillType.Active,
            SlotType.Ultimate    => skill.skillType == SkillType.Ultimate,
            _                    => false,
        };
        if (!typeMatch) return false;

        return skill.IsCompatibleWith(GetEquippedWeaponFamily());
    }

    /// <summary>Famille de l'arme actuellement équipée (UnArmed si aucune) — même résolution
    /// pour le check SkillData (IsCompatible ci-dessus) et PassiveSkillData (OnDrop/
    /// OnPointerEnter, slot Passive).</summary>
    private WeaponType GetEquippedWeaponFamily()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        return (_player?.equippedWeapon?.weaponType ?? WeaponType.UnArmed).GetStartingFamily();
    }

    // ── Drop ──────────────────────────────────────────────────
    public void OnDrop(PointerEventData eventData)
    {
        ResetHighlight();
        if (slotIndex < 0) return;

        // ── Échange slot↔slot — priorité sur un drop venant de la Library ──
        if (_draggingSlot != null)
        {
            HandleSlotSwap(_draggingSlot, this);
            return;
        }

        // Drop venant de la Library (pas un swap) — même protection CD/combat sur CE slot
        // destination (voir commentaire d'en-tête fichier).
        if (IsLocked()) return;

        // ── Slot passif — chemin séparé, PassiveSkillData pas SkillData ──
        if (slotType == SlotType.Passive)
        {
            PassiveSkillData droppedPassive = PassiveDragSource.CurrentDragging;
            if (droppedPassive == null) return;

            if (!droppedPassive.IsCompatibleWith(GetEquippedWeaponFamily()))
            {
                Debug.LogWarning($"[DRAG] {droppedPassive.name} incompatible avec l'arme équipée — drop annulé.");
                return;
            }

            // Anti-doublon sur les 3 slots équipés
            if (PassifBarUI.Instance != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i == slotIndex) continue;
                    if (PassifBarUI.Instance.GetPassifAtSlot(i) == droppedPassive)
                    {
                        Debug.LogWarning($"[DRAG] {droppedPassive.name} est déjà équipé en slot {i} — drop annulé.");
                        return;
                    }
                }
            }

            PassifBarUI.Instance?.SetPassifAtSlot(slotIndex, droppedPassive);
            Debug.Log($"[DRAG] {droppedPassive.name} → Passive slot {slotIndex}");
            return;
        }

        SkillData dropped = SkillDragSource.CurrentDragging;
        if (dropped == null) return;

        if (!IsCompatible(dropped))
        {
            Debug.LogWarning($"[DRAG] {dropped.name} ({dropped.skillType}) " +
                             $"incompatible avec slot {slotIndex} ({slotType}).");
            return;
        }

        // ── Anti-doublon : interdit de placer un skill déjà présent dans la SkillBar ──
        if (SkillBar.Instance != null)
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == slotIndex) continue; // le slot de destination ne compte pas
                if (SkillBar.Instance.GetSkillAtSlot(i) == dropped)
                {
                    Debug.LogWarning($"[DRAG] {dropped.name} est déjà équipé en slot {i} — drop annulé.");
                    return;
                }
            }
        }

        SkillBar.Instance?.SetSkillAtSlot(slotIndex, dropped);
        Debug.Log($"[DRAG] {dropped.name} → {slotType} slot {slotIndex}");
    }

    // ── Highlight survol ──────────────────────────────────────
    public void OnPointerEnter(PointerEventData eventData)
    {
        // ── Échange slot↔slot — chemin séparé des deux ci-dessous ──
        if (_draggingSlot != null)
        {
            if (_slotImage != null)
                _slotImage.color = WouldSwapSucceed(_draggingSlot, this) ? HighlightValid : HighlightInvalid;
            return;
        }

        // Drop Library sur un slot verrouillé (combat/CD) — highlight invalide direct, même si la
        // compatibilité serait par ailleurs correcte.
        if (IsLocked())
        {
            if (_slotImage != null) _slotImage.color = HighlightInvalid;
            return;
        }

        // ── Slot passif — chemin séparé ──
        if (slotType == SlotType.Passive)
        {
            PassiveSkillData draggingPassive = PassiveDragSource.CurrentDragging;
            if (draggingPassive == null) return;

            bool passiveCompatible = draggingPassive.IsCompatibleWith(GetEquippedWeaponFamily());
            if (passiveCompatible && PassifBarUI.Instance != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i == slotIndex) continue;
                    if (PassifBarUI.Instance.GetPassifAtSlot(i) == draggingPassive)
                    {
                        passiveCompatible = false;
                        break;
                    }
                }
            }

            if (_slotImage != null)
                _slotImage.color = passiveCompatible ? HighlightValid : HighlightInvalid;
            return;
        }

        SkillData dragging = SkillDragSource.CurrentDragging;
        if (dragging == null) return;

        bool compatible = IsCompatible(dragging);

        // Vérifie aussi le doublon pour que le highlight soit cohérent avec le drop
        if (compatible && SkillBar.Instance != null)
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == slotIndex) continue;
                if (SkillBar.Instance.GetSkillAtSlot(i) == dragging)
                {
                    compatible = false;
                    break;
                }
            }
        }

        if (_slotImage != null)
            _slotImage.color = compatible ? HighlightValid : HighlightInvalid;
    }

    public void OnPointerExit(PointerEventData eventData) => ResetHighlight();

    private void ResetHighlight()
    {
        if (_slotImage != null) _slotImage.color = _originalColor;
    }
}
