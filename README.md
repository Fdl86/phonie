# PHONIE DEV0.4.0.8 - AIRPORT & RADIO CONSOLIDATION

Cette candidate consolide DEV0.4 avant le passage à DEV0.5.

Fonctions principales :

- détection automatique de tout aérodrome après spawn, téléportation ou changement de terrain ;
- rechargement complet des Facilities, pistes, fréquences, météo, réseau sol et diagnostic ;
- contexte radio indépendant du contexte géographique en vol ;
- A/A, CTAF, UNICOM, ATIS et météo automatique toujours silencieux ;
- fréquence dialoguée recommandée : Tour en priorité, puis Sol/Clairance, Approche/Départ et AFIS/FSS ;
- aucun changement automatique de COM1 ;
- roulage générique vers le point d'attente ;
- tout vrai HOLD_SHORT Facilities relié à la piste est valable pour annoncer prêt ;
- contrôle du trafic avant alignement et autorisation de décollage ;
- collationnement simplifié obligatoire par PTT ;
- carte Diagnostic et redémarrage séparé de l'ASR et de la voix.

Commencer par `TEST-DEV0.4.0.8.md`. La source ne doit être qualifiée de release qu'après un GitHub Actions entièrement vert.
