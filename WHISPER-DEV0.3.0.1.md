# Whisper dans PHONIE DEV0.3.0.1

## Moteur retenu

PHONIE utilise uniquement Whisper via Whisper.net et le runtime natif whisper.cpp.

Modèle par défaut :

```text
ggml-small-q5_1.bin
```

Caractéristiques de l'intégration :

- modèle multilingue ;
- langue forcée sur `fr` ;
- environ 181 Mio sur disque ;
- audio préparé en PCM 16 bits, mono, 16 kHz ;
- traitement au relâchement du PTT ;
- fonctionnement local après téléchargement ;
- modèle conservé dans le dossier portable PHONIE ;
- empreinte SHA-1 contrôlée avant installation : `6fe57ddcfdd1c6b07cdcc73aaf620810ce5fc771`.

## Ce que PHONIE évalue

La réussite n'est pas seulement une transcription littérale parfaite. L'analyse vérifie séparément :

- station appelée ;
- indicatif ;
- position ;
- intention ;
- information ATIS.

Le mode laboratoire permet de corriger une transcription puis de relancer l'analyse sans refaire l'enregistrement.

## Limites du build

- une seule transcription à la fois ;
- pas de reconnaissance continue hors PTT ;
- pas encore de voix synthétique ATC ;
- pas encore de tour de piste complet ;
- scénario ATC écrit limité au premier contact au parking.
