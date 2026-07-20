# ATC ID et indicatif - PHONIE DEV0.3.0.5

## Source de vérité

L'identifiant attendu vient directement du simulateur via la SimVar `ATC ID`.

PHONIE ne demande pas à l'utilisateur de recopier manuellement l'immatriculation pour ce jalon.

## Chaîne de traitement

1. lecture SimConnect ;
2. nettoyage des espaces et caractères non alphanumériques ;
3. formatage lisible, par exemple `FGABC` vers `F-GABC` ;
4. transcription ASR ;
5. recherche directe de l'immatriculation ;
6. recherche de l'alphabet aéronautique ;
7. rapprochement phonétique avec l'ATC ID ;
8. calcul d'un score de confiance.

## Exemples ciblés

Les formulations suivantes doivent pouvoir être rapprochées de `F-GABC` lorsque l'ATC ID du simulateur vaut `F-GABC` :

```text
Fox Golf Alfa Marvo Charlie
Fox Gold Fabre Beauchardie
Fox Gold va pas Bravo Charlie
FoxGolf Alfa Bravo Charlie
Fox Colf Alfa Bravo Charlie
```

Le moteur conserve une réponse de demande de répétition lorsque le score reste insuffisant.

## Sécurité

L'ATC ID sert de contexte, pas de remplacement aveugle. Une phrase sans trace phonétique crédible de l'indicatif ne doit pas être validée automatiquement.
