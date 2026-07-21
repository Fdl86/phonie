# Changelog DEV0.4.1.1

## Correctif CI SIA

- Correction de la découverte Atlas VAC après l'évolution du catalogue SIA de juillet 2026.
- Abandon de l'ancien index AD 0.6 comme source de codes ICAO : ce document liste des noms d'aérodromes, pas une liste exploitable de codes.
- Pagination actuelle `page` prise en charge, avec suivi du lien `Page Suivant` et repli segmenté par préfixe ICAO si une recherche large est plafonnée.
- Les liens mobiles `/documents/download/...` servent uniquement à découvrir les ICAO ; chaque PDF est ensuite épinglé sur le DVD AIRAC immuable sélectionné.
- Construction des noms de DVD indépendante de la langue Windows (`JUL`, `AUG`, etc.).
- Ajout de reprises automatiques temporisées pour les erreurs HTTP transitoires du SIA.
- Ajout de tests hors ligne couvrant le chemin AIRAC, les liens mobiles et la pagination.

## Fonctionnel préservé

- Base radio exclusivement issue des publications officielles SIA.
- Aucune fréquence opérationnelle codée en dur.
- Moteur aérodrome dynamique DEV0.4.0.9, opérations sol, PTT, ASR, indicatifs et voix contrôleur inchangés.
