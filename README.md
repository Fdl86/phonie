# PHONIE DEV0.4.1.0 - SIA RADIO & CONTROLLER VOICES

Application ATC VFR locale, portable et sans installation pour MSFS 2020 et MSFS 2024.

## Nouveautés principales

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

Commencer par `TEST-DEV0.4.1.0.md`.

## Stockage portable

PHONIE écrit uniquement dans son propre dossier : `config`, `logs`, `recordings`, `cache`, `models` et `data`.
