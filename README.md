# PHONIE DEV0.3.0 - FIRST CONTACT

PHONIE est une application Windows x64 portable destinée à construire un contrôle aérien VFR vocal en français pour Microsoft Flight Simulator 2020 et 2024.

## Première avancée opérationnelle

DEV0.3.0 regroupe les corrections prévues pour DEV0.2.6 et le premier jalon vocal :

- interface compacte adaptée à la zone de travail Windows, sans passer sous la barre des tâches ;
- fiche aérodrome LFBI visible ;
- classification opérationnelle des fréquences ;
- ATIS texte expérimental à partir de la météo locale et des pistes SimConnect ;
- transcription locale du dernier PTT avec Whisper Small q5_1 multilingue ;
- analyse de la station appelée, de l'indicatif, de la position, de l'intention et de l'information ATIS ;
- première réponse ATC écrite pour un appel au parking en vue de tours de piste ;
- mode laboratoire permettant de tester une phrase sans refaire un vol ;
- export des échanges dans `logs\sessions`.

Vosk n'est pas intégré.

## Whisper

Le modèle retenu est `small-q5_1`, multilingue et configuré explicitement en français. Il représente le compromis initial poids/précision de PHONIE.

Le modèle n'est pas inclus dans l'archive compilée. Le bouton `Télécharger Small q5_1` l'enregistre dans :

```text
PHONIE\models\whisper\ggml-small-q5_1.bin
```

PHONIE vérifie l'empreinte SHA-1 officielle avant de l'accepter. Après ce téléchargement unique, la transcription fonctionne entièrement en local.

Le runtime CPU nécessite les instructions AVX/AVX2/FMA/F16C et le redistribuable Microsoft Visual C++ 2019 ou plus récent. La configuration cible actuelle répond aux exigences CPU ; le redistribuable est généralement déjà présent avec MSFS et les jeux récents.

## Profil radio LFBI

Le build distingue données brutes du simulateur et interprétation opérationnelle :

- `118.505` - Poitiers Tour MSFS 2024 - dialogue autorisé ;
- `118.500` - Poitiers Tour MSFS 2020 ou doublon hérité - dialogue autorisé ;
- `121.780` - ATIS - diffusion automatique, aucune réponse au pilote ;
- `124.000` - répondeur enregistré - aucune conversation ;
- `134.100` - Poitiers Approche / SIV - dialogue autorisé.

Le scenery LFBI personnalisé peut ajouter ou dupliquer des entrées. PHONIE conserve les données brutes dans les rapports et applique ensuite ses règles opérationnelles.

## Airport Data

La définition `TAXI_PATH` a été réalignée sur la structure complète de la Facilities API. Les champs de bord, d'éclairage et de ligne centrale sont lus avant les index de début et de fin. Les valeurs incohérentes génèrent maintenant des avertissements explicites.

Les départs de type non-piste restent disponibles dans les données brutes, mais ne sont plus présentés comme de vrais seuils.

## Arborescence portable

```text
PHONIE\
|-- PHONIE.exe
|-- config\
|   `-- settings.json
|-- logs\
|   |-- PHONIE-DEV0.3.0-*.log
|   |-- airport-data\
|   `-- sessions\
|-- models\
|   `-- whisper\
|-- recordings\
|   `-- last-ptt.wav
`-- cache\
```

PHONIE ne stocke pas ses réglages, journaux, enregistrements, modèles ou caches dans AppData ou le registre. Son dossier doit être accessible en écriture.

## Compilation

Le workflow GitHub Actions produit une version Windows x64 autonome. Le modèle Whisper reste séparé afin de ne pas alourdir chaque archive de build.

Commencer par `TEST-DEV0.3.0.md`.
