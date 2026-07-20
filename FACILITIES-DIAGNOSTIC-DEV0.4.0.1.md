# Diagnostic Facilities - PHONIE DEV0.4.0.1

## Objectif

Déterminer précisément pourquoi certains `TaxiPaths` LFBI deviennent incohérents à partir d'un index donné, sans inventer de structure ni ignorer les avertissements.

## Format capturé

L'en-tête Facilities est lu sur 40 octets :

| Offset | Taille | Champ |
|---:|---:|---|
| 0 | 4 | taille déclarée |
| 4 | 4 | version |
| 8 | 4 | identifiant du message |
| 12 | 4 | identifiant de demande utilisateur |
| 16 | 4 | identifiant unique |
| 20 | 4 | identifiant unique du parent |
| 24 | 4 | type Facilities |
| 28 | 4 | élément de liste |
| 32 | 4 | index de l'élément |
| 36 | 4 | taille de la liste |

La charge utile TaxiPath attendue fait 64 octets, soit 16 champs de 4 octets :

`TYPE`, `WIDTH`, `LEFT_HALF_WIDTH`, `RIGHT_HALF_WIDTH`, `WEIGHT`, `RUNWAY_NUMBER`, `RUNWAY_DESIGNATOR`, `LEFT_EDGE`, `LEFT_EDGE_LIGHTED`, `RIGHT_EDGE`, `RIGHT_EDGE_LIGHTED`, `CENTER_LINE`, `CENTER_LINE_LIGHTED`, `START`, `END`, `NAME_INDEX`.

## Interprétation

Le build ne considère la disposition binaire comme cohérente que si :

- au moins un TaxiPath est reçu ;
- chaque TaxiPath peut être décodé ;
- aucun champ essentiel ne contient une valeur manifestement hors plage ;
- la taille déclarée correspond à la taille reçue ;
- la charge utile TaxiPath fait 64 octets ;
- aucun index n'est dupliqué, manquant ou hors plage ;
- `ListSize` reste cohérent.

Cette validation ne prouve pas encore que la topologie de l'aérodrome est correcte. Elle prouve uniquement que les paquets ont été reçus et décodés sans rupture de structure apparente.
