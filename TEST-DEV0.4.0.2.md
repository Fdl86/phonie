# Protocole PHONIE DEV0.4.0.2 - GROUND OPERATIONS RC

## 1. GitHub Actions

Après copie du ZIP source dans le dépôt en conservant uniquement `.git` :

1. commit conseillé : `PHONIE DEV0.4.0.2 - Ground Operations RC` ;
2. pousser avec GitHub Desktop ;
3. ouvrir l'onglet Actions ;
4. vérifier que `Build solution`, `DEV0.3 regression smoke tests` et `Ground Operations core tests` sont verts ;
5. télécharger `PHONIE-DEV0.4.0.2-win-x64.zip`.

Ne pas poursuivre si le workflow est rouge.

## 2. Installation portable

1. extraire l'artefact dans un dossier neuf ;
2. lancer `PHONIE.exe` ;
3. vérifier le titre `DEV0.4.0.2 - GROUND OPERATIONS RC` ;
4. vérifier la présence de `config`, `logs`, `recordings`, `cache` et `models` dans ce même dossier.

## 3. Préparation MSFS

Test principal conseillé sous MSFS 2024, puis comparaison sous MSFS 2020 :

1. charger LFBI avec la scène habituelle ;
2. utiliser F-HNNY ;
3. placer l'avion sur un parking relié au réseau taxi ;
4. régler une météo avec vent établi afin que la piste en service soit non ambiguë ;
5. régler COM1 sur la fréquence TOUR reconnue par le simulateur ;
6. attendre la fin de la lecture Airport Data et l'état d'occupation connu.

Dans l'interface, la ligne SOL doit afficher un graphe, une position, une piste et une occupation différente de `INCONNUE`.

## 4. Premier contact et indicatif

Dire une première fois :

`Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, au parking, bonjour.`

Attendu :

- réponse avec l'indicatif complet ;
- aucune invention d'indicatif ;
- état de contact établi.

Puis dire :

`Fox Novembre Yankee, prêt au roulage.`

Attendu :

- réponse commençant par `Fox Novembre Yankee` ;
- mémoire interne toujours liée à F-HNNY.

## 5. Clairance de roulage prioritaire

Au parking, dire :

`Fox Novembre Yankee, prêt au roulage.`

Vérifier :

- piste en service cohérente avec le vent ;
- point d'attente existant, par exemple D, D1, D2 ou D3 selon les données et la route ;
- aucun libellé générique de type `HS-17` ;
- taxiways annoncés présents dans les données LFBI ;
- route visible dans la ligne SOL ;
- décision `TAXI_CLEARANCE` dans `logs\ground-operations` ;
- génération d'un WAV complet dans `cache\controller-voice` ;
- aucune concaténation audible de fragments.

Noter le parking de départ, la piste, le point d'attente et les taxiways annoncés.

## 6. Clairance déjà donnée

Sans déplacer l'avion, redire :

`Fox Novembre Yankee, prêt au roulage.`

Attendu :

- aucun nouveau point d'attente arbitraire ;
- aucune nouvelle route calculée ;
- rappel de poursuivre vers le point déjà attribué ;
- code `TAXI_CLEARANCE_ALREADY_ISSUED`.

## 7. Roulage réel et point d'attente

1. suivre la route annoncée ;
2. vérifier le passage de l'état vers `Taxiing` lorsque l'avion se déplace ;
3. atteindre le point attribué ;
4. vérifier le passage vers `AtHoldShort`.

Au point, dire :

`Fox Novembre Yankee, prêt au départ.`

Attendu : reconnaissance distincte du point d'attente, sans nouvelle clairance de roulage.

## 8. Alignement et décollage

Au point d'attente, dire :

`Fox Novembre Yankee, demande alignement et décollage.`

Attendu :

- intention combinée reconnue ;
- autorisation d'alignement uniquement ;
- aucune autorisation immédiate de décollage avant confirmation de la position sur piste.

Après alignement réel, dire :

`Fox Novembre Yankee, demande décollage.`

Attendu : autorisation uniquement si PHONIE localise l'avion sur la piste et si l'état précédent autorise cette transition.

## 9. Refus incompatibles

Depuis le parking, avant toute clairance, tester :

`Demande décollage.`

Attendu : refus explicite, aucune autorisation.

Depuis une position non raccordée au réseau, tester une demande de roulage. Attendu : absence de clairance inventée et cause visible.

## 10. Règles radio

Tester successivement :

- ATIS : bulletin uniquement, aucune réponse au PTT ;
- bouton de lecture ATIS : génération puis lecture du bulletin complet ;
- 124.000 LFBI : silence au PTT ;
- auto-information / CTAF / UNICOM : silence ;
- 134.100 LFBI : dialogue d'information/approche lorsque la fréquence est identifiée par le simulateur ;
- fréquence inconnue : silence et aucune clairance.

## 11. Occupation

Lorsque du trafic IA est disponible :

1. placer ou attendre un avion près d'un point d'attente ;
2. demander le roulage ;
3. vérifier que le point ou le segment occupé n'est pas choisi.

Si le trafic SimConnect est indisponible, PHONIE doit afficher `occupation INCONNUE` et refuser la clairance plutôt que déclarer arbitrairement la voie libre.

## 12. Non-régression DEV0.3

Vérifier rapidement :

- PTT clavier global ;
- PTT HOTAS si configuré ;
- enregistrement WAV ;
- Whisper Small Vulkan ;
- Whisper Small CPU ;
- Turbo uniquement si sélectionné manuellement ;
- benchmark GPU ;
- modèles et paramètres toujours dans le dossier PHONIE.

## 13. Fichiers à transmettre

Après la session, fermer PHONIE puis envoyer :

- le dernier fichier `logs\PHONIE-DEV0.4.0.2-....log` ;
- `logs\ground-operations\ground-decisions-....jsonl` ;
- le dernier `logs\airport-data\airport-....json` ;
- une courte note avec parking de départ, vent, piste choisie et comportement observé.
