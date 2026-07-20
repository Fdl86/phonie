# Changelog PHONIE DEV0.4.0.4

## Taxi occupancy and routing diagnostics

- remplace l'occupation radiale large par une occupation géométrique ciblée ;
- un avion immobile associé à un parking ne bloque plus les nœuds et taxiways communs situés à proximité ;
- un avion stationné ne bloque que son parking et sa bretelle directe ;
- un trafic roulant ne bloque que le segment sur lequel il se trouve réellement ;
- les extrémités de segment ne sont bloquées que lorsque l'objet est à moins de 8 mètres ;
- les points d'attente ne sont bloqués que lorsque le trafic est réellement à proximité immédiate ;
- ajout d'un diagnostic détaillé dans l'overlay Diagnostic PHONIE ;
- affichage de chaque objet SimConnect reçu, de son indicatif, sa vitesse, sa classification, son parking ou segment le plus proche et des éléments réellement bloqués ;
- affichage de chaque point d'attente candidat, de son accessibilité et des nœuds ou segments qui empêchent éventuellement le chemin ;
- enrichissement du journal `logs\ground-operations\ground-decisions-*.jsonl` avec les diagnostics trafic et routage ;
- nouveaux tests automatiques basés sur la fixture réelle LFBI : trois avions immobiles sur les parkings voisins ne doivent pas empêcher le roulage depuis le parking 6 ;
- version visible `DEV0.4.0.4 - GROUND OPERATIONS RC`.

## Cause corrigée

DEV0.4.0.3 considérait tout nœud situé à moins de 30 mètres et tout segment situé à moins de 20 mètres d'un objet avion comme occupé. Sur l'aire de stationnement LFBI, des objets SimConnect immobiles proches des parkings 9, 10 et 11 condamnaient ainsi des nœuds communs de la ligne jaune, alors que la voie était visuellement libre.
