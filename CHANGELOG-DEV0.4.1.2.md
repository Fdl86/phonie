# Changelog DEV0.4.1.2

## Correctif CI SIA

- Correction de la cause réellement observée sur GitHub Actions : la recherche globale ponctuée `AD-2` renvoie une page sans document exploitable au générateur.
- Suppression de cette recherche comme point de départ.
- Découverte catalogue segmentée avec des préfixes ICAO alphanumériques valides de trois caractères : `LFA` à `LFZ`.
- Arrêt immédiat de la méthode catalogue après trois préfixes vides consécutifs, afin de ne plus perdre plus d'une minute dans une voie manifestement inutilisable.
- Ajout d'une seconde méthode indépendante : balayage léger des URL PDF du DVD AIRAC officiel immuable avec requêtes partielles, puis téléchargement complet uniquement des PDF réellement présents.
- Identité HTTP compatible avec le rendu public du site SIA.
- Diagnostics distincts pour la méthode catalogue et la méthode DVD directe.
- Tests hors ligne ajoutés pour la génération des candidats, la détection PDF partielle et le basculement automatique vers le DVD.

## Fonctionnel préservé

- Base radio exclusivement issue des publications officielles SIA.
- Aucun aérodrome ni aucune fréquence opérationnelle utilisé comme source codée en dur.
- Moteur aérodrome dynamique DEV0.4.0.9, opérations sol, PTT, ASR, indicatifs et voix contrôleur inchangés.
