# PHONIE DEV0.4.1.1 - SIA CATALOG HOTFIX & CONTROLLER VOICES

Application ATC VFR locale, portable et sans installation pour MSFS 2020 et MSFS 2024.

## Correctif DEV0.4.1.1

- Découverte du catalogue Atlas VAC compatible avec la pagination SIA actuelle.
- Téléchargements épinglés sur le DVD immuable du cycle AIRAC sélectionné.
- Reprises réseau et génération indépendante de la langue du runner Windows.

## Fonctionnalités principales

- Détection dynamique des aérodromes conservée depuis DEV0.4.0.9.
- Base radio France générée exclusivement depuis les publications officielles du SIA.
- Aucune fréquence opérationnelle codée dans l'application pour LFBI, LFOU, LFJB, LFFW ou tout autre terrain.
- Facilities MSFS reste utilisé pour la géométrie, le trafic et la fréquence COM active, jamais comme source radio française de vérité.
- Gestion AIRAC `previous / current / next`, validation SHA-256 et retour à la dernière base SIA valide.
- Mise à jour portable depuis GitHub avec bouton `MAJ SIA` et contrôle automatique léger.
- Séparation entre station locale et service régional.
- A/A, CTAF, UNICOM, ATIS, AWS et messages enregistrés restent silencieux.
- Voix française homme ou femme attribuée aléatoirement à chaque station dialoguée, stable pendant la session.

## Compilation

Le workflow GitHub génère la base SIA active lorsque le dépôt est encore en mode amorçage, valide les données, compile, exécute les tests puis publie l'artefact Windows x64 autonome.

Aucune version ne doit être considérée comme testable avant que le workflow complet soit vert.

## Test

Commencer par `TEST-DEV0.4.1.1.md`.

## Stockage portable

PHONIE écrit uniquement dans son propre dossier : `config`, `logs`, `recordings`, `cache`, `models` et `data`.
