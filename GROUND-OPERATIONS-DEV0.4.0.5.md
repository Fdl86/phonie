# MOTEUR GROUND OPERATIONS - DEV0.4.0.5

Le moteur sépare données Facilities, modèle géométrique, profil opérationnel, occupation, routage, état de session, décision radio et rendu graphique.

LFBI n'est pas codé dans le moteur. Son profil est un fichier indépendant.

## Résolution d'un point

Un point officiel est associé par ICAO, coordonnées, rôle, piste compatible et rayon de correspondance. Les index Facilities et noms de scène ne sont pas des clés permanentes.

## Sélection de l'attente

Le routeur choisit la piste, localise le départ, écarte les points intermédiaires, privilégie les points de départ officiels, applique l'occupation, calcule le chemin minimal, conserve le détail pour le diagnostic et prononce seulement les éléments utiles.

## LFBI

Le profil définit A3 intermédiaire, A2 attente de départ et A entrée de piste. Les `via` inutiles sont supprimés.

## Service radio

Le moteur reçoit une capacité : `Controlled`, `InformationOnly`, `AutomaticBroadcast`, `RecordedMessage`, `SelfInformation` ou `Unknown`. La décision verbale dépend de l'autorité du service.

## Collationnement

`RequiresAcknowledgement` est attaché aux messages Tour/AFIS. Le délai est armé après la fin de la synthèse. Un PTT clôt l'attente sans analyse sémantique bloquante.

## Diagnostic graphique

La carte WPF est construite localement à partir du graphe Facilities, sans fond Internet, OCR ni capture de caméra.
