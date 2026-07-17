# Règles de comportement radio — base DEV0.2.3

Ce document fixe le comportement cible sans encore générer de réponse audio.

## Organisme contrôlé

Types SimConnect : `TWR`, `GND`, `CLR`, `APPR`, `DEP`.

PHONIE pourra dialoguer, délivrer une instruction ou une clairance, demander un rappel et analyser le collationnement.

## AFIS / service d'information

Type SimConnect initialement associé : `FSS`.

L'agent donne des informations et paramètres, mais ne délivre pas les mêmes autorisations qu'un contrôleur. La qualification définitive dépendra du profil officiel de l'aérodrome lorsque la classification MSFS est ambiguë.

## ATIS / météo automatique

Types SimConnect : `ATIS`, `AWS`.

PHONIE diffuse une information cyclique. Le pilote ne dialogue pas avec cette fréquence.

## Auto-information

Types SimConnect : `CTAF`, `UNI`.

PHONIE ne répond jamais au pilote. De futurs trafics IA pourront occuper la fréquence uniquement avec des messages cohérents avec leur position, leur phase de vol et la procédure locale.

## Fréquence inconnue

PHONIE reste silencieux. Aucune fréquence ne sera transformée arbitrairement en TOUR, AFIS ou auto-information.
