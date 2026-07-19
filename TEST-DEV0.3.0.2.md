# Protocole PHONIE DEV0.3.0.2

## 1. Démarrage

- lancer PHONIE sans simulateur ;
- vérifier `DEV0.3.0.2` ;
- fermer et relancer ;
- vérifier l'absence de crash ;
- vérifier les dossiers `models\whisper` et `models\vosk`.

## 2. Connexion MSFS 2024

- charger l'avion à LFBI ;
- attendre SimConnect ;
- vérifier l'avion, la position, COM1 et la météo ;
- vérifier que la carte Simulateur affiche `ATC ID` ;
- noter exactement l'identifiant lu.

Critère bloquant : l'indicatif attendu par PHONIE doit venir du simulateur.

## 3. Profil Whisper Base CPU

- sélectionner `Whisper Base CPU - rapide` ;
- installer le profil ;
- enregistrer une phrase de 5 à 8 secondes ;
- transcrire ;
- relever temps, texte et indicatif détecté.

Phrase :

```text
Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking aviation générale pour tours de piste, avec information Alpha.
```

## 4. Profil Whisper Small CPU

- sélectionner `Whisper Small CPU - équilibré` ;
- installer si nécessaire ;
- reprendre exactement le même WAV avec `Transcrire` ;
- relever les mêmes mesures.

## 5. Comparaison CPU

Cliquer sur `Comparer`.

Résultat attendu :

- Base CPU exécuté si installé ;
- Small CPU exécuté si installé ;
- Vosk signalé absent ou exécuté s'il est installé ;
- un seul WAV utilisé ;
- temps séparé pour chaque profil.

## 6. Vosk FR expérimental

- sélectionner Vosk ;
- installer le modèle ;
- transcrire le même WAV ;
- vérifier qu'aucun basculement automatique vers Vosk ne se produit depuis Whisper ;
- comparer vitesse et extraction opérationnelle.

## 7. Indicatif fuzzy

Avec l'ATC ID du simulateur réglé sur `F-GABC`, prononcer séparément :

```text
Fox Golf Alfa Marvo Charlie
Fox Gold Fabre Beauchardie
Fox Gold va pas Bravo Charlie
FoxGolf Alfa Bravo Charlie
Fox Colf Alfa Bravo Charlie
```

Vérifier dans la ligne d'analyse :

- indicatif `F-GABC` ;
- source `rapproché de l'ATC ID SimConnect` ou équivalent ;
- confiance affichée ;
- réponse ATC produite seulement si le service radio autorise le dialogue.

## 8. Sécurité de la détection

Prononcer une phrase ne contenant aucun indicatif crédible.

PHONIE doit demander de répéter l'indicatif et ne doit pas attribuer automatiquement l'ATC ID.

## 9. Vulkan

- sélectionner `Whisper Small Vulkan - GPU` ;
- confirmer le message de redémarrage ;
- fermer puis relancer PHONIE ;
- vérifier que le profil Vulkan est actif ;
- transcrire le même WAV ;
- relever temps, CPU, mémoire et qualité ;
- vérifier la fluidité de MSFS.

Revenir ensuite à un profil CPU doit également demander un redémarrage.

## 10. Règles radio

- 118.505 : réponse TOUR autorisée ;
- 121.780 : transcription possible, aucune réponse ;
- 124.000 : aucune conversation ;
- 134.100 : service Approche/SIV dialogué ;
- fréquence inconnue : silence PHONIE.

## 11. Audio

- commencer à +9 dB ;
- vérifier le pic et le limiteur ;
- éviter une intervention massive du limiteur ;
- joindre le WAV si l'indicatif reste mal reconnu.

## 12. Fichiers à transmettre

```text
logs\PHONIE-DEV0.3.0.2-*.log
logs\sessions\radio-*.jsonl
recordings\last-ptt.wav
logs\airport-data\airport-LFBI-*.json
logs\airport-data\airport-LFBI-*.txt
```

Ajouter une capture du profil sélectionné et de la comparaison.
