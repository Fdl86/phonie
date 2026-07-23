# Notes techniques PHONIE DEV0.4.2.0 R3

## Architecture des projets

- `Phonie.Core` contient les modèles et moteurs radio indépendants de WPF.
- `Phonie` référence `Phonie.Core` et contient l'interface, SimConnect, l'audio et les services applicatifs.
- `Phonie.Core.Tests` référence `Phonie.Core`.
- `Phonie.SmokeTests` référence également `Phonie.Core` et compile seulement les quelques services applicatifs non-WPF nécessaires aux tests.
- Aucun projet de tests ne référence l'exécutable WPF autonome.

## ASR contextuel

Le prompt Whisper est reconstruit à chaque PTT. Il contient l'indicatif SimConnect, la piste, le point d'attente et les expressions plausibles. La station n'est incluse qu'avant l'établissement du contact.

Garde-fous initiaux : durée minimale 180 ms, niveau minimal -62 dBFS, seuil de non-parole, segment unique, absence de contexte hérité, rejet des descriptions de bruit et des sorties ressemblant au prompt.

## Mémoire radio et session

Chaque contact est indexé par la clé normalisée de l'organisme et non par la fréquence seule. Un passage temporaire sur ATIS ne détruit pas le contact Tour ou SIV. Un changement d'organisme ouvre un nouveau contact.

La session complète est effacée sur chargement ou redémarrage du vol, arrêt/démarrage de la simulation, perte de connexion, changement d'appareil ou d'indicatif, ou action manuelle de nouvelle session.

## Base radio SIA

Les jeux référencés par manifest sont vérifiés sur leurs octets exacts. Les chemins suivants sont marqués `-text` :

- `data/radio/france/**`
- `tests/Phonie.SmokeTests/Fixtures/radio/france/**`
- `data/airports/**/*.json`

Le workflow contrôle l'attribut Git effectif, l'absence de CRLF et les SHA-256 avant la compilation. La sérialisation du manifest reste en camelCase.

## Déploiement portable

L'application, les modèles, les enregistrements, les journaux, les caches et les configurations restent sous le dossier PHONIE. Aucun script manuel n'est requis pour préparer le dépôt ou exécuter l'application.
