# MOTEUR GROUND OPERATIONS - DEV0.4.0.6

## Principe

Le graphe Facilities reste la source de vérité pour le trajet physique. Les appellations locales sont des informations de diagnostic et non plus des conditions nécessaires à la phraséologie.

## Roulage

Tour : `roulez au point d'attente et rappelez prêt`.

AFIS : piste, paramètres et demande de rappel au point d'attente, sans instruction impérative de roulage.

## Départ

La demande `prêt au point d'attente` est acceptée seulement si le localisateur confirme un nœud HoldShort de départ associé à la piste attribuée. Un point marqué `IntermediateHoldingPoint` par le profil est refusé.

Piste libre et trafic connu : état `TakeoffCleared` et clairance combinée alignement-décollage.

Piste occupée : `RUNWAY_OCCUPIED_HOLD`.

Trafic inconnu : `TRAFFIC_STATUS_UNKNOWN_HOLD`.

## Appellations

Le texte transcrit peut contenir Alpha, Alpha 2 ou une autre appellation erronée. Cette valeur est journalisée mais ne peut ni autoriser ni refuser le décollage. Seule la position géométrique confirme le point d'attente.
