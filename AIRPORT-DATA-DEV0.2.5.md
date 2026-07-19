# PHONIE DEV0.2.5 - Airport Data

## But du build

Cette version teste la lecture structurée des installations aéroportuaires fournies par le simulateur connecté.

LFBI est la première cible de validation, mais l'architecture accepte tout code OACI composé de quatre caractères.

## Données demandées

PHONIE utilise la Facilities API de SimConnect pour demander :

- identité, nom, région, position, altitude et variation magnétique de l'aérodrome ;
- pistes, dimensions, orientations, surfaces et désignateurs ;
- positions de départ ;
- fréquences et types de service ;
- parkings ;
- points, chemins et noms de taxiways.

Les fréquences ne sont pas codées en dur. Le rapport reflète la base du simulateur connecté, y compris les différences éventuelles entre MSFS 2020 et MSFS 2024.

## Déclenchement

La lecture est demandée automatiquement lorsque PHONIE identifie un code OACI exploitable sur COM1. Près de Poitiers, LFBI est aussi utilisé comme cible géographique de premier test.

Le bouton `Lire LFBI` permet de forcer manuellement une nouvelle demande lorsque SimConnect est connecté.

## Rapports portables

Chaque lecture réussie produit deux fichiers dans :

```text
PHONIE\logs\airport-data\
```

- un fichier JSON complet pour l'analyse technique ;
- un fichier TXT lisible rapidement.

Les dix derniers rapports sont conservés. PHONIE ne crée aucun rapport Airport Data hors de son propre répertoire.

## Nature expérimentale

Cette version sert à observer exactement les données renvoyées par chaque simulateur. Une donnée absente ou incohérente doit être signalée à partir des fichiers JSON et TXT, sans la remplacer silencieusement par une valeur inventée.
