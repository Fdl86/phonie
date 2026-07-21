# PHONIE DEV0.4.0.7 - DYNAMIC AIRPORT & RADIO CONTEXT

PHONIE est une application Windows x64 portable en C# / .NET 8 / WPF destinée aux communications ATC VFR locales avec Microsoft Flight Simulator 2020 et 2024.

DEV0.4.0.7 corrige les deux limites majeures constatées en DEV0.4.0.6 : le contexte ne reste plus attaché à LFBI après un spawn ou une téléportation, et la séquence de départ n'exige plus un point local particulier comme A2.

## Détection dynamique de l'aérodrome

PHONIE recherche automatiquement l'aérodrome correspondant à la position réelle de l'avion. Lors d'un spawn, d'une téléportation ou d'un déplacement vers un nouveau terrain, il invalide l'ancien contexte et recharge :

- les pistes et seuils ;
- les parkings, taxiways et points d'attente ;
- les fréquences et leurs types de service ;
- la station radio active ;
- le trafic au sol ;
- la piste probable en service ;
- la carte Diagnostic ;
- l'éventuel profil local disponible.

Aucun aérodrome ne doit être préparé ou codé à l'avance pour être détecté. Une liste Facilities proche est interrogée régulièrement. Une liste mondiale est utilisée comme secours lorsque l'interface Facilities étendue n'est pas disponible.

## Contextes géographique et radio séparés

Le contexte géographique décrit le terrain autour de l'avion. Le contexte radio décrit la station accordée sur COM1.

En vol, PHONIE peut donc conserver le terrain situé sous l'avion comme contexte géographique tout en basculant le contexte radio vers l'aérodrome d'arrivée ou sa station d'approche. La résolution radio utilise en priorité l'identifiant et la position de la station COM active, puis la fréquence dans les Facilities déjà chargées.

## Roulage et départ génériques

Après une demande de roulage sur une fréquence contrôlée :

`Fox Novembre Yankee, roulez au point d'attente et rappelez prêt.`

Les noms locaux A, A2, A3, D1 et les segments internes ne sont pas nécessaires dans la phrase radio. Ils restent disponibles dans le diagnostic lorsqu'ils sont fiables.

Tout véritable nœud `HOLD_SHORT` Facilities est valable pour l'annonce prêt au départ. Le profil local ne peut ni autoriser ni interdire le départ. Les associations de piste fiables sont préférées pour le routage ; si la scène les renseigne mal, PHONIE se replie sur les autres vrais points d'attente accessibles.

Piste libre et trafic connu :

`Fox Novembre Yankee, alignez-vous piste deux un, vent deux un zéro degrés, un zéro nœuds, autorisé décollage.`

Piste occupée ou trafic indisponible : maintien au point d'attente.

## AFIS et collationnement

Sur une fréquence AFIS, PHONIE transmet uniquement les informations disponibles. Il ne donne jamais d'autorisation de roulage, d'alignement ou de décollage.

Après chaque message opérationnel Tour ou AFIS, une émission PTT pilote est attendue. Le contenu exact n'est pas bloquant dans cette version. En l'absence de PTT, PHONIE relance.

## Portabilité

Toutes les données restent sous le dossier PHONIE : `config`, `data`, `logs`, `recordings`, `cache` et `models`.

Le workflow compile, teste et publie un dossier Windows x64 directement dans l'artefact GitHub. Une seule extraction donne accès à `PHONIE.exe`.

Commencer les essais avec `TEST-DEV0.4.0.7.md`.
