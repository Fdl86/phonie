# Normalisation Facilities - DEV0.4.0.4

## Résultat du diagnostic DEV0.4.0.1

Les cinq captures LFBI ont confirmé que le paquet TaxiPath reste correctement aligné dans les deux simulateurs.

Sous MSFS 2020, certains chemins `TAXI`, `PARKING` et `PATH` contiennent des valeurs indéfinies dans `RUNWAY_NUMBER` et `RUNWAY_DESIGNATOR`. Les champs suivants restent correctement positionnés. Il ne s'agit donc pas d'un décalage de structure.

## Règle appliquée

- chemin `RUNWAY` : numéro et désignateur validés et conservés ;
- chemin non-piste : champs piste bruts conservés dans le diagnostic, mais normalisés à `null` dans le moteur ;
- chemin `CLOSED`, `VEHICLE`, `ROAD` ou `PAINTEDLINE` : exclu du routage avion ;
- chemin `RUNWAY` : exclu du roulage normal vers le point d'attente ;
- chemin `TAXI`, `PARKING` ou `PATH` : exploitable si ses extrémités existent.

## Association des points d'attente

Un point d'attente est d'abord associé au segment de piste physique le plus proche. L'identité de cette piste physique est ensuite utilisée pour les deux extrémités opérationnelles. Ainsi, un point proche d'un segment marqué `21` reste utilisable pour une clairance vers la `03` de la même piste physique.

## Nommage

Le moteur utilise uniquement les noms présents dans `TAXI_NAME` et les chemins connectés. Une désignation générique de type `HS-17` n'est jamais prononcée. Si aucun nom radio fiable n'est disponible, la clairance est refusée avec une cause visible.

## Données LFBI validées par fixture

Les fixtures réelles contiennent :

- 3 pistes physiques ;
- 125 points taxi ;
- 23 parkings ;
- 154 chemins ;
- 5 entrées de noms taxi ;
- 3 points d'attente exploitables dans la capture testée.
