# PHONIE DEV0.3.0.4 - TURBO QUALITY PROFILE

PHONIE est une application Windows x64 portable destinée au contrôle aérien VFR vocal en français dans Microsoft Flight Simulator 2020 et 2024.

Cette version conserve les corrections d'indicatif de DEV0.3.0.3 et ajoute un profil de reconnaissance plus précis pour les machines équipées d'un GPU Vulkan.

## Nouveau profil

- Whisper Large-v3 Turbo q5_0 multilingue ;
- exécution Vulkan avec retour CPU déjà fourni par le runtime Whisper ;
- modèle téléchargé uniquement à la demande ;
- fichier stocké dans `models\whisper` ;
- taille affichée : environ 548 Mio ;
- contrôle SHA-256 avant installation ;
- aucune inclusion du modèle dans le ZIP PHONIE.

## Profils ASR disponibles

- Whisper Base CPU - rapide ;
- Whisper Small CPU - équilibré ;
- Whisper Small Vulkan - GPU ;
- Whisper Large-v3 Turbo Vulkan - qualité ;
- Vosk FR - expérimental.

Le passage entre deux profils Vulkan ne demande pas de redémarrage. Le passage CPU vers Vulkan, ou Vulkan vers CPU, demande toujours un redémarrage afin de charger le bon runtime natif.

## Comparaison

Lorsque PHONIE a démarré en mode Vulkan, le bouton `Comparer` peut maintenant tester sur le même WAV :

- Whisper Small Vulkan ;
- Whisper Large-v3 Turbo Vulkan ;
- Vosk FR.

Les moteurs sont libérés après la comparaison afin de ne pas conserver plusieurs modèles simultanément pendant le vol.

## Contrôle automatique

GitHub Actions compile PHONIE, compile les smoke tests et vérifie également que le profil Large-v3 Turbo Vulkan est bien enregistré comme moteur Whisper Vulkan avant la publication.

Les limites TaxiPath déjà connues restent hors du périmètre de cette version.

Commencer par `TEST-DEV0.3.0.4.md`.
