# Architecture de légèreté - DEV0.2.4

## Principes conservés

- lecture SimConnect principale autour de 1 Hz ;
- météo locale mise en cache pendant 30 secondes ;
- HOTAS interrogé à 20 Hz uniquement pour les périphériques connectés ;
- diagnostic échantillonné toutes les 5 secondes ;
- indicateur micro à 8 Hz ;
- aucune boucle agressive ;
- aucune génération audio récurrente.

## Optimisations ajoutées

- lecture de chauffe SimConnect avant le premier snapshot complet ;
- variables optionnelles lancées seulement après réussite des variables principales ;
- délai de 30 secondes avant nouvelle tentative d'une variable optionnelle en échec ;
- libération explicite des périphériques audio inventoriés ;
- un seul fichier PTT valide conservé ;
- traitement du gain directement sur les échantillons capturés ;
- limiteur par simple écrêtage numérique à -1 dBFS ;
- rotation des logs à 10 sessions.

Le traitement du gain est une multiplication simple par échantillon. Son coût CPU attendu reste négligeable face au simulateur.
