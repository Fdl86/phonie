# Third-party notices

PHONIE DEV0.4.0.4 utilise :

- `SimConnect.NET` 0.2.1 - licence MIT ;
- `NAudio` 2.3.0 - licence MIT ;
- `Whisper.net` 1.7.4 - licence MIT ;
- `Whisper.net.Runtime` 1.7.4 - runtime CPU natif whisper.cpp ;
- `Whisper.net.Runtime.Vulkan` 1.7.4 - runtime Vulkan natif whisper.cpp ;
- `Vosk` 0.3.38 - moteur de reconnaissance hors ligne ;
- `System.Speech` 8.0.0 - synthèse vocale Windows locale ;
- modèles `ggml-base-q5_1.bin` et `ggml-small-q5_1.bin`, téléchargés séparément depuis le dépôt de modèles whisper.cpp ;
- modèle `vosk-model-small-fr-0.22`, téléchargé séparément depuis le site officiel Vosk.

Les modèles ne sont pas redistribués dans le dépôt source ni dans l'archive PHONIE produite par défaut. L'utilisateur déclenche leur téléchargement dans l'application.

L'icône PHONIE intégrée à cette version a été créée spécifiquement pour le projet à partir de la maquette visuelle validée.

- Modèle optionnel Whisper Large-v3 Turbo q5_0, téléchargé depuis le dépôt ggerganov/whisper.cpp sur Hugging Face.

## Données aéronautiques SIA

La base radio France de PHONIE est dérivée des publications du Service de l'Information Aéronautique français. Le cycle AIRAC, la date d'effet et la source sont affichés dans l'application et conservés dans le jeu de données généré.

PHONIE n'est ni certifié ni approuvé par le SIA. Les données restent soumises aux conditions de réutilisation publiées par le SIA. Les NOTAM, SUP AIP et documents officiels applicables doivent être consultés avant tout vol réel.
