# PHONIE DEV0.4.1.9 - Radio Context, Contact History & Phraseology

## Fermeture CI DEV0.4.1.9

- Ajout de `bonsoir` au parseur de premier contact ; la réponse contrôleur conserve désormais la salutation du pilote en soirée.
- Protection Git `-text` étendue aux profils `data/airports/**/*.json`.

- Correction du parseur : « de retour avec vous », « rebonjour » et « re bonjour » sont désormais classés comme reprise de contact, même sans le mot « bonjour » isolé.
- Correction du test de phraséologie départ : la sortie correcte conserve l'ordre « piste deux un, alignez-vous et autorisé décollage », conforme à la fiche Aéroclub du Poitou et au principe d'identification explicite de la piste.
- Ajout d'une régression directe sur la reconnaissance des reprises de contact.


## Correctifs opérationnels

- Le type Facilities actif départage désormais les modes SIA partageant le même canal. Une Tour active dans MSFS ne devient plus une A/A silencieuse uniquement parce que les horaires SIA ne sont pas évalués.
- La fréquence active détermine l'organisme. Le nom prononcé par le pilote est facultatif.
- PHONIE reste silencieux lorsqu'un autre service ou un autre aérodrome est clairement appelé.
- Suppression des noms d'aérodrome codés en dur dans l'analyse et les réponses de production.
- Un SIV/FIS régional ne peut plus délivrer d'instruction de roulage, d'alignement ou de décollage.

## Historique des contacts

- Mémoire séparée par organisme radio : aérodrome, station et rôle de service.
- Premier échange avec indicatif complet, puis indicatif abrégé autorisé uniquement pour cet organisme.
- Aucun nouveau « bonjour » sur un organisme déjà contacté.
- Nouveau premier contact autorisé lors d'un passage Sol vers Tour.
- « Rebonjour » et « de retour » reconnus après une écoute ATIS ou un retour sur fréquence.
- Une première transmission contenant déjà une demande de roulage est traitée immédiatement.

## Cycle de vie du vol

- Réinitialisation complète sur SimStop, SimStart, FlightLoaded, perte de connexion, changement d'appareil ou d'indicatif.
- Le changement normal d'aérodrome ne détruit plus l'historique radio du vol.
- Bouton « Nouvelle session » dans le diagnostic pour forcer la remise à zéro sans redémarrer PHONIE.
- La remise à zéro efface contacts, indicatifs abrégés, ATIS, roulage, piste, attentes et collationnements, mais conserve réglages, modèles, voix et base SIA.

## Phraséologie

- Formulations sol/départ alignées sur le manuel DSNA/ENAC et la fiche Aéroclub du Poitou fournis pour le projet.
- Piste toujours explicitement identifiée dans les autorisations.
- « Rappelez prêt » utilisé pour la séquence de départ.
- Les services d'information restent informatifs et ne produisent aucune clairance de contrôle.
