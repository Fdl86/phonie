# Voix contrôleur DEV0.4.1.6

La clé station est normalisée une seule fois et sert à la fois à l'attribution de voix et au chemin de cache. Deux variantes de casse comme `lfbi|tour` et `LFBI|TOUR` réutilisent donc le même répertoire.

Les segments de chemin neutralisent les caractères interdits Windows, les points et espaces finaux, ainsi que les noms réservés `CON`, `PRN`, `AUX`, `NUL`, `COM1` à `COM9` et `LPT1` à `LPT9`.
