# PHONIE DEV0.4.0.4 - GROUND OPERATIONS RC

PHONIE est une application Windows x64 portable en C# / .NET 8 / WPF destinée aux communications ATC VFR locales avec Microsoft Flight Simulator 2020 et 2024.

DEV0.4.0.4 est la version candidate fonctionnelle du chantier `GROUND OPERATIONS`. Elle conserve les fonctions validées de DEV0.3.0.5 et ajoute un premier moteur ATC au sol fondé sur les données réelles SimConnect Facilities.

Cette révision corrige le faux blocage du réseau observé lorsque des objets SimConnect immobiles proches des parkings condamnaient aussi les nœuds du taxiway commun. L'occupation devient géométrique et ciblée, avec un diagnostic détaillé visible dans l'application.

## Fonction prioritaire

Lorsqu'un pilote demande le roulage sur une fréquence contrôlée, PHONIE tente de :

1. identifier l'aérodrome et le réseau de roulage ;
2. localiser l'avion sur un parking, un point ou un segment taxi ;
3. déterminer la piste en service selon le vent du simulateur ;
4. associer les points d'attente à la piste physique ;
5. interroger les avions SimConnect proches ;
6. exclure les points et segments occupés ;
7. calculer le chemin accessible le plus court ;
8. annoncer uniquement un point d'attente nommé et les taxiways réellement parcourus.

Si la station, l'indicatif, la position, la piste, l'occupation ou l'itinéraire ne sont pas suffisamment fiables, aucune clairance de roulage n'est inventée.

## Moteur métier

Le projet `src/Phonie.Core` contient un noyau indépendant de WPF et de SimConnect :

- normalisation des pistes, parkings, points et chemins taxi ;
- graphe non orienté du réseau de roulage ;
- association des points d'attente à une piste physique ;
- localisation de l'avion ;
- occupation dynamique du graphe ;
- routage déterministe ;
- reconnaissance des intentions sol ;
- machine d'état opérationnelle ;
- mémoire de l'indicatif et de la dernière instruction.

Les captures réelles LFBI fournies sous MSFS 2020 et MSFS 2024 sont intégrées comme fixtures de test du noyau.

## Indicatif

L'indicatif SimConnect reste la source de confiance. Pour `F-HNNY` :

- premier contact : `Fox Hôtel Novembre Novembre Yankee` ;
- après établissement du contact : `Fox Novembre Yankee` ;
- identité conservée en mémoire : `F-HNNY` ;
- forme abrégée autorisée : `F-NY`.

PHONIE ne fabrique jamais un indicatif à partir d'un mot reconnu dans la phrase du pilote.

## Radio

- organisme contrôlé : dialogue et décisions opérationnelles ;
- AFIS / FSS : information uniquement, aucune clairance de contrôle ;
- ATIS / AWS : bulletin automatique, aucun dialogue au PTT ;
- auto-information / CTAF / UNICOM : silence ;
- LFBI 124.000 : message enregistré, silence ;
- LFBI 134.100 : approche / SIV, dialogue autorisé lorsque le simulateur l'identifie.

## ATIS et voix

Les textes et les fichiers audio sont générés comme des énoncés complets. Il n'existe aucune banque de fragments WAV ni concaténation de mots.

L'ATIS utilise, lorsque disponibles : piste, vent, visibilité, plafond, température, point de rosée et QNH. Une nouvelle signature est créée uniquement lorsque les données opérationnelles changent. Le WAV complet est alors mis en cache dans `cache\atis`.

Les réponses du contrôleur sont synthétisées intégralement et mises en cache par station dans `cache\controller-voice`.

## ASR préservé

- Whisper Small CPU ;
- Whisper Small Vulkan ;
- Whisper Large-v3 Turbo Vulkan, optionnel ;
- Vosk FR expérimental ;
- fallback CPU visible ;
- préchauffage Turbo et benchmark GPU conservés.

Turbo n'est jamais sélectionné automatiquement par le moteur Ground Operations.

## Stockage portable

Toutes les données utilisateur sont écrites sous le dossier PHONIE :

- `config` ;
- `logs` ;
- `logs\ground-operations` ;
- `logs\airport-data` ;
- `recordings` ;
- `cache` ;
- `models`.

PHONIE n'écrit volontairement ni dans AppData ni dans le registre pour ses données utilisateur.

## Compilation

Le workflow GitHub Actions :

1. vérifie les fichiers source requis ;
2. restaure et compile explicitement les quatre projets en Release ;
3. exécute les tests de régression DEV0.3 ;
4. exécute les tests du noyau Ground Operations ;
5. publie l'application autonome Windows x64 uniquement après réussite ;
6. vérifie la structure portable ;
7. crée l'artefact `PHONIE-DEV0.4.0.4-win-x64.zip`.

Commencer les essais réels avec `TEST-DEV0.4.0.4.md`.
