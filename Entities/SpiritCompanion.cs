using UnityEngine;

// =============================================================
// SpiritCompanion.cs — visuel d'esprit flottant qui suit le joueur
// Path : Assets/Scripts/Entities/SpiritCompanion.cs
// AetherTree GDD v3.6 — §5.8 (2026-09-06)
//
// Ajouté automatiquement (AddComponent) sur l'instance spawnée par
// Player.SpawnSpiritCompanion() — le prefab (SpiritData.spiritPrefab) n'a besoin d'aucun
// script dessus, juste le mesh/FBX. Suivi procédural (lerp + bob sinusoïdal), pas de
// NavMeshAgent ni d'Animator requis — traverse les obstacles, look "familier magique".
//
// Comportement : joueur en mouvement → suit derrière/à côté (offset fixe, espace local).
// Joueur à l'arrêt → orbite autour de lui (cercle horizontal, vitesse angulaire constante).
// Détection du mouvement auto-contenue (delta de position du target d'une frame à l'autre) —
// aucune dépendance à Player/AFK, le compagnon observe juste sa cible.
//
// Réglages par défaut ici ; pour un esprit qui doit flotter différemment (plus vite/haut),
// ajouter ce même component DIRECTEMENT sur le prefab avec des valeurs personnalisées —
// Init() ne touche jamais les réglages visuels, seulement _target.
// =============================================================
public class SpiritCompanion : MonoBehaviour
{
    [Header("Suivi (joueur en mouvement)")]
    [Tooltip("Décalage par rapport au joueur, en espace LOCAL du joueur (x=droite, y=hauteur, z=avant).")]
    public Vector3 offset = new Vector3(0.8f, 1.8f, -0.6f);
    [Tooltip("Vitesse de rattrapage de la position cible — plus haut = suit de plus près.")]
    public float followSmoothing = 5f;

    [Header("Orbite (joueur à l'arrêt)")]
    public float orbitRadius = 1.5f;
    public float orbitHeight = 1.8f;
    [Tooltip("Vitesse de rotation autour du joueur, en degrés/seconde.")]
    public float orbitSpeed = 60f;
    [Tooltip("Vitesse du joueur en dessous de laquelle il est considéré à l'arrêt (unités/sec).")]
    public float idleSpeedThreshold = 0.15f;

    [Header("Flottaison")]
    public float bobHeight = 0.15f;
    public float bobSpeed = 2f;

    private Transform _target;
    private float _bobTimer;
    private float _orbitAngle;
    private Vector3 _lastTargetPos;
    private bool _hasLastPos;

    /// <summary>Appelé une fois juste après Instantiate — ne touche jamais les réglages
    /// visuels (offset/orbite/bob/smoothing), seulement la cible à suivre.</summary>
    public void Init(Transform target)
    {
        _target = target;
        _bobTimer = Random.Range(0f, Mathf.PI * 2f); // déphasage — plusieurs esprits ne bobbent pas en sync
        _orbitAngle = Random.Range(0f, 360f);
        _hasLastPos = false;
    }

    private void Update()
    {
        if (_target == null) return;

        _bobTimer += Time.deltaTime * bobSpeed;

        float targetSpeed = 0f;
        if (_hasLastPos)
            targetSpeed = (_target.position - _lastTargetPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastTargetPos = _target.position;
        _hasLastPos = true;

        bool isIdle = targetSpeed <= idleSpeedThreshold;

        Vector3 desired;
        if (isIdle)
        {
            _orbitAngle += orbitSpeed * Time.deltaTime;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            desired = _target.position + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius;
            desired.y = _target.position.y + orbitHeight;
        }
        else
        {
            desired = _target.position + _target.TransformDirection(offset);
        }

        desired.y += Mathf.Sin(_bobTimer) * bobHeight;

        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * followSmoothing);
    }
}
