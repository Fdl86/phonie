# Ground Operations - DEV0.4.0.7

## Règle de point d'attente

La géométrie Facilities est autoritaire. Tout nœud construit comme `HOLD_SHORT` est valable pour l'annonce prêt au départ.

Les profils locaux et leurs rôles ne conditionnent plus la décision. Ils servent uniquement à enrichir la carte, les noms et le diagnostic.

Pour calculer le roulage, PHONIE préfère les points associés à la piste sélectionnée. Si aucune association exploitable n'existe, il recherche un itinéraire vers tous les véritables points d'attente accessibles.

La tolérance de localisation autour d'un nœud `HOLD_SHORT` est portée à 55 mètres afin d'absorber les décalages fréquents entre le nœud Facilities et la ligne peinte ou le panneau de la scène.

## Phraseologie générique

Tour : `roulez au point d'attente et rappelez prêt`.

AFIS : piste, vent, QNH et trafic disponibles, sans instruction impérative.

Au prochain appel, la position réelle, l'état de la piste et la disponibilité du trafic pilotent la décision. Le nom prononcé par le pilote n'est pas utilisé pour autoriser le départ.
