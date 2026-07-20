# PHONIE DEV0.3.0.5 - Benchmark GPU / VRAM

## Conditions

- démarrer PHONIE avec un profil Whisper Vulkan ;
- installer Small Vulkan et Turbo Vulkan pour une comparaison complète ;
- Vosk reste facultatif ;
- enregistrer un PTT de référence de 5 à 10 secondes ;
- cliquer sur `Bench GPU`.

Le test dure environ 40 à 60 secondes selon les moteurs, dont 30 secondes consacrées à la mesure de libération mémoire.

## Mesures

La télémétrie repose sur les compteurs natifs Windows `GPU Engine` et `GPU Process Memory`, interrogés avec les noms anglais PDH afin de fonctionner sur un Windows localisé.

L'utilisation GPU du processus correspond au moteur GPU associé au PID PHONIE le plus sollicité à chaque échantillon. Elle reste donc comprise entre 0 et 100 %. La VRAM est la somme des instances de mémoire GPU associées au PID PHONIE.

Le rapport distingue :

- backend Vulkan demandé ;
- activité GPU confirmée par les compteurs ;
- activité non confirmée, pouvant indiquer un fallback CPU ou des compteurs indisponibles.

Aucune consommation globale de la carte n'est présentée comme une consommation certaine de PHONIE.
