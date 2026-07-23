# PHONIE DEV0.4.2.0 R3

Source propre de **PHONIE DEV0.4.2.0 - Radio Intent & Contextual ASR**.

La R3 corrige l'intégration du projet `Phonie.SmokeTests` : les tests référencent désormais directement `Phonie.Core` au lieu de recopier partiellement ses fichiers. La solution complète est restaurée et compilée en une seule étape CI avec les avertissements traités comme erreurs.

## Mise à jour du dépôt

1. Conserver uniquement le dossier `.git` dans le dépôt local.
2. Extraire tout le contenu de ce ZIP directement à la racine.
3. Vérifier les suppressions et ajouts dans GitHub Desktop.
4. Committer et pousser.
5. Utiliser uniquement l'artefact Windows produit par un workflow GitHub Actions entièrement vert.

Aucune commande de renormalisation, aucun script PowerShell et aucun ancien document de version ne sont nécessaires.

## Contenu documentaire

- `docs/CHANGELOG-DEV0.4.2.0.md` : fonctions et corrections de la version.
- `docs/TEST-DEV0.4.2.0.md` : protocole et critères de validation.
- `docs/TECHNICAL-NOTES-DEV0.4.2.0.md` : architecture radio, ASR, session et base SIA.

## Validation CI attendue

Le workflow doit réussir successivement : précontrôle source/SHA, restauration de `PHONIE.sln`, compilation complète à zéro avertissement, Smoke Tests, Core Tests, prépublication Windows x64, tests et génération SIA, validation radio, publication puis création de l'artefact portable.
