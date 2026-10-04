using UnityEngine;

// =============================================================
// FRACTIONALACCUMULATOR.CS — Convertit un flux continu de montants fractionnaires
// (régénération/DoT calculés en taux × deltaTime) en incréments ENTIERS.
// Path : Assets/Scripts/Utils/FractionalAccumulator.cs
//
// Sans ça, un DoT de 10 dégâts/sec à 60 FPS vaut 0.16 dégât/frame — arrondir ce montant
// directement à chaque frame donnerait 0 à CHAQUE frame, pour toujours (DoT silencieusement
// cassé, pas juste imprécis). Ce struct garde le reliquat fractionnaire d'une frame à l'autre
// au lieu de le perdre, et ne rend un entier que lorsqu'au moins un point complet s'est
// accumulé. Voir docs/superpowers/specs/2026-10-04-integer-hp-mana-design.md.
// =============================================================
public struct FractionalAccumulator
{
    private float _remainder;

    /// <summary>Ajoute `delta` (peut être fractionnaire) au reliquat et retourne la partie
    /// ENTIÈRE accumulée depuis la dernière extraction (0 si rien n'a encore atteint un point
    /// entier). Le reliquat restant est conservé pour le prochain appel, jamais perdu.
    /// FloorToInt (pas Round) : un reliquat de 0.5 n'a pas encore atteint un point entier, il
    /// doit continuer à s'accumuler plutôt que d'être arrondi prématurément vers le haut.</summary>
    public int ExtractWhole(float delta)
    {
        _remainder += delta;
        int whole = Mathf.FloorToInt(_remainder);
        if (whole > 0) _remainder -= whole;
        return whole;
    }
}
