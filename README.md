# PHONIE DEV0.3.0.5 - GPU TELEMETRY & TURBO WARMUP

PHONIE est une application Windows x64 portable destinée au contrôle aérien VFR vocal en français dans Microsoft Flight Simulator 2020 et 2024.

DEV0.3.0.5 clôt la branche technique DEV0.3 avec deux fonctions : le préchauffage automatique du profil Whisper Large-v3 Turbo Vulkan et un benchmark local GPU/VRAM.

## Préchauffage Turbo

Lorsque le profil `Whisper Large-v3 Turbo Vulkan - qualité` est sélectionné et que son modèle est installé, PHONIE initialise le moteur en arrière-plan :

- état visible pendant l'initialisation ;
- interface toujours utilisable ;
- modèle conservé en mémoire pour supprimer le délai du premier véritable appel ;
- nouvelle initialisation après une comparaison ou un benchmark ayant libéré les moteurs ;
- erreur visible et journalisée sans blocage de l'application.

Le modèle reste téléchargé uniquement à la demande dans `models\whisper` et son empreinte SHA-256 est vérifiée.

## Benchmark GPU / VRAM

Le bouton `Bench GPU` utilise le dernier WAV enregistré et exécute, lorsqu'ils sont installés :

- Whisper Small Vulkan ;
- Whisper Large-v3 Turbo Vulkan ;
- Vosk FR comme référence CPU.

Chaque moteur est exécuté trois fois : un passage à froid puis deux passages à chaud. PHONIE mesure notamment :

- temps de chargement du modèle ;
- temps d'inférence ;
- temps bout en bout ;
- utilisation GPU du processus PHONIE ;
- VRAM dédiée et mémoire GPU partagée du processus ;
- RAM et CPU du processus ;
- mémoire GPU immédiatement après libération puis après 30 secondes.

Les rapports sont écrits dans :

- `logs\benchmarks\gpu-benchmark-*.txt` ;
- `logs\benchmarks\gpu-benchmark-*.json`.

Les compteurs GPU sont ceux de Windows et ne nécessitent ni logiciel AMD, ni `nvidia-smi`, ni outil Intel. La mesure fonctionne donc avec les cartes AMD, NVIDIA et Intel lorsque le pilote expose les compteurs Windows. Lorsqu'une attribution fiable au processus n'est pas possible, le rapport l'indique explicitement.

## Profils ASR

- Whisper Base CPU - rapide ;
- Whisper Small CPU - équilibré ;
- Whisper Small Vulkan - GPU ;
- Whisper Large-v3 Turbo Vulkan - qualité ;
- Vosk FR - expérimental.

Le passage entre deux profils Vulkan ne demande pas de redémarrage. Le passage CPU vers Vulkan, ou Vulkan vers CPU, demande un redémarrage afin de charger le runtime natif adapté.

## Limites connues

Le décodage des `TaxiPaths` et le moteur de roulage restent hors du périmètre de DEV0.3. Ils constituent le chantier principal de DEV0.4.0 - GROUND OPERATIONS.

Commencer par `TEST-DEV0.3.0.5.md`.
