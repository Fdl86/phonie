# PROTOCOLE DE TEST - PHONIE DEV0.4.0.6

## 1. Build

GitHub Actions doit terminer en vert : précontrôle, restauration, compilation des quatre projets, Smoke Tests, Core Tests et publication Windows x64.

L'artefact `PHONIE-DEV0.4.0.6-win-x64` doit donner directement `PHONIE.exe` après une seule extraction.

## 2. Version et connexion

Vérifier `DEV0.4.0.6 - HOLD SHORT FLOW`, la connexion MSFS, l'indicatif, la fréquence et la piste sélectionnée.

## 3. Roulage Tour

Depuis S6 à LFBI :

1. premier contact ;
2. PTT de collationnement ;
3. annoncer `prêt au roulage`.

Réponse attendue :

`Fox Novembre Yankee, roulez au point d'attente et rappelez prêt.`

Ne doivent jamais être prononcés : Alpha, Alpha 2, Alpha 3, Delta, D1, via ou intersection.

## 4. Carte et diagnostic

La carte doit malgré tout montrer le chemin interne. À LFBI, le diagnostic peut conserver A3, A2 et A, avec A3 intermédiaire et A2 comme point d'attente de départ. La phrase radio doit rester générique.

## 5. Prêt au point d'attente

Arriver au point d'attente de départ et annoncer :

`Fox Novembre Yankee, prêt au point d'attente.`

Piste libre, réponse attendue :

`Fox Novembre Yankee, alignez-vous piste zéro trois, vent ..., autorisé décollage.`

La piste dépend des conditions réelles. Après la réponse, effectuer un PTT de collationnement.

## 6. Protection de position

Depuis le parking ou un point intermédiaire, annoncer `prêt au point d'attente`.

Résultat attendu : aucune autorisation de décollage. PHONIE demande de maintenir et de rappeler prêt au point d'attente de départ.

Une appellation mal reconnue dans la phrase ne doit plus changer la décision : la position SimConnect reste prioritaire.

## 7. Trafic

Avec un trafic détecté sur la piste : `maintenez point d'attente, trafic sur la piste`.

Si le service trafic devient indisponible : `maintenez point d'attente, trafic non déterminé`.

Aucune autorisation ne doit être donnée dans ces deux cas.

## 8. AFIS

Sur le terrain AFIS choisi :

- demande de roulage : piste, vent/QNH et rappel au point d'attente ;
- au point d'attente : piste, paramètres et trafic disponible ;
- aucune occurrence de `roulez`, `alignez-vous` ou `autorisé décollage` ;
- collationnement PTT toujours attendu, avec relance en cas de silence.

## 9. Redémarrage vocal

Tester `Redémarrer ASR`, puis un PTT. Tester `Redémarrer voix`, puis provoquer une réponse. La connexion MSFS et l'état Ground Operations doivent être conservés.

## 10. À fournir en cas d'anomalie

Dossier `logs`, capture de la carte, ICAO, parking, fréquence, phrase prononcée, transcription et réponse exacte de PHONIE.
