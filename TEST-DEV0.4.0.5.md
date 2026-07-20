# PROTOCOLE DE TEST - PHONIE DEV0.4.0.5

## 1. GitHub Actions

Le workflow doit être entièrement vert : précontrôle, restauration, compilation, tests DEV0.3, tests Ground Operations et publication Windows x64. Après téléchargement, une seule décompression doit afficher directement `PHONIE.exe`.

## 2. Test LFBI principal

- MSFS à LFBI ;
- avion au parking S6 ;
- fréquence Tour ;
- PHONIE DEV0.4.0.5.

Échanges :

1. `Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, bonjour.`
2. Après la réponse, faire un bref PTT de collationnement.
3. `Fox Novembre Yankee, prêt au roulage.`

Résultat attendu : destination `Alpha 2`, aucune mention de `Delta 1`, aucune longue liste de taxiways, route jaune sur la carte, A3 intermédiaire, A2 sélectionné, A entrée de piste, aucun faux obstacle.

Rouler jusqu'à A2 puis annoncer : `Fox Novembre Yankee, prêt en Alpha 2.`

Résultat attendu : décision cohérente depuis A2, départ intersection A ou remontée de piste selon la décision, aucune autorisation depuis le parking, nouveau PTT de collationnement attendu.

## 3. Relance de collationnement

Après un message Tour ou AFIS, ne pas appuyer sur le PTT. Attendre `collationnez`, puis `accusez réception`. Faire ensuite un PTT court. L'attente doit disparaître et le contenu ne doit pas créer une nouvelle demande.

## 4. Test AFIS

Noter ICAO, parking et fréquence. Demander les informations de roulage puis s'annoncer prêt au point d'attente.

Résultat attendu : piste, paramètres et informations utiles ; aucun `roulez` impératif ; aucune autorisation d'alignement ou de décollage ; PTT de retour attendu ; diagnostic `InformationOnly`.

## 5. Redémarrage vocal

Tester `Redémarrer ASR`, effectuer un PTT, puis `Redémarrer voix` et provoquer une réponse. L'application doit rester active. Un changement CPU/Vulkan doit demander explicitement une relance complète.

## 6. En cas de défaut

Transmettre une capture de la carte, le texte `DIAGNOSTIC ROULAGE`, `logs\ground-operations`, l'ICAO, le parking, la fréquence, la phrase et la réponse.
