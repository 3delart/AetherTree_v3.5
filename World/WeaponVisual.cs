using UnityEngine;

// =============================================================
// WEAPONVISUAL.CS — Attache le prefab de l'arme équipée à la main
// (combat) ou au rangement (dos/hanche, hors combat).
// Path : Assets/Scripts/World/WeaponVisual.cs
//
// Sockets = enfants vides positionnés à la main / au rangement dans
// l'Editor (offset propre à chaque prefab d'arme — pas de calcul ici,
// juste un reparent avec transform local à zéro. Le prefab doit être
// modélisé avec son pivot/orientation cohérents avec ça).
//
// Bascule sur Player.CombatActive — même flag que PlayerAnimatorController
// pour InCombat (armée/désarmée) et SkillBar pour le lock MultiHit.
// =============================================================

public class WeaponVisual : MonoBehaviour
{
    [Header("Sockets (enfants des bones, position/rotation à ajuster dans l'Editor)")]
    [SerializeField] private Transform handSocket;
    [SerializeField] private Transform sheathSocket;

    private Player     _player;
    private GameObject _weaponInstance;
    private bool       _lastCombatActive;

    private void Awake()
    {
        _player = GetComponent<Player>();

        if (handSocket == null || sheathSocket == null)
            Debug.LogWarning("[WeaponVisual] handSocket/sheathSocket non assignés — " +
                              "l'arme équipée ne s'affichera pas.", this);
    }

    private void Update()
    {
        if (_player == null || _weaponInstance == null) return;

        bool combat = _player.CombatActive;
        if (combat == _lastCombatActive) return;
        _lastCombatActive = combat;

        Transform target = combat ? handSocket : sheathSocket;
        if (target == null) return;

        _weaponInstance.transform.SetParent(target, false);
        _weaponInstance.transform.localPosition = Vector3.zero;
        _weaponInstance.transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Appelé par Player.EquipWeapon()/UnequipWeapon(). data == null → déséquipe
    /// (détruit le visuel sans en recréer un).
    /// </summary>
    public void RefreshWeapon(WeaponData data)
    {
        if (_weaponInstance != null)
        {
            Destroy(_weaponInstance);
            _weaponInstance = null;
        }

        if (data == null || data.weaponPrefab == null) return;

        _lastCombatActive = _player != null && _player.CombatActive;
        Transform socket  = _lastCombatActive ? handSocket : sheathSocket;
        if (socket == null) return;

        _weaponInstance = Instantiate(data.weaponPrefab, socket);
        _weaponInstance.transform.localPosition = Vector3.zero;
        _weaponInstance.transform.localRotation = Quaternion.identity;
    }
}
