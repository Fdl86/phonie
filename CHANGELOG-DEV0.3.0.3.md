# Changelog PHONIE DEV0.3.0.3

## Indicatif et contexte

- suppression du faux indicatif `T-OUR` extrait de `Poitiers Tour` ;
- suppression des faux indicatifs `V-OICI` et `F-AUT` extraits de mots ordinaires ;
- retrait du nom de station avant l'analyse de l'immatriculation ;
- priorité à l'ATC ID SimConnect lorsqu'il est confirmé par la transmission ;
- rejet d'un indicatif divergent lorsque l'ATC ID du simulateur est connu ;
- ajout de variantes phonétiques françaises et d'erreurs ASR fréquentes ;
- rapprochement séquentiel tolérant jusqu'à quelques mots parasites ;
- aucune fabrication d'indicatif lorsque le score contextuel est insuffisant.

## Phraséologie

- `tours de pisse` et variantes proches sont rapprochés de `tours de piste` ;
- `information Alpes` est rapprochée de l'information Alpha ;
- conservation de la détection du parking, du roulage et des tours de piste.

## Mémoire ASR

- libération de Whisper lors du passage vers Vosk ;
- libération de Vosk lors du retour vers Whisper ;
- libération des deux modèles après le bouton Comparer ;
- les modèles sont rechargés à la prochaine transcription si nécessaire.

## Validation continue

- ajout d'un projet console `Phonie.SmokeTests` sans paquet de test externe ;
- exécution obligatoire des tests de phraséologie dans GitHub Actions avant publication ;
- conservation du build Windows x64 autonome.

## Limite maintenue

- le parser TaxiPath détaillé de la scène LFBI reste à corriger dans une version dédiée aux opérations au sol.


## Correctif CI des tests de phraséologie

- Les smoke tests ne référencent plus l’exécutable WPF autonome.
- Le moteur de phraséologie et ses modèles sont compilés comme sources liées dans un projet de test net8.0 indépendant.
- Le workflow compile les tests avant leur exécution et impose le SDK .NET 8 via global.json.
