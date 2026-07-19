# Changelog PHONIE DEV0.3.0.4

## Ajout

- profil `Whisper Large-v3 Turbo Vulkan - qualité` ;
- téléchargement du modèle `ggml-large-v3-turbo-q5_0.bin` ;
- vérification SHA-256 avant installation ;
- sélection à chaud entre Small Vulkan et Large-v3 Turbo Vulkan ;
- intégration du profil Turbo au comparateur lorsque PHONIE démarre avec le runtime Vulkan ;
- smoke test vérifiant la présence et la nature Vulkan du nouveau profil.

## Conservé

- récupération de l'ATC ID depuis MSFS ;
- normalisation contextuelle des indicatifs ;
- Whisper Base et Small CPU ;
- Whisper Small Vulkan ;
- Vosk FR expérimental ;
- classification radio et silence sur les services non dialogués.
