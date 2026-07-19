# Architecture de légèreté DEV0.3.0

- télémétrie SimConnect autour de 1 Hz ;
- Airport Data demandé ponctuellement ;
- ATIS recalculé seulement lors d'un changement significatif ;
- modèle Whisper chargé uniquement lors de la première transcription ;
- une transcription à la fois ;
- aucun traitement vocal continu ;
- modèle absent de l'archive de build ;
- diagnostic CPU, mémoire, threads, handles et cadence conservé.

Le coût de Whisper doit être évalué séparément pendant la transcription et au repos après chargement du modèle.
