using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// ELEMENTDATA.CS — Data-driven, zéro switch à maintenir
// Path : Assets/Scripts/Data/Elements/ElementData.cs
// AetherTree GDD v3.5 — §6.1 / §6.4
//
// Pour ajouter un élément :
//   1. Ajouter la valeur dans ElementType
//   2. Lui mettre l'attribut [ElementInfo(label, r, g, b, counter)]
//   C'est tout. Tout le reste s'adapte automatiquement.
//
// DÉMO : Neutral, Fire, Water, Earth, Nature (5 éléments)
// FINAL : + Lightning, Darkness, Light (8 éléments total)
//         Glace supprimée v3.0 — Wind supprimé v3.0
//
// Cycle élémentaire (GDD §6.1) :
//   🔥 Feu → 🌊 Eau → ⚡ Foudre → 🌍 Terre → 🌿 Nature → 🔥 Feu
//   ☀ Lumière ↔ 🌑 Ténèbres (duo miroir)
//   ⚪ Neutre : ne contre rien, rien ne le contre
//
// Poison = DebuffType uniquement — JAMAIS ElementType (§3.1.1.1)
// =============================================================

[AttributeUsage(AttributeTargets.Field)]
public class ElementInfoAttribute : Attribute
{
    public string      Label   { get; }
    public float       R       { get; }
    public float       G       { get; }
    public float       B       { get; }
    public ElementType Counter { get; }  // Contre-élément selon GDD §6.1
    public bool        IsDemo  { get; }  // Disponible en démo

    public ElementInfoAttribute(string label, float r, float g, float b,
                                ElementType counter, bool isDemo = false)
    {
        Label   = label;
        R       = r;
        G       = g;
        B       = b;
        Counter = counter;
        IsDemo  = isDemo;
    }
}

// =============================================================
// ENUM ELEMENTTYPE
// Ajouter un élément = ajouter une ligne + son attribut [ElementInfo]
// Cycle GDD §6.1 : Feu→Eau→Foudre→Terre→Nature→Feu | Lumière↔Ténèbres
// =============================================================
// Ordinaux figés explicitement (2026-09-07) — Unity sérialise un enum par sa position int,
// jamais son nom. Avant ce figeage, insérer/retirer un membre au milieu décalait tous les
// suivants et corrompait silencieusement les .asset déjà sauvegardés. Désormais : ajouter un
// membre = choisir le prochain entier libre, n'importe où dans le fichier ; ne JAMAIS réutiliser
// un entier déjà attribué (même à un membre retiré/[Obsolete]).
public enum ElementType
{
    [ElementInfo("— Any —",   0f,    0f,    0f,    ElementType.Any)]
    Any = -1,

    [ElementInfo("Neutre",    0.75f, 0.75f, 0.75f, ElementType.Neutral, isDemo: true)]
    Neutral = 0,

    [ElementInfo("Feu",       1.0f,  0.35f, 0.0f,  ElementType.Water,   isDemo: true)]
    Fire = 1,

    [ElementInfo("Eau",       0.1f,  0.5f,  1.0f,  ElementType.Lightning, isDemo: true)]
    Water = 2,

    [ElementInfo("Foudre",    0.8f,  0.6f,  1.0f,  ElementType.Earth)]
    Lightning = 3,

    [ElementInfo("Terre",     0.6f,  0.4f,  0.1f,  ElementType.Nature,  isDemo: true)]
    Earth = 4,

    [ElementInfo("Nature",    0.15f, 0.75f, 0.2f,  ElementType.Fire,    isDemo: true)]
    Nature = 5,

    [ElementInfo("Ténèbres",  0.3f,  0.1f,  0.4f,  ElementType.Light)]
    Darkness = 6,

    [ElementInfo("Lumière",   1.0f,  0.95f, 0.5f,  ElementType.Darkness)]
    Light = 7,
}

// =============================================================
// EXTENSIONS — lit les attributs, cache les résultats
// =============================================================
public static class ElementDataExtensions
{
    private static readonly Dictionary<ElementType, ElementInfoAttribute> _cache
        = new Dictionary<ElementType, ElementInfoAttribute>();

    private static ElementInfoAttribute GetInfo(ElementType type)
    {
        if (_cache.TryGetValue(type, out var cached)) return cached;
        var field = typeof(ElementType).GetField(type.ToString());
        var attr  = field?.GetCustomAttributes(typeof(ElementInfoAttribute), false);
        var info  = (attr != null && attr.Length > 0) ? (ElementInfoAttribute)attr[0] : null;
        _cache[type] = info;
        return info;
    }

    public static string      GetLabel(this ElementType t)   => GetInfo(t)?.Label   ?? t.ToString();
    public static ElementType GetCounter(this ElementType t) => GetInfo(t)?.Counter ?? ElementType.Neutral;
    public static bool        IsNeutral(this ElementType t)  => t == ElementType.Neutral;
    public static bool        IsDemo(this ElementType t)     => GetInfo(t)?.IsDemo  ?? false;

    public static Color GetColor(this ElementType t)
    {
        var info = GetInfo(t);
        return info != null ? new Color(info.R, info.G, info.B) : Color.white;
    }

    /// <summary>
    /// Épithète du titre selon l'élément et la famille d'arme. GDD v3.5 §6.4.
    /// Retourne la chaîne vide si la combinaison est inconnue.
    /// </summary>
    public static string GetEpithet(this ElementType element, WeaponType weapon)
    {
        // Remonte à la famille de départ (ex: LongSword → ShortSword)
        WeaponType family = weapon.GetStartingFamily();

        if (_epithetTable.TryGetValue((family, element), out string ep))
            return ep;

        return string.Empty;
    }

    // ── Table des épithètes — GDD v3.5 §6.4 — 20 armes × 8 éléments ──────────
    // (WeaponType famille, ElementType) → épithète
    private static readonly Dictionary<(WeaponType, ElementType), string> _epithetTable
        = new Dictionary<(WeaponType, ElementType), string>
    {
        // ── Short Sword (Lame) ────────────────────────────────
        { (WeaponType.ShortSword, ElementType.Neutral),   "Pur"           },
        { (WeaponType.ShortSword, ElementType.Fire),      "Embrasé"       },
        { (WeaponType.ShortSword, ElementType.Water),     "Déferlant"     },
        { (WeaponType.ShortSword, ElementType.Earth),     "Tellurique"    },
        { (WeaponType.ShortSword, ElementType.Nature),    "Sauvage"       },
        { (WeaponType.ShortSword, ElementType.Lightning), "Fulgurant"     },
        { (WeaponType.ShortSword, ElementType.Darkness),  "Maudit"        },
        { (WeaponType.ShortSword, ElementType.Light),     "Sacré"         },

        // ── Long Sword (Chevalier) ────────────────────────────
        { (WeaponType.LongSword,  ElementType.Neutral),   "Impassible"    },
        { (WeaponType.LongSword,  ElementType.Fire),      "Incandescent"  },
        { (WeaponType.LongSword,  ElementType.Water),     "Ondoyant"      },
        { (WeaponType.LongSword,  ElementType.Earth),     "Immuable"      },
        { (WeaponType.LongSword,  ElementType.Nature),    "Primordial"    },
        { (WeaponType.LongSword,  ElementType.Lightning), "Foudroyant"    },
        { (WeaponType.LongSword,  ElementType.Darkness),  "Abyssal"       },
        { (WeaponType.LongSword,  ElementType.Light),     "Radieux"       },

        // ── Double Sword (Duelliste) ──────────────────────────
        { (WeaponType.DoubleSword, ElementType.Neutral),  "Martial"       },
        { (WeaponType.DoubleSword, ElementType.Fire),     "Ardent"        },
        { (WeaponType.DoubleSword, ElementType.Water),    "Déferlant"     },
        { (WeaponType.DoubleSword, ElementType.Earth),    "Granitique"    },
        { (WeaponType.DoubleSword, ElementType.Nature),   "Féroce"        },
        { (WeaponType.DoubleSword, ElementType.Lightning),"Électrisé"     },
        { (WeaponType.DoubleSword, ElementType.Darkness), "Crépusculaire" },
        { (WeaponType.DoubleSword, ElementType.Light),    "Étincelant"    },

        // ── Great Axe (Berserker) ─────────────────────────────
        { (WeaponType.GreatAxe,   ElementType.Neutral),   "Endurci"       },
        { (WeaponType.GreatAxe,   ElementType.Fire),      "Volcanique"    },
        { (WeaponType.GreatAxe,   ElementType.Water),     "Torrentiel"    },
        { (WeaponType.GreatAxe,   ElementType.Earth),     "Rocheux"       },
        { (WeaponType.GreatAxe,   ElementType.Nature),    "Bestial"       },
        { (WeaponType.GreatAxe,   ElementType.Lightning), "Tonnant"       },
        { (WeaponType.GreatAxe,   ElementType.Darkness),  "Ténébreux"     },
        { (WeaponType.GreatAxe,   ElementType.Light),     "Céleste"       },

        // ── Scythe (Faucheur) ─────────────────────────────────
        { (WeaponType.Scythe,     ElementType.Neutral),   "Austère"       },
        { (WeaponType.Scythe,     ElementType.Fire),      "Torride"       },
        { (WeaponType.Scythe,     ElementType.Water),     "Diluvien"      },
        { (WeaponType.Scythe,     ElementType.Earth),     "Fossile"       },
        { (WeaponType.Scythe,     ElementType.Nature),    "Sylvestre"     },
        { (WeaponType.Scythe,     ElementType.Lightning), "Orageux"       },
        { (WeaponType.Scythe,     ElementType.Darkness),  "Ombral"        },
        { (WeaponType.Scythe,     ElementType.Light),     "Auroral"       },

        // ── Mace (Briseur) ────────────────────────────────────
        { (WeaponType.Mace,       ElementType.Neutral),   "Forgé"         },
        { (WeaponType.Mace,       ElementType.Fire),      "Flamboyant"    },
        { (WeaponType.Mace,       ElementType.Water),     "Torrentiel"    },
        { (WeaponType.Mace,       ElementType.Earth),     "Tellurique"    },
        { (WeaponType.Mace,       ElementType.Nature),    "Verdoyant"     },
        { (WeaponType.Mace,       ElementType.Lightning), "Galvanique"    },
        { (WeaponType.Mace,       ElementType.Darkness),  "Sinistre"      },
        { (WeaponType.Mace,       ElementType.Light),     "Lumineux"      },

        // ── Hammer (Écraseur) ─────────────────────────────────
        { (WeaponType.Hammer,     ElementType.Neutral),   "Trempé"        },
        { (WeaponType.Hammer,     ElementType.Fire),      "Brasier"       },
        { (WeaponType.Hammer,     ElementType.Water),     "Submergé"      },
        { (WeaponType.Hammer,     ElementType.Earth),     "Lithique"      },
        { (WeaponType.Hammer,     ElementType.Nature),    "Racinaire"     },
        { (WeaponType.Hammer,     ElementType.Lightning), "Magnétique"    },
        { (WeaponType.Hammer,     ElementType.Darkness),  "Lugubre"       },
        { (WeaponType.Hammer,     ElementType.Light),     "Divin"         },

        // ── Dagger (Assassin) ─────────────────────────────────
        { (WeaponType.Dagger,     ElementType.Neutral),   "Résolu"        },
        { (WeaponType.Dagger,     ElementType.Fire),      "Ardent"        },
        { (WeaponType.Dagger,     ElementType.Water),     "Déferlant"     },
        { (WeaponType.Dagger,     ElementType.Earth),     "Tellurique"    },
        { (WeaponType.Dagger,     ElementType.Nature),    "Sylvestre"     },
        { (WeaponType.Dagger,     ElementType.Lightning), "Fulgurant"     },
        { (WeaponType.Dagger,     ElementType.Darkness),  "Maudit"        },
        { (WeaponType.Dagger,     ElementType.Light),     "Sacré"         },

        // ── Double Dagger (Traqueur) ──────────────────────────
        { (WeaponType.DoubleDagger, ElementType.Neutral),  "Acéré"        },
        { (WeaponType.DoubleDagger, ElementType.Fire),     "Scorché"      },
        { (WeaponType.DoubleDagger, ElementType.Water),    "Lacustre"     },
        { (WeaponType.DoubleDagger, ElementType.Earth),    "Sédimentaire" },
        { (WeaponType.DoubleDagger, ElementType.Nature),   "Âpre"         },
        { (WeaponType.DoubleDagger, ElementType.Lightning),"Plasma"       },
        { (WeaponType.DoubleDagger, ElementType.Darkness), "Voilé"        },
        { (WeaponType.DoubleDagger, ElementType.Light),    "Béni"         },

        // ── Shield (Sentinelle) ───────────────────────────────
        { (WeaponType.Shield,     ElementType.Neutral),   "Inaltéré"      },
        { (WeaponType.Shield,     ElementType.Fire),      "Incandescent"  },
        { (WeaponType.Shield,     ElementType.Water),     "Engloutissant" },
        { (WeaponType.Shield,     ElementType.Earth),     "Granitique"    },
        { (WeaponType.Shield,     ElementType.Nature),    "Ancestral"     },
        { (WeaponType.Shield,     ElementType.Lightning), "Foudroyant"    },
        { (WeaponType.Shield,     ElementType.Darkness),  "Spectral"      },
        { (WeaponType.Shield,     ElementType.Light),     "Éthéré"        },

        // ── Bow (Archer) ──────────────────────────────────────
        { (WeaponType.Bow,        ElementType.Neutral),   "Discipliné"    },
        { (WeaponType.Bow,        ElementType.Fire),      "Flamboyant"    },
        { (WeaponType.Bow,        ElementType.Water),     "Fluvial"       },
        { (WeaponType.Bow,        ElementType.Earth),     "Minéral"       },
        { (WeaponType.Bow,        ElementType.Nature),    "Frondaison"    },
        { (WeaponType.Bow,        ElementType.Lightning), "Volatil"       },
        { (WeaponType.Bow,        ElementType.Darkness),  "Funèbre"       },
        { (WeaponType.Bow,        ElementType.Light),     "Solaire"       },

        // ── Crossbow (Arbalétrier) ────────────────────────────
        { (WeaponType.Crossbow,   ElementType.Neutral),   "Inflexible"    },
        { (WeaponType.Crossbow,   ElementType.Fire),      "Pyrique"       },
        { (WeaponType.Crossbow,   ElementType.Water),     "Marin"         },
        { (WeaponType.Crossbow,   ElementType.Earth),     "Calcaire"      },
        { (WeaponType.Crossbow,   ElementType.Nature),    "Enraciné"      },
        { (WeaponType.Crossbow,   ElementType.Lightning), "Statique"      },
        { (WeaponType.Crossbow,   ElementType.Darkness),  "Obscur"        },
        { (WeaponType.Crossbow,   ElementType.Light),     "Illuminé"      },

        // ── Pistol (Tireur) ───────────────────────────────────
        { (WeaponType.Pistol,     ElementType.Neutral),   "Épuré"         },
        { (WeaponType.Pistol,     ElementType.Fire),      "Ardent"        },
        { (WeaponType.Pistol,     ElementType.Water),     "Ondoyant"      },
        { (WeaponType.Pistol,     ElementType.Earth),     "Tellurique"    },
        { (WeaponType.Pistol,     ElementType.Nature),    "Tribal"        },
        { (WeaponType.Pistol,     ElementType.Lightning), "Arc"           },
        { (WeaponType.Pistol,     ElementType.Darkness),  "Nocturne"      },
        { (WeaponType.Pistol,     ElementType.Light),     "Zénith"        },

        // ── Shotgun (Gunner) ──────────────────────────────────
        { (WeaponType.Shotgun,    ElementType.Neutral),   "Brut"          },
        { (WeaponType.Shotgun,    ElementType.Fire),      "Embrasé"       },
        { (WeaponType.Shotgun,    ElementType.Water),     "Déferlant"     },
        { (WeaponType.Shotgun,    ElementType.Earth),     "Tectonique"    },
        { (WeaponType.Shotgun,    ElementType.Nature),    "Farouche"      },
        { (WeaponType.Shotgun,    ElementType.Lightning), "Électrisé"     },
        { (WeaponType.Shotgun,    ElementType.Darkness),  "Abyssal"       },
        { (WeaponType.Shotgun,    ElementType.Light),     "Céleste"       },

        // ── Sniper (Précisionniste) ───────────────────────────
        { (WeaponType.Sniper,     ElementType.Neutral),   "Tempéré"       },
        { (WeaponType.Sniper,     ElementType.Fire),      "Incandescent"  },
        { (WeaponType.Sniper,     ElementType.Water),     "Glaciaire"     },
        { (WeaponType.Sniper,     ElementType.Earth),     "Cristallin"    },
        { (WeaponType.Sniper,     ElementType.Nature),    "Primordial"    },
        { (WeaponType.Sniper,     ElementType.Lightning), "Foudroyant"    },
        { (WeaponType.Sniper,     ElementType.Darkness),  "Ombral"        },
        { (WeaponType.Sniper,     ElementType.Light),     "Auroral"       },

        // ── Whip (non présent dans la table GDD — entrées vides par sécurité) ──
        // À compléter si ajouté à la table §6.4

        // ── Staff (Mage) ──────────────────────────────────────
        { (WeaponType.Staff,      ElementType.Neutral),   "Originel"      },
        { (WeaponType.Staff,      ElementType.Fire),      "Incandescent"  },
        { (WeaponType.Staff,      ElementType.Water),     "Torrentiel"    },
        { (WeaponType.Staff,      ElementType.Earth),     "Tellurique"    },
        { (WeaponType.Staff,      ElementType.Nature),    "Primordial"    },
        { (WeaponType.Staff,      ElementType.Lightning), "Foudroyant"    },
        { (WeaponType.Staff,      ElementType.Darkness),  "Abyssal"       },
        { (WeaponType.Staff,      ElementType.Light),     "Céleste"       },

        // ── Scepter (Arcaniste) ───────────────────────────────
        { (WeaponType.Scepter,    ElementType.Neutral),   "Stoïque"       },
        { (WeaponType.Scepter,    ElementType.Fire),      "Ignifuge"      },
        { (WeaponType.Scepter,    ElementType.Water),     "Aquatique"     },
        { (WeaponType.Scepter,    ElementType.Earth),     "Lithique"      },
        { (WeaponType.Scepter,    ElementType.Nature),    "Sylvestre"     },
        { (WeaponType.Scepter,    ElementType.Lightning), "Galvanique"    },
        { (WeaponType.Scepter,    ElementType.Darkness),  "Crépusculaire" },
        { (WeaponType.Scepter,    ElementType.Light),     "Divin"         },

        // ── Orb (Gardien de l'Âme) ───────────────────────────
        { (WeaponType.Orb,        ElementType.Neutral),   "Équilibré"     },
        { (WeaponType.Orb,        ElementType.Fire),      "Ardent"        },
        { (WeaponType.Orb,        ElementType.Water),     "Ondoyant"      },
        { (WeaponType.Orb,        ElementType.Earth),     "Immuable"      },
        { (WeaponType.Orb,        ElementType.Nature),    "Verdoyant"     },
        { (WeaponType.Orb,        ElementType.Lightning), "Magnétique"    },
        { (WeaponType.Orb,        ElementType.Darkness),  "Voilé"         },
        { (WeaponType.Orb,        ElementType.Light),     "Radieux"       },

        // ── Tome (Bibliomancien) ──────────────────────────────
        { (WeaponType.Tome,       ElementType.Neutral),   "Inébranlable"  },
        { (WeaponType.Tome,       ElementType.Fire),      "Torride"       },
        { (WeaponType.Tome,       ElementType.Water),     "Diluvien"      },
        { (WeaponType.Tome,       ElementType.Earth),     "Pétrifié"      },
        { (WeaponType.Tome,       ElementType.Nature),    "Racinaire"     },
        { (WeaponType.Tome,       ElementType.Lightning), "Orageux"       },
        { (WeaponType.Tome,       ElementType.Darkness),  "Lugubre"       },
        { (WeaponType.Tome,       ElementType.Light),     "Éthéré"        },

        // ── Wand (Enchanteur) ─────────────────────────────────
        { (WeaponType.Wand,       ElementType.Neutral),   "Raffiné"       },
        { (WeaponType.Wand,       ElementType.Fire),      "Brasier"       },
        { (WeaponType.Wand,       ElementType.Water),     "Lacustre"      },
        { (WeaponType.Wand,       ElementType.Earth),     "Fossile"       },
        { (WeaponType.Wand,       ElementType.Nature),    "Feuillu"       },
        { (WeaponType.Wand,       ElementType.Lightning), "Tonnant"       },
        { (WeaponType.Wand,       ElementType.Darkness),  "Spectral"      },
        { (WeaponType.Wand,       ElementType.Light),     "Lumineux"      },
    };

    /// <summary>Tous les éléments non-sentinelles, avec ou sans Neutre.</summary>
    public static List<ElementType> GetAllElements(bool includeNeutral = false)
    {
        var result = new List<ElementType>();
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            if (!includeNeutral && t == ElementType.Neutral) continue;
            result.Add(t);
        }
        return result;
    }

    /// <summary>Éléments disponibles en démo (includeNeutral optionnel).</summary>
    public static List<ElementType> GetDemoElements(bool includeNeutral = false)
    {
        var result = new List<ElementType>();
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            if (!includeNeutral && t == ElementType.Neutral) continue;
            if (t.IsDemo()) result.Add(t);
        }
        return result;
    }
}

// Pas de ElementData SO — tout est piloté par [ElementInfo] sur ElementType directement
// (voir ElementDataExtensions ci-dessus). Aucun asset .asset de ce type n'a jamais existé
// dans le projet, ni ElementAffinityReq (retirés tous les deux, code mort — 2026).
