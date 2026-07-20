# Changelog PHONIE DEV0.3.0.5

## Ajouté

- préchauffage automatique de Whisper Large-v3 Turbo Vulkan lorsque ce profil est actif et installé ;
- états visibles `Initialisation`, `Prêt` et `Erreur` ;
- bouton `Bench GPU` utilisant le dernier WAV enregistré ;
- trois passages par moteur : un à froid et deux à chaud ;
- mesure GPU Windows indépendante du constructeur ;
- mesure de la VRAM dédiée et de la mémoire GPU partagée du processus PHONIE ;
- mesure CPU, RAM, chargement du modèle, inférence et durée bout en bout ;
- mesure de la mémoire GPU après libération immédiate puis après 30 secondes ;
- exports TXT et JSON dans `logs\benchmarks`.

## Modifié

- les résultats de transcription enregistrent désormais séparément le chargement, l'inférence et la durée totale ;
- les modèles sont libérés proprement pendant le benchmark puis le profil Turbo actif est préchauffé de nouveau ;
- le workflow GitHub crée le dossier portable `logs\benchmarks`.

## Inchangé

- règles radio ;
- contexte d'indicatif SimConnect ;
- modèles et empreintes de téléchargement ;
- données Airport Data ;
- défaut de décodage TaxiPath, reporté à DEV0.4.0.
