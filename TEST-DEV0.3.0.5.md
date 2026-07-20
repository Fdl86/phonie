# Protocole rapide PHONIE DEV0.3.0.5

## 1. Compilation

GitHub Actions doit afficher :

```text
Build succeeded.
PHONIE phraseology smoke tests OK
```

## 2. Préchauffage Turbo

1. Sélectionner `Whisper Large-v3 Turbo Vulkan - qualité`.
2. Redémarrer PHONIE si la session précédente était CPU.
3. Vérifier que le modèle Turbo est installé.
4. Relancer PHONIE.

Résultat attendu :

- message `Initialisation du moteur qualité Vulkan` ;
- interface non figée ;
- état final `Moteur qualité prêt` ;
- événement `ASR_WARMUP` dans le log.

## 3. Appel radio après préchauffage

Dire :

> Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, prêt au point d'attente piste zéro trois pour un départ vers le nord-ouest, deux personnes à bord, demande alignement et décollage.

Le premier appel réel ne doit plus subir le délai de chargement de plusieurs secondes. Le temps d'inférence Turbo doit rester proche des mesures à chaud précédentes.

## 4. Benchmark GPU

1. Conserver le dernier WAV.
2. Cliquer sur `Bench GPU`.
3. Attendre la fin des trois passages par moteur et des 30 secondes de refroidissement.

Résultat attendu :

- interface réactive ;
- rapport TXT et JSON dans `logs\benchmarks` ;
- passages 1, 2 et 3 pour chaque moteur installé ;
- temps de chargement surtout visible au passage 1 ;
- temps à chaud visibles aux passages 2 et 3 ;
- VRAM avant, au pic, après libération et après 30 secondes ;
- mention explicite lorsque Windows ne permet pas de confirmer le backend GPU.

## 5. Réinitialisation

Après le benchmark, si Turbo est toujours le profil sélectionné, PHONIE doit le préchauffer de nouveau et revenir à l'état prêt.
