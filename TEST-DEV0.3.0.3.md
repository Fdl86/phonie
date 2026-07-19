# Protocole rapide PHONIE DEV0.3.0.3

## 1. Build

GitHub Actions doit afficher successivement :

```text
Build
Run phraseology smoke tests
PHONIE phraseology smoke tests OK
Publish autonomous Windows x64
```

Tout échec avant la publication bloque la livraison.

## 2. Connexion

- lancer PHONIE puis MSFS 2024 à LFBI ;
- vérifier `DEV0.3.0.3` ;
- vérifier `ATC ID F-HNNY` ou l'identifiant réel de l'avion ;
- régler COM1 sur 118.505.

## 3. Laboratoire

Coller puis analyser :

```text
Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, au parking pour tours de piste.
```

Résultat attendu :

```text
Station : Poitiers Tour
Indicatif : F-HNNY
Position : parking
Intention : tours de piste
```

Deuxième phrase :

```text
Poitiers Tour, Fox Hotel novembre novembre yankee, au parking aviation générale, demande roulage.
```

Résultat attendu : `F-HNNY`, `parking aviation générale`, `roulage`.

## 4. Test anti faux indicatif

Coller :

```text
Poitiers Tour, bonjour.
```

PHONIE ne doit produire ni `T-OUR`, ni aucun autre indicatif.

## 5. Test vocal Vulkan

Prononcer :

```text
Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, au parking pour des tours de piste.
```

Le résultat doit conserver F-HNNY même si Whisper produit `novembre`, `yankees`, `onki` ou un mot parasite.

## 6. Comparaison

Cliquer sur `Comparer`, puis attendre la fin. Vérifier que :

- Whisper Vulkan et Vosk donnent chacun un résultat ;
- PHONIE reste utilisable ;
- la mémoire redescend après la comparaison ou après quelques secondes ;
- un nouvel appui PTT recharge normalement le profil choisi.

## 7. Règles radio

- 118.505 : réponse autorisée ;
- 121.780 : aucune réponse ;
- 124.000 : aucune réponse dialoguée ;
- fréquence inconnue : silence.

## 8. Fichiers utiles en cas d'anomalie

```text
logs\PHONIE-DEV0.3.0.3-*.log
logs\sessions\radio-*.jsonl
recordings\last-ptt.wav
```
