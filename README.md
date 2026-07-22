# PHONIE DEV0.4.1.9 - RADIO CONTEXT, CONTACT HISTORY & FLIGHT RESET

Cette version ferme les blocages observés pendant le premier essai national de DEV0.4.1.6.

Principaux changements :

- départage d'un canal SIA Tour/A-A par le type Facilities réellement actif ;
- identification de l'organisme par la fréquence, sans exiger que son nom soit parfaitement reconnu ;
- silence lorsqu'une autre station est clairement appelée ;
- historique des contacts, salutations et indicatifs abrégés séparé par organisme ;
- conservation du contact après une écoute ATIS ou un changement normal d'aérodrome ;
- remise à zéro complète lors d'un nouveau vol, Restart Flight, retour menu, reconnexion, changement d'appareil ou d'indicatif ;
- bouton manuel `Nouvelle session` dans le diagnostic ;
- refus des instructions sol sur un SIV/FIS régional ;
- phraséologie sol et départ revue à partir du manuel DSNA/ENAC et de la fiche de l'Aéroclub du Poitou fournis au projet.

Commencer par `TEST-DEV0.4.1.9.md`.

Après extraction à la racine du dépôt en conservant `.git`, exécuter `RENORMALIZE-GIT-INDEX-DEV0.4.1.9.cmd`. Le script vérifie les attributs Git, les fins de ligne et les SHA-256 radio avant le commit.
