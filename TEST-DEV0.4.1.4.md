# Protocole DEV0.4.1.4

## Objectif immédiat

Valider le correctif C# `DateTimeOffset?` et obtenir un workflow entièrement vert sans attendre la génération SIA avant de découvrir une erreur de compilation.

## Ordre attendu du workflow

1. Installation .NET 8.
2. Précontrôle de l'arbre source.
3. Restauration des quatre projets.
4. Compilation de Phonie.Core, Smoke Tests, Core Tests et PHONIE avec zéro avertissement.
5. Exécution des Smoke Tests et Core Tests.
6. Installation Python et tests du générateur.
7. Sonde eAIP LFBI/LFOU.
8. Génération et validation de la base SIA.
9. Publication Windows x64 et contrôle du paquet portable.

## Résultat attendu

- aucune erreur CS0173 ;
- quatre compilations réussies avec 0 avertissement et 0 erreur ;
- Smoke Tests et Core Tests réussis ;
- base SIA d'au moins 200 aérodromes et 150 fréquences ;
- fichier `data/radio/france/current/airports-fr.json` présent ;
- artefact `PHONIE-DEV0.4.1.4-win-x64` publié.

## Test MSFS après workflow vert

Reprendre le protocole opérationnel DEV0.4.1.3 : LFBI, LFOU, changement de contexte sans redémarrage, services silencieux, priorité Tour/Sol/Approche, PTT et voix contrôleur.
