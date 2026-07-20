# PHONIE DEV0.4.0.1 - FACILITIES DIAGNOSTIC

PHONIE est une application Windows x64 portable destinée au contrôle aérien VFR vocal en français dans Microsoft Flight Simulator 2020 et 2024.

DEV0.4.0.1 est le premier build du chantier `GROUND OPERATIONS`. Son objectif unique est d'obtenir une preuve binaire fiable du contenu SimConnect Facilities, en particulier des `TaxiPaths`, avant la construction du graphe de roulage.

## Base conservée

Le build reprend sans modification volontaire du comportement :

- SimConnect MSFS 2020 et MSFS 2024 ;
- scanner radio et règles par type de service ;
- PTT clavier global et HOTAS ;
- enregistrement local ;
- Whisper Small CPU ;
- Whisper Small Vulkan ;
- Whisper Large-v3 Turbo Vulkan et son préchauffage ;
- Vosk FR expérimental ;
- installation et vérification des modèles ;
- comparaison ASR ;
- benchmark GPU, VRAM, CPU et RAM ;
- contexte ATC ID et protection contre les indicatifs inventés ;
- stockage exclusivement dans le dossier portable PHONIE.

## Diagnostic Facilities

Chaque demande d'aérodrome capture désormais :

- l'en-tête complet de chaque paquet `SIMCONNECT_RECV_FACILITY_DATA` ;
- la taille déclarée et la taille réellement reçue ;
- la version, les identifiants de demande et de parent ;
- le type de donnée, `IsListItem`, `ItemIndex` et `ListSize` ;
- le paquet binaire brut, en-tête compris ;
- les 16 champs de chaque `TaxiPath` avec offset, type, valeur et octets exacts ;
- les index dupliqués, manquants ou hors plage ;
- le premier `TaxiPath` impossible à décoder ou présentant des valeurs manifestement suspectes.

Les captures sont écrites dans :

```text
logs\airport-data\raw\airport-<ICAO>-<SIMULATEUR>-<DATE>\
```

Le dossier contient notamment :

- `diagnostic-summary.json` ;
- `packets.csv` ;
- `taxipath-fields.csv` ;
- les paquets `*.bin` ;
- `LISEZ-MOI.txt`.

Aucune valeur incohérente n'est remplacée, corrigée artificiellement ou masquée.

## Indicatifs pour le futur moteur de session

La règle DEV0.4 reste : premier contact avec l'indicatif complet, puis abréviation autorisée après établissement du contact ou emploi par le contrôleur. Exemple : `F-HNNY` devient `F-NY`, tout en restant lié en mémoire à `F-HNNY`.

DEV0.4.0.1 ne modifie pas encore la machine conversationnelle. Cette règle sera intégrée dans le build fonctionnel Ground Operations.

## Limite volontaire

Ce build ne construit pas encore de graphe, ne choisit pas de point d'attente et ne génère aucune nouvelle clairance de roulage. Les données réelles LFBI doivent d'abord confirmer le format binaire exact.

Commencer par `TEST-DEV0.4.0.1.md`.
