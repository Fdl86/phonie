# Protocole rapide PHONIE DEV0.3.0.4

1. Vérifier que GitHub Actions affiche `Build succeeded` puis `PHONIE phraseology smoke tests OK`.
2. Lancer PHONIE et sélectionner `Whisper Large-v3 Turbo Vulkan - qualité`.
3. Si PHONIE était lancé en profil CPU, redémarrer l'application.
4. Cliquer sur `Installer profil` et vérifier la fin du téléchargement avec validation SHA-256.
5. Sur 118.505, enregistrer :

   `Poitiers Tour, Fox Hotel November November Yankee, au parking pour des tours de piste, avec l'information Alpha.`

6. Vérifier : F-HNNY, Poitiers Tour, parking, tours de piste, Alpha.
7. Noter le temps de transcription Turbo.
8. Cliquer une fois sur `Comparer` et comparer Small Vulkan, Large-v3 Turbo Vulkan et Vosk.

Envoyer une capture et le log uniquement en cas d'erreur, de crash, de mauvais indicatif ou de téléchargement refusé.
