# Protocole DEV0.4.1.6

## Contrôles CI obligatoires avant le réseau

1. Vérification `.gitattributes` sur tous les fichiers radio hachés.
2. Recalcul des SHA-256 des descripteurs `previous`, `current` et `next` de chaque manifest présent.
3. Refus explicite de tout CRLF dans un jeu de données haché.
4. Compilation WPF PHONIE avec zéro avertissement.
5. Compilation et exécution des Smoke Tests et Core Tests.
6. Prépublication autonome Windows x64 avec présence de `PHONIE.exe`.

## Régressions couvertes

- fixture SIA chargée sur runner Windows avec `core.autocrlf=true` ;
- Tour locale prioritaire au sol ;
- Approche régionale recommandée quand aucune Tour locale n'existe ;
- FIS régional recommandé en vol face à une A/A locale ;
- portée locale utilisée uniquement comme départage à priorité égale ;
- manifest sérialisé en camelCase ;
- noms réservés Windows et suffixes interdits neutralisés dans le cache voix.

## Données SIA

Le manifest de dépôt reste temporairement en mode `bootstrapRequired: true`. Tant que le workflow `update-sia-radio.yml` n'a pas publié la première base nationale versionnée, un build complet reconstruit encore la base SIA. Ce point est connu et ne doit pas être confondu avec une erreur de compilation ou d'intégrité.
