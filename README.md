# PHONIE DEV0.4.0.6 - HOLD SHORT FLOW

PHONIE est une application Windows x64 portable en C# / .NET 8 / WPF destinée aux communications ATC VFR locales avec Microsoft Flight Simulator 2020 et 2024.

DEV0.4.0.6 simplifie la fin du chantier Ground Operations avant DEV0.5. Le moteur continue de calculer le trajet exact avec les données Facilities, mais les noms locaux de points d'attente ne sont plus nécessaires dans la phraséologie pilote-contrôleur.

## Roulage contrôlé

Après une demande de roulage, PHONIE détermine la piste, calcule un itinéraire accessible et répond :

`Fox Novembre Yankee, roulez au point d'attente et rappelez prêt.`

Les appellations A, A2, A3, D1 et les segments internes restent visibles dans le diagnostic et sur la carte, mais ne sont plus prononcés. Une mauvaise appellation de scène ou une transcription approximative ne pilote donc plus la clairance.

## Prêt au point d'attente

Au prochain appel, PHONIE vérifie la position réelle. La clairance combinée n'est donnée que lorsque :

- l'avion est sur un vrai point d'attente de départ lié à la piste attribuée ;
- un point intermédiaire n'est pas confondu avec le point de départ ;
- le trafic disponible est connu ;
- aucun segment de la piste attribuée n'est occupé.

Réponse Tour attendue lorsque la piste est libre :

`Fox Novembre Yankee, alignez-vous piste deux un, vent deux un zéro degrés, un zéro nœuds, autorisé décollage.`

Si la piste est occupée ou si l'état du trafic est indisponible, PHONIE maintient l'avion au point d'attente.

## AFIS

Le moteur géométrique et le diagnostic restent disponibles, mais l'AFIS transmet uniquement la piste, le vent, le QNH et les renseignements de trafic disponibles. Il ne dit jamais `roulez`, `alignez-vous` ou `autorisé décollage`.

## Diagnostic graphique

`Diagnostic > Carte du roulage` conserve :

- le réseau Facilities ;
- la piste et les points d'attente ;
- l'itinéraire interne attribué ;
- les noms Facilities et opérationnels ;
- les segments occupés ;
- l'avion et les trafics analysés.

Le profil LFBI reste présent pour comprendre et vérifier A3, A2 et A, mais ces noms ne conditionnent plus la phrase radio.

## Collationnement

Après chaque message opérationnel Tour ou AFIS, une émission PTT est attendue. Son contenu n'est pas bloquant dans cette version. En l'absence de PTT, PHONIE relance avec `collationnez`, puis `accusez réception`.

## Redémarrage vocal

Les commandes `Redémarrer ASR` et `Redémarrer voix` permettent de relancer séparément la reconnaissance et la synthèse sans fermer PHONIE. Le changement de runtime Whisper CPU/Vulkan demande encore une relance complète.

## Voix réalistes

La refonte complète des voix ATC est prévue dans DEV0.8 - ATC Voice. Les versions DEV0.4 à DEV0.7 stabilisent d'abord la logique opérationnelle, les circuits, le trafic et le moteur multi-aérodromes afin que les nouvelles voix reposent sur un dialogue fiable.

## Stockage et livraison

Toutes les données restent sous le dossier PHONIE : `config`, `data`, `logs`, `recordings`, `cache` et `models`.

Le workflow compile, teste et publie un artefact Windows x64 à décompression unique. Une seule extraction donne directement accès à `PHONIE.exe`.

Commencer les essais avec `TEST-DEV0.4.0.6.md`.
