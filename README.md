# PHONIE DEV0.4.0.5 - GROUND OPERATIONS FINAL RC

PHONIE est une application Windows x64 portable en C# / .NET 8 / WPF destinée aux communications ATC VFR locales avec Microsoft Flight Simulator 2020 et 2024.

DEV0.4.0.5 consolide le chantier `GROUND OPERATIONS` avant le passage à DEV0.5. Elle conserve la base stable DEV0.3.0.5 et le routage validé de DEV0.4.0.4.

## Fonctions principales

Lorsqu'un pilote demande le roulage, PHONIE récupère le réseau par SimConnect Facilities, localise l'avion, détermine la piste en service, applique un profil opérationnel lorsqu'il existe, distingue point intermédiaire, point d'attente de départ et entrée de piste, écarte les occupations réelles, calcule l'itinéraire et produit une phraséologie courte.

Le trajet détaillé reste visible dans le diagnostic même lorsqu'il n'est pas récité à la radio.

## Profil opérationnel LFBI

`data\airports\LFBI.json` associe les données officielles au graphe par position et fonction, sans dépendre des index internes de la scène :

- A3 : point intermédiaire ;
- A2 : point d'attente de départ ;
- A : entrée de piste pour le départ depuis l'intersection.

Depuis le parking S6, la destination radio est A2. Les noms internes Facilities `D`, `D1`, `D2` ou `D3` restent visibles dans le diagnostic mais ne remplacent pas l'appellation opérationnelle vérifiée.

Sur un aérodrome sans profil, PHONIE conserve le moteur géométrique. Il utilise un nom Facilities plausible avec son niveau de confiance ou une formulation générique lorsque le nom n'est pas fiable.

## Carte du roulage

La section `Diagnostic > Carte du roulage` affiche le réseau Facilities, la piste, l'itinéraire attribué, le point d'attente sélectionné, les autres candidats, l'entrée de piste, l'avion utilisateur, le trafic analysé et les occupations.

## Séquence départ

La machine d'état distingue parking, roulage, point intermédiaire, point d'attente de départ, prêt au départ, départ intersection, alignement, remontée de piste, autorisation de décollage et avion en vol. La position réelle doit confirmer le point annoncé.

## ATC, AFIS et services sans dialogue

- ATC contrôlé : instructions et autorisations opérationnelles ;
- AFIS / FSS : renseignements uniquement, aucune autorisation de contrôle ;
- ATIS / AWS : diffusion automatique, aucun dialogue ;
- auto-information / CTAF / UNICOM : silence ;
- fréquence inconnue : silence.

## Collationnement simplifié

Après un message opérationnel de la Tour ou de l'AFIS, PHONIE attend une émission PTT du pilote. Le contenu est journalisé mais n'est pas bloquant. Sans PTT, PHONIE relance avec `collationnez`, puis `accusez réception`. Le PTT de collationnement n'est pas réinterprété comme une nouvelle demande.

## Redémarrage vocal

- `Redémarrer ASR` annule les workers, libère les modèles et réinitialise le profil courant ;
- `Redémarrer voix` arrête la lecture et recrée le moteur de synthèse.

Le changement de runtime Whisper CPU vers Vulkan, ou Vulkan vers CPU, demande toujours une relance complète de PHONIE.

## Indicatif

Pour `F-HNNY` : premier contact `Fox Hôtel Novembre Novembre Yankee`, puis `Fox Novembre Yankee`, avec conservation interne de `F-HNNY` et de la forme abrégée `F-NY`.

## Stockage portable

Toutes les données restent sous le dossier PHONIE : `config`, `data`, `logs`, `recordings`, `cache` et `models`.

## Livraison

Le workflow compile, teste et publie le dossier Windows x64. GitHub crée lui-même l'artefact : il n'existe plus de ZIP à l'intérieur du ZIP. Une seule décompression donne directement accès à `PHONIE.exe`.

Commencer les essais avec `TEST-DEV0.4.0.5.md`.
