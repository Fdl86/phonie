# Changelog DEV0.4.1.6

## Intégrité SHA-256 et fins de ligne

- Ajout de `.gitattributes` avec `-text` sur les jeux de données radio et fixtures dont les octets sont contrôlés par SHA-256.
- Le workflow Windows vérifie avant compilation les attributs Git, l'absence de CRLF et chaque empreinte déclarée par les manifests.
- Le workflow de mise à jour SIA renormalise explicitement `data/radio/france` avant son commit automatisé.

## Sélection radio

- `SiaRadioCatalog.Recommend` n'exclut plus les services régionaux.
- La priorité opérationnelle est évaluée avant la portée ; la portée locale ne sert plus que de départage à priorité égale.
- L'Approche régionale reste donc disponible sans Tour locale, et le FIS régional peut devenir prioritaire en vol.

## Manifest AIRAC

- Sérialisation du manifest centralisée avec `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.
- `OfficialRadioCatalogService` et `RadioDataUpdateService` produisent désormais le même format compatible avec les outils Python et le manifest distant.
- Une bascule AIRAC validée reste active en mémoire si le manifest ne peut pas être réécrit dans un répertoire protégé.

## Cache des voix

- La clé de répertoire utilise la même clé station normalisée que l'attribution de voix.
- Les noms réservés Windows (`CON`, `COM1`, `LPT1`, etc.), caractères interdits et points/espaces finaux sont neutralisés.
- Les segments trop longs sont raccourcis de façon déterministe avec suffixe SHA-256.
