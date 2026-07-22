# Changelog DEV0.4.1.5

## Fermeture de la compilation complète

- Correction de `CS0246` dans `RadioDataUpdateService.cs`.
- Ajout explicite de `using System.Net.Http;` pour le type `HttpClient`.
- Le journal DEV0.4.1.4 avait compilé sans erreur `Phonie.Core`, `Phonie.SmokeTests` et `Phonie.Core.Tests`; le compilateur du projet WPF complet n'avait remonté qu'un seul diagnostic, désormais corrigé.

## CI renforcée

- Le projet WPF PHONIE est compilé en premier avec les avertissements traités comme erreurs.
- Les Smoke Tests et Core Tests sont exécutés avant toute collecte SIA.
- Une prépublication autonome Windows x64 est réalisée avant la collecte réseau afin de détecter immédiatement une erreur spécifique à `dotnet publish`.
- Le dossier de prépublication est supprimé avant la génération et la publication finale.

## Données radio

- Aucun changement du parseur ou des règles radio.
- La dernière exécution a généré 420 aérodromes et 672 fréquences, puis validé la base nationale et l'absence de fréquence codée dans `src`.
