# Profils ASR - PHONIE DEV0.3.0.2

## Objectif

Comparer la latence et la précision sans changer d'enregistrement ni de logique ATC.

## Whisper Base CPU

- fichier : `ggml-base-q5_1.bin` ;
- emplacement : `models\whisper` ;
- usage : profil rapide ;
- langue forcée : français.

## Whisper Small CPU

- fichier : `ggml-small-q5_1.bin` ;
- emplacement : `models\whisper` ;
- usage : profil équilibré et précision CPU ;
- langue forcée : français.

## Whisper Small Vulkan

- même modèle Small ;
- runtime natif Vulkan chargé en priorité ;
- runtime CPU placé en secours technique si Vulkan est indisponible ;
- changement CPU/Vulkan appliqué au prochain démarrage.

## Vosk FR

- modèle : `vosk-model-small-fr-0.22` ;
- emplacement : `models\vosk` ;
- moteur indépendant ;
- usage expérimental et comparaison ;
- aucun basculement automatique depuis Whisper.

## Mode Comparer

Le même fichier `recordings\last-ptt.wav` est préparé séparément pour chaque moteur. Les résultats affichent :

- nom du profil ;
- temps de traitement ;
- transcription ;
- modèle absent ou erreur éventuelle.

## Décision future

Le profil par défaut sera choisi sur les mesures réelles :

- temps entre relâchement PTT et transcription ;
- reconnaissance de l'ATC ID ;
- station appelée ;
- position ;
- intention ;
- nombres critiques ;
- impact CPU, mémoire et fluidité MSFS.
