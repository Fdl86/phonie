# Protocole DEV0.4.1.1

## Prérequis

Ne télécharger l'artefact que si le workflow GitHub est entièrement vert : génération SIA, validation SIA, compilation sans avertissement, Smoke Tests, Core Tests et publication.

Vider seulement le dossier `logs`, lancer MSFS 2024 sur un parking, puis lancer PHONIE.

Vérifier dans l'en-tête :

```text
DEV0.4.1.1 - SIA RADIO & CONTROLLER VOICES
```

## 0. Workflow GitHub

Le workflow doit franchir dans cet ordre : tests Python, génération SIA, validation SIA, restauration .NET, compilation sans avertissement, Smoke Tests, Core Tests, publication.

La génération doit annoncer au moins 100 documents VAC détectés, puis plus de 200 aérodromes dans la base finale. Elle ne doit plus afficher `Catalogue VAC incomplet: seulement 0 documents`.

## 1. Base SIA

La ligne SIA doit afficher un cycle, un nombre d'aérodromes supérieur à 200 et un nombre de fréquences non nul. Elle ne doit pas afficher `amorçage` ou `indisponible`.

Cliquer une fois sur `MAJ SIA`. Le résultat attendu est `déjà à jour` ou une mise à jour réussie. Une erreur réseau ne doit pas supprimer la base locale valide.

## 2. Détection géographique

Démarrer sur un aérodrome français puis changer de terrain sans redémarrer PHONIE.

Attendu :

- le contexte `SOL` bascule automatiquement ;
- pistes, parkings, taxiways et points d'attente sont rechargés ;
- la base radio affiche les moyens officiels du nouvel ICAO ;
- aucune fréquence de l'ancien terrain ne reste comme recommandation locale.

## 3. Fréquence locale A/A

Sur un aérodrome publié en A/A, régler le canal indiqué sur la VAC officielle.

Attendu :

- station locale reconnue depuis la base SIA ;
- source affichée avec cycle SIA ;
- `AUTO-INFORMATION` ;
- aucune réponse et aucune relance après PTT ;
- une fréquence régionale proposée par MSFS ne remplace pas la fréquence locale officielle.

## 4. Station contrôlée ou AFIS

Sur un terrain possédant un service dialogué publié, régler le canal officiel puis faire un premier contact.

Attendu :

- type de service correct ;
- Tour prioritaire au sol lorsqu'elle est publiée et exploitable ;
- Approche ou FIS distinct d'une station locale ;
- AFIS donne des renseignements sans clairance de contrôle ;
- aucun dialogue si les horaires publiés sont complexes et non évalués automatiquement.

## 5. Voix

Observer la ligne `Voix` sur deux stations dialoguées différentes.

Attendu :

- voix française identifiée lorsque Windows en possède ;
- homme ou femme possible ;
- voix stable pendant plusieurs échanges avec la même station ;
- station différente pouvant recevoir une autre voix ;
- aucune voix attribuée à A/A ou ATIS interactif.

## 6. Mise à jour et secours

Couper Internet, relancer PHONIE et vérifier que la base SIA locale continue de fonctionner.

Une base distante invalide, incomplète ou avec un mauvais SHA-256 doit être rejetée sans modifier `current`.

## Arrêt au premier échec

Transmettre :

- ZIP complet du dossier `logs` ;
- capture de l'écran principal ;
- capture Diagnostic ;
- aérodrome et fréquence COM1 ;
- texte exact de la ligne Base SIA ;
- texte exact de `SOL`, `RADIO`, `Source` et `Voix`.
