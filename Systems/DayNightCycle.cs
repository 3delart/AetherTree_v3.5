using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DAYNIGHTCYCLE.CS — Cycle jour/nuit
// Path : Assets/Scripts/World/DayNightCycle.cs
// AetherTree GDD v30 — §20
//
// Setup :
//   Poser sur _Managers.
//   Assigner sunLight (Directional Light).
//   dayDuration = durée d'un jour complet en secondes (ex: 600 = 10 min).
//   startHour   = heure de départ (0-24).
//
// Events :
//   OnDayStart  — déclenché au lever du soleil (sunrise = 6h)
//   OnNightStart — déclenché au coucher du soleil (sunset = 20h)
//
// Hooks UnlockManager :
//   IsNight() dans UnlockManager.cs retourne DayNightCycle.Instance.IsNight
// =============================================================

public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────
    [Header("Lumière directionnelle (Soleil/Lune)")]
    public Light sunLight;

    [Header("Durée")]
    [Tooltip("Durée d'un cycle complet en secondes. Ex: 600 = 10 min réel = 1 jour.")]
    public float dayDuration = 600f;

    [Tooltip("Heure de départ au lancement (0-24).")]
    [Range(0f, 24f)]
    public float startHour = 8f;

    [Header("Seuils jour/nuit")]
    [Tooltip("Heure du lever du soleil.")]
    [Range(0f, 12f)]
    public float sunriseHour = 6f;

    [Tooltip("Heure du coucher du soleil.")]
    [Range(12f, 24f)]
    public float sunsetHour = 20f;

    [Header("Couleurs lumière")]
    [Tooltip("Couleur de la lumière en plein jour.")]
    public Color dayColor   = new Color(1.0f, 0.95f, 0.85f);

    [Tooltip("Couleur de la lumière au lever/coucher.")]
    public Color sunsetColor = new Color(1.0f, 0.55f, 0.25f);

    [Tooltip("Couleur de la lumière la nuit.")]
    public Color nightColor  = new Color(0.15f, 0.20f, 0.40f);

    [Header("Intensité lumière")]
    public float dayIntensity    = 1.2f;
    public float sunsetIntensity = 0.8f;
    public float nightIntensity  = 0.05f;

    [Header("Ambient light")]
    public Color dayAmbient    = new Color(0.35f, 0.35f, 0.35f);
    public Color nightAmbient  = new Color(0.05f, 0.05f, 0.15f);

    // ── Events ────────────────────────────────────────────────
    public static event System.Action OnDayStart;
    public static event System.Action OnNightStart;

    // ── Runtime ───────────────────────────────────────────────
    private float _currentHour;
    private bool  _isNight;

    public float CurrentHour => _currentHour;
    public bool  IsNight     => _isNight;

    /// <summary>Heure affichée sous forme "HH:MM".</summary>
    public string TimeLabel
    {
        get
        {
            int h = Mathf.FloorToInt(_currentHour);
            int m = Mathf.FloorToInt((_currentHour - h) * 60f);
            return $"{h:D2}:{m:D2}";
        }
    }

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _currentHour = startHour;
        _isNight     = IsHourNight(startHour);
    }

    private void Update()
    {
        if (dayDuration <= 0f) return;

        float previousHour = _currentHour;
        _currentHour = (_currentHour + (24f / dayDuration) * Time.deltaTime) % 24f;

        // Détection transitions
        bool wasNight = IsHourNight(previousHour);
        bool nowNight = IsHourNight(_currentHour);

        if (wasNight != nowNight)
        {
            _isNight = nowNight;
            if (nowNight) OnNightStart?.Invoke();
            else          OnDayStart?.Invoke();
        }

        UpdateLight();
    }

    // =========================================================
    // LUMIÈRE
    // =========================================================

    private void UpdateLight()
    {
        if (sunLight == null) return;

        // Rotation — 0h = minuit (soleil sous l'horizon), 6h = lever, 12h = zénith, 20h = coucher
        float angle = (_currentHour / 24f) * 360f - 90f;
        sunLight.transform.rotation = Quaternion.Euler(angle, 170f, 0f);

        // Couleur & intensité selon l'heure
        Color  targetColor;
        float  targetIntensity;
        Color  targetAmbient;

        if (_currentHour >= sunriseHour && _currentHour < sunriseHour + 2f)
        {
            // Lever du soleil
            float t = (_currentHour - sunriseHour) / 2f;
            targetColor     = Color.Lerp(nightColor,   sunsetColor, t);
            targetIntensity = Mathf.Lerp(nightIntensity, sunsetIntensity, t);
            targetAmbient   = Color.Lerp(nightAmbient,  dayAmbient, t);
        }
        else if (_currentHour >= sunriseHour + 2f && _currentHour < sunsetHour - 2f)
        {
            // Plein jour
            targetColor     = dayColor;
            targetIntensity = dayIntensity;
            targetAmbient   = dayAmbient;
        }
        else if (_currentHour >= sunsetHour - 2f && _currentHour < sunsetHour)
        {
            // Coucher du soleil
            float t = (_currentHour - (sunsetHour - 2f)) / 2f;
            targetColor     = Color.Lerp(dayColor,    sunsetColor, t);
            targetIntensity = Mathf.Lerp(dayIntensity, sunsetIntensity, t);
            targetAmbient   = Color.Lerp(dayAmbient,   nightAmbient, t);
        }
        else
        {
            // Nuit
            targetColor     = nightColor;
            targetIntensity = nightIntensity;
            targetAmbient   = nightAmbient;
        }

        sunLight.color     = targetColor;
        sunLight.intensity = targetIntensity;
        RenderSettings.ambientLight = targetAmbient;
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private bool IsHourNight(float hour)
        => hour < sunriseHour || hour >= sunsetHour;
}
