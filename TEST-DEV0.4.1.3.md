# Protocole DEV0.4.1.3

## 0. Workflow GitHub obligatoire

Ne télécharger l'artefact que si le workflow entier est vert.

Les étapes attendues sont :

1. tests hors ligne du générateur ;
2. sonde eAIP officielle LFBI et LFOU ;
3. génération nationale ;
4. validation de la base et absence de fréquences codées en dur ;
5. restauration .NET ;
6. compilation de tous les projets sans avertissement ;
7. Smoke Tests ;
8. Core Tests ;
9. publication Windows x64.

La sonde doit afficher une ou plusieurs fréquences pour chacun des deux ICAO, depuis `SIA eAIP HTML AD 2` ou `SIA eAIP PDF AD 2`.

La génération nationale doit ensuite afficher au moins 200 aérodromes et 150 fréquences. Elle doit également indiquer combien d'aérodromes ont été lus par eAIP HTML, eAIP PDF et Atlas VAC.

## 1. Lancement

Vider seulement le dossier `logs`, lancer MSFS 2024 sur un parking, puis lancer PHONIE.

Vérifier dans l'en-tête :

```text
DEV0.4.1.3 - SIA RADIO & CONTROLLER VOICES
```

La ligne SIA doit afficher un cycle actif et une base valide. Elle ne doit pas afficher `amorçage` ou `indisponible`.

## 2. Changement d'aérodrome

Démarrer sur LFBI, se téléporter vers LFOU, puis vers un troisième terrain sans redémarrer PHONIE.

Attendu :

- contexte sol rechargé automatiquement ;
- pistes, parkings, taxiways et points d'attente renouvelés ;
- ICAO et moyens radio du nouvel aérodrome ;
- aucune recommandation locale héritée du terrain précédent.

## 3. Services radio

Tester au minimum :

- un terrain contrôlé ;
- un AFIS ;
- un terrain A/A ;
- une fréquence ATIS.

Attendu :

- Tour, Approche et AFIS classés correctement depuis la base SIA ;
- A/A et ATIS reconnus mais silencieux ;
- aucune réponse lorsque les horaires officiels restent ambigus ;
- la Tour reste prioritaire sur A/A lorsqu'un service dialogué opérationnel existe.

## 4. Scénario sol complet

Depuis un parking : premier contact, roulage, arrivée à un vrai point d'attente, annonce prêt, instruction d'alignement/décollage.

Contrôler le diagnostic de route et transmettre le dossier `logs` au premier comportement incohérent.

## 5. Voix et portabilité

La voix d'une station doit rester stable pendant la session et pouvoir différer sur une autre station. Aucun fichier ne doit être créé hors du dossier PHONIE.

## Rapport en cas d'échec

Transmettre le log GitHub complet ou le ZIP de `logs`, avec aérodrome, canal COM1, ligne Base SIA, lignes SOL/RADIO/Source/Voix et capture de l'écran principal.
