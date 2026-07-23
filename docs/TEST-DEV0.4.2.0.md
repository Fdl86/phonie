# Validation PHONIE DEV0.4.2.0 R3

## CI obligatoire

Le workflow doit réussir dans cet ordre :

1. précontrôle de l'arbre source, des attributs Git et des SHA-256 ;
2. restauration de `PHONIE.sln` ;
3. compilation complète de l'application, du Core et des deux projets de tests avec zéro avertissement ;
4. Smoke Tests ;
5. Core Tests et rejeu du corpus réel ;
6. prépublication autonome Windows x64 avec présence de `PHONIE.exe` ;
7. tests, génération et validation SIA ;
8. publication de l'artefact portable.

Un artefact provenant d'un workflow incomplet ou rouge ne doit pas être utilisé.

## Régressions automatisées prioritaires

- `Phonie.SmokeTests` compile en référençant `Phonie.Core` sans recopier ses sources.
- « prêt en Alpha 2 pour un départ depuis l'intersection » ne devient pas une station appelée.
- « Prôliette Tour » peut être rapproché de la Tour active sans faux silence.
- « Nantes Tour » sur une fréquence Poitiers produit une correction pédagogique.
- Une station connue sans fréquence exploitable produit « vérifiez la fréquence ».
- Le nom de station est facultatif après contact.
- Les 26 transmissions réelles du corpus sont rejouées.
- `retour avec vous`, `de retour avec vous`, `rebonjour`, `bonjour` et `bonsoir` atteignent le moteur de contact.
- ATIS, messages enregistrés et A/A restent non dialogués.
- Restart Flight, retour menu et nouvelle session effacent l'historique opérationnel.

## Essai MSFS prioritaire

1. Établir le contact Tour avec l'indicatif complet.
2. Après contact, transmettre « Fox Novembre Yankee, prêt au roulage » sans répéter la station.
3. Au point d'attente, demander un départ depuis l'intersection.
4. Dire volontairement « Nantes Tour » sur Poitiers et contrôler la correction fournie.
5. Dire « pour un départ » sans appeler de station et vérifier l'absence de faux `CALLED_STATION_*`.
6. Tester un PTT vide, très court et un bruit seul.
7. Vérifier les journaux brut/nettoyé, station, intention, corrections et scores.

## Limitation connue

Le collationnement reste accepté sur présence d'un PTT sans validation sémantique complète. Cette limitation est conservée jusqu'au chantier dédié au moteur de collationnement.
