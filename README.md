# PHONIE DEV0.4.1.3 - eAIP AD 2.18 RADIO HOTFIX & CONTROLLER VOICES

Application ATC VFR locale, portable et sans installation pour MSFS 2020 et MSFS 2024.

## Correctif DEV0.4.1.3

La découverte SIA fonctionne désormais : le workflow DEV0.4.1.2 a trouvé et traité 420 aérodromes. Son échec venait ensuite de l'extraction radio des cartes Atlas VAC : plusieurs PDF utilisent des polices cartographiques dont la couche texte ne restitue pas correctement les tableaux, ce qui limitait la base à 113 fréquences.

Le générateur utilise maintenant cette chaîne officielle :

- export SIA XML/AIXM 4.5 lorsqu'il est directement accessible ;
- découverte des ICAO par le catalogue Atlas VAC ;
- extraction prioritaire de la rubrique eAIP AD 2.18 depuis la page HTML officielle ;
- repli sur le PDF eAIP complet AD 2 si le HTML n'est pas disponible ;
- Atlas VAC uniquement en dernier secours.

Le seuil national de 200 aérodromes et 150 fréquences n'a pas été abaissé. Aucune fréquence opérationnelle et aucune liste d'aérodromes de production ne sont intégrées au code.

## Fonctionnalités principales

- Détection dynamique des aérodromes conservée depuis DEV0.4.0.9.
- Base radio France générée exclusivement depuis les publications officielles du SIA.
- Facilities MSFS utilisé pour la géométrie, le trafic et la fréquence COM active, jamais comme source radio française de vérité.
- Gestion AIRAC `previous / current / next`, validation SHA-256 et retour à la dernière base SIA valide.
- Mise à jour portable depuis GitHub avec bouton `MAJ SIA` et contrôle automatique léger.
- Séparation entre station locale et service régional.
- A/A, CTAF, UNICOM, ATIS, AWS et messages enregistrés restent silencieux.
- Voix française homme ou femme attribuée aléatoirement à chaque station dialoguée, stable pendant la session.

## Compilation

Le workflow commence par les tests hors ligne, puis sonde en direct les rubriques AD 2.18 de deux aérodromes de validation avant de lancer la génération nationale. Il valide ensuite la base, compile tous les projets sans avertissement, exécute les Smoke Tests et Core Tests, puis publie l'artefact Windows x64 autonome.

Aucune version ne doit être considérée comme testable avant que le workflow complet soit vert.

## Test

Commencer par `TEST-DEV0.4.1.3.md`.

## Stockage portable

PHONIE écrit uniquement dans son propre dossier : `config`, `logs`, `recordings`, `cache`, `models` et `data`.
