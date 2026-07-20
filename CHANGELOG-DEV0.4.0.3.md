# Changelog PHONIE DEV0.4.0.3

## GROUND OCCUPANCY HOTFIX

- correction du refus erroné `Aucun itinéraire accessible vers un point d'attente libre` observé à LFBI ;
- confirmation par les logs que le graphe reste valide : 148 nœuds, 154 segments et 3 points d'attente ;
- test automatique garantissant qu'un itinéraire existe depuis chacun des 23 parkings LFBI vers la piste 03 lorsque le réseau est libre ;
- exclusion explicite de `SIMCONNECT_OBJECT_ID_USER` de la liste du trafic sol ;
- second filtre de protection par indicatif, proximité et vitesse lorsque l'identifiant objet n'est pas suffisant ;
- réduction du rayon d'occupation des nœuds de 45 m à 30 m ;
- réduction du rayon d'occupation des segments de 35 m à 20 m ;
- conservation de l'exclusion des vrais trafics présents sur un point d'attente ou un segment ;
- enrichissement du journal `ground-decisions-*.jsonl` avec position, nœud, segment et occupation détaillée ;
- les segments de piste ne sont pas autorisés comme raccourci vers le point d'attente ;
- la remontée de piste ou le départ depuis une intersection reste une décision opérationnelle postérieure au point d'attente ;
- version visible `DEV0.4.0.3 - GROUND OPERATIONS RC`.

## Correctif CI - dépendance PHONIE.sln supprimée

- le workflow restaure et compile explicitement les quatre projets ;
- la présence de `PHONIE.sln` n'est plus requise par GitHub Actions ;
- un précontrôle liste précisément les fichiers source manquants avant toute restauration ;
- aucune modification fonctionnelle de l'application.
