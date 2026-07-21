# Changelog DEV0.4.1.0

## Radio France

- Suppression de l'utilisation de toute table de fréquences codée en dur.
- Neutralisation de l'ancien fichier `france-official.json`.
- Nouveau catalogue générique SIA avec canaux entiers et normalisation 8,33 kHz.
- Gestion des stations locales et régionales.
- Facilities MSFS interdit comme secours radio pour les ICAO français.
- Gestion `previous / current / next` et activation locale à la date AIRAC.
- Validation du schéma, de la couverture, des statistiques et du SHA-256.
- Mise à jour portable via manifest GitHub.
- Workflow SIA avec contrôle léger quotidien et génération lourde uniquement lors d'un changement de publication.

## Voix

- Attribution aléatoire homme ou femme par station dialoguée.
- Voix stable pendant la session.
- Cache audio séparé par station et identité vocale.
- Diagnostic du nombre de voix françaises disponibles et de la voix active.

## Préservé

- Détection dynamique et changement automatique d'aérodrome de DEV0.4.0.9.
- Routage sol, points d'attente, trafic, PTT, ASR et indicatif.
