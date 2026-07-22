# Base radio SIA - DEV0.4.1.2

## Principe

Pour les aérodromes français, les fréquences, canaux, indicatifs et types de service proviennent exclusivement d'une base générée depuis les publications officielles du Service de l'Information Aéronautique.

Aucune fréquence d'aérodrome n'est codée dans le code de production. Les terrains utilisés dans les tests ne sont que des fixtures de validation et ne sont jamais chargés par l'application réelle.

## Sources

Le générateur privilégie l'export officiel SIA XML/AIXM 4.5. Si l'archive directe n'est pas accessible au workflow, il utilise le jeu officiel Atlas VAC du cycle applicable.

La découverte Atlas VAC dispose de deux méthodes indépendantes : recherches catalogue par préfixes ICAO et balayage partiel des PDF présents sur le DVD AIRAC immuable. Le téléchargement complet et l'extraction ne commencent qu'après validation de l'existence du PDF.

La source, le cycle AIRAC, les dates d'effet, la version du générateur, la révision et le SHA-256 sont conservés dans les fichiers générés.

## Structure portable

```text
PHONIE/data/radio/france/
  manifest.json
  previous/airports-fr.json
  current/airports-fr.json
  next/airports-fr.json
  .staging/
```

`previous` est le seul secours autorisé. Facilities MSFS ne remplace jamais une donnée radio française officielle absente ou invalide.

## Activation AIRAC

Un cycle `next` peut être téléchargé à l'avance. PHONIE ne l'active qu'à sa date d'effet. L'ancien `current` devient alors `previous`.

## Mise à jour

Le workflow quotidien commence par une vérification légère du catalogue officiel. Le traitement lourd n'est lancé que lorsque la signature de publication change, lorsqu'un corrigendum apparaît ou lors d'une exécution manuelle forcée.

Dans PHONIE, le bouton `MAJ SIA` télécharge seulement un manifest et les jeux de données dont la révision a changé. Chaque fichier est vérifié avant remplacement atomique.

## Sécurité

PHONIE reste silencieux lorsque :

- la base officielle est indisponible et aucune précédente base valide n'existe ;
- le canal n'est pas publié pour l'aérodrome détecté ;
- plusieurs modes incompatibles partagent un canal avec des horaires non interprétables ;
- le service est A/A, CTAF, UNICOM, ATIS, AWS ou message enregistré.

Les horaires complexes, HX, PPR ou dépendants d'un NOTAM ne sont jamais inventés.
