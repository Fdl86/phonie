# Changelog PHONIE DEV0.3.0.2

## Reconnaissance vocale

- ajout de Whisper Base q5_1 CPU ;
- conservation de Whisper Small q5_1 CPU ;
- ajout du runtime Whisper Vulkan 1.7.4 ;
- ajout de Vosk FR 0.3.38 avec modèle Small FR 0.22 optionnel ;
- sélection persistante du profil ASR ;
- redémarrage explicite lors d'un changement CPU/Vulkan ;
- comparaison des profils installés sur le même WAV ;
- journalisation du moteur, du temps et de la transcription ;
- préparation audio commune en PCM 16 bits, mono, 16 kHz.

## Indicatif avion

- lecture de la SimVar `ATC ID` ;
- affichage de l'identifiant dans la carte Simulateur ;
- transmission de l'identifiant au moteur de phraséologie ;
- normalisation générique des immatriculations ;
- rapprochement phonétique et contextuel ;
- score et source de détection visibles dans l'analyse ;
- tolérance renforcée aux erreurs courantes de Whisper.

## Modèles et sécurité

- téléchargement séparé de chaque modèle ;
- contrôle SHA-256 des modèles Whisper ;
- contrôle SHA-256 de l'archive Vosk ;
- extraction Vosk protégée contre les chemins sortant du dossier cible ;
- aucun basculement automatique vers Vosk ;
- stockage uniquement sous `models\whisper` et `models\vosk`.

## Audio

- gain par défaut +9 dB pour une nouvelle configuration ;
- conservation du gain existant lors d'une mise à jour.

## Compatibilité

- migration automatique des réglages DEV0.3.0.1 ;
- First Contact, ATIS, règles radio, PTT et Airport Data conservés.

## Limites connues

- le changement entre runtime Whisper CPU et Vulkan nécessite un redémarrage ;
- les performances Vulkan doivent être validées sur la RX 7800 XT ;
- Vosk reste expérimental ;
- le décodage complet de certains champs TaxiPath reste non résolu avec le scenery LFBI custom.
