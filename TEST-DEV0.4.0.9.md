# Test DEV0.4.0.9 - protocole prioritaire

## 1. GitHub Actions

Le workflow doit être entièrement vert : compilation sans erreur, Smoke Tests, Core Tests et publication Windows x64.

## 2. Spawn direct à LFOU

- démarrer MSFS à Cholet sans ouvrir d'abord LFBI ;
- attendre quelques secondes ;
- vérifier `SOL LFOU` dans PHONIE ;
- vérifier que les Facilities LFOU sont demandées et que le contexte précédent est absent ;
- sur 120.405 pendant les horaires AFIS publiés : `CHOLET INFORMATION`, dialogue AFIS ;
- hors horaires AFIS : `CHOLET A/A`, PHONIE silencieux.

## 3. Téléportation LFOU vers LFBI

Sans redémarrer PHONIE :

- téléporter l'avion à LFBI ;
- vérifier la bascule automatique `SOL LFBI` ;
- vérifier le rechargement des pistes, fréquences, météo et réseau sol ;
- vérifier la recommandation `POITIERS TOUR 118.505` ;
- 134.100 doit être reconnu comme Poitiers Approche/SIV ;
- 121.780 et 124.000 doivent rester silencieuses.

## 4. Roulage et départ

- demander le roulage : réponse générique vers le point d'attente ;
- annoncer prêt depuis n'importe quel véritable `HOLD_SHORT` relié à la piste ;
- piste libre : alignement et autorisation de décollage ;
- trafic ou état trafic inconnu : maintien.

## 5. À transmettre en cas d'échec

ZIP complet du dossier `logs`, capture Diagnostic, fréquence COM1 active, ICAO, position et heure du test.
