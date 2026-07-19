# PHONIE DEV0.3.0 - FIRST CONTACT

## Interface

- maximisation limitée à la zone de travail du moniteur courant ;
- barre des tâches toujours préservée ;
- cartes de télémétrie compactées ;
- zone PTT resserrée ;
- journal technique placé dans un panneau Diagnostic ;
- nouveaux panneaux Échange radio, ATIS et Aérodrome actif.

## Reconnaissance

- Whisper.net 1.7.4 ;
- runtime CPU local ;
- modèle multilingue Small q5_1, environ 181 Mio ;
- téléchargement manuel depuis PHONIE ;
- vérification SHA-1 ;
- audio 16 kHz mono PCM ;
- transcription automatique facultative au relâchement du PTT ;
- mode laboratoire et retranscription du dernier PTT.

## Première logique ATC

- extraction de l'indicatif F-GABC ou de l'alphabet aéronautique ;
- station, position, intention et information ATIS ;
- première réponse écrite pour une demande de tours de piste au parking ;
- non-réponse obligatoire sur ATIS, répondeur et auto-information ;
- export JSONL des échanges.

## LFBI

- classification 118.505, 118.500, 121.780, 124.000 et 134.100 ;
- gestion explicite des doublons possibles du scenery custom ;
- ATIS texte expérimental ;
- sélection de la piste principale selon le vent.

## Airport Data

- structure TAXI_PATH complétée et réalignée ;
- validation des comptes, index, numéros et désignateurs ;
- départs non-piste conservés en brut mais filtrés de l'affichage opérationnel.
