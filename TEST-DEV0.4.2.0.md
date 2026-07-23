# Validation PHONIE DEV0.4.2.0

## CI obligatoire

Le workflow doit réussir dans cet ordre : précontrôle SHA/Git, restore, compilation à zéro avertissement, Smoke Tests, Core Tests, prépublication Windows x64, tests/génération SIA, validation de l'absence de fréquences de production codées en dur, puis publication de l'artefact.

## Régressions automatisées prioritaires

- « prêt en Alpha 2 pour un départ depuis l'intersection » ne devient pas une station appelée.
- « Prôliette Tour » est rapproché de la Tour active sans faux silence.
- « Nantes Tour » sur une fréquence Poitiers produit une correction et la fréquence officielle seulement si elle est unique et exploitable.
- Une station connue sans fréquence exploitable produit « vérifiez la fréquence ».
- Une demande intersection reste reconnue lorsque « prêt » est transcrit « prend », « après » ou « Ypres » dans un contexte compatible.
- Les 26 transmissions réelles du corpus sont rejouées ; au moins 22 doivent produire l'intention attendue et une seule doit désigner une autre station.
- `retour avec vous`, `de retour avec vous`, `rebonjour`, `bonjour` et `bonsoir` atteignent le moteur de contact.
- Les services ATIS, enregistrés et A/A restent silencieux.
- Restart Flight, retour menu et nouvelle session effacent l'historique opérationnel.

## Essai MSFS prioritaire

1. Établir le contact Tour avec l'indicatif complet.
2. Après contact, transmettre uniquement « Fox Novembre Yankee, prêt au roulage » : aucune répétition du nom de station ne doit être nécessaire.
3. Au point d'attente, dire « Fox Novembre Yankee, Alpha 2, prêt pour un départ depuis l'intersection ».
4. Rejouer la demande sans « prêt » : le moteur ne doit l'accepter que si l'avion est réellement au point d'attente.
5. Appeler volontairement une autre Tour connue : PHONIE doit indiquer « ici [station active] » et la fréquence cible uniquement si elle est certaine.
6. Dire « pour un départ » sans appeler de station : aucun `CALLED_STATION_*` ne doit apparaître.
7. Tester un PTT vide, très court et un bruit seul : aucune intention opérationnelle ne doit être créée.
8. Contrôler les journaux : brut, nettoyé, prompt, probabilité, niveau audio, station, intention et corrections doivent être présents.

## Limitation connue

Le collationnement est encore accepté sur présence d'un PTT sans validation sémantique complète. Cette limitation est volontairement conservée jusqu'à l'amélioration de l'ASR et du moteur de collationnement.
