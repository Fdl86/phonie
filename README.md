# PHONIE DEV0.4.0.9 - AIRPORT DETECTION & OFFICIAL RADIO

Candidate corrective de DEV0.4, construite à partir de DEV0.4.0.8.

Corrections prioritaires :

- décodage robuste des listes d'aérodromes MSFS 2024 reçues avec un emplacement `rgData` de compatibilité supplémentaire ;
- tests de régression reproduisant exactement les tailles observées dans les logs : 161 éléments / 5832 octets et 204 éléments / 7380 octets ;
- détection géographique dynamique de LFOU, LFBI et de tout aérodrome ICAO présent dans le cache Facilities ;
- rechargement complet du contexte lors d'un changement de terrain : pistes, fréquences, météo, réseau sol et diagnostic ;
- amorce d'un catalogue radio prioritaire issu du SIA eAIP, indépendant des fréquences erronées ou dupliquées des scènes MSFS ;
- LFOU : 120.405, AFIS aux horaires publiés et A/A silencieuse hors horaires ;
- LFBI : Tour 118.505 prioritaire, Approche/SIV 134.100, ATIS 121.780 silencieux, répondeur 124.000 silencieux ;
- Tour prioritaire sur Approche et A/A ; aucune modification automatique de COM1 ;
- tout véritable `HOLD_SHORT` Facilities relié à la piste reste valable pour annoncer prêt au départ.

Commencer par `TEST-DEV0.4.0.9.md`.

Cette source est une candidate CI. Elle ne doit être qualifiée de release qu'après compilation, tests et publication GitHub Actions entièrement verts.
