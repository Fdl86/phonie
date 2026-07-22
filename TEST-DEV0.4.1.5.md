# Protocole DEV0.4.1.5

## Résultat CI attendu

Avant toute génération SIA, les étapes suivantes doivent être vertes :

1. `Restore projects`
2. `Build PHONIE app first with zero warnings`
3. `Build test projects with zero warnings`
4. `Run smoke tests`
5. `Run core tests`
6. `Preflight autonomous Windows x64 publish`

La prépublication doit contenir `PHONIE.exe`, puis être supprimée.

La seconde partie doit ensuite afficher :

- tests Python SIA réussis ;
- sondes eAIP LFBI et LFOU réussies ;
- 420 aérodromes détectés ou une couverture nationale supérieure aux minima ;
- au moins 200 aérodromes et 150 fréquences validés ;
- publication autonome Windows x64 réussie ;
- artefact `PHONIE-DEV0.4.1.5-win-x64` publié.

## Contrôles après téléchargement

- Extraire l'artefact une seule fois.
- Vérifier `PHONIE.exe` et `BUILD-INFO.txt` à la racine.
- Vérifier `data/radio/france/current/airports-fr.json`.
- Lancer PHONIE hors de `Program Files` et confirmer l'affichage `DEV0.4.1.5`.
