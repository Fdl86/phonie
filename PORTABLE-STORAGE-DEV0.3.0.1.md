# Stockage portable DEV0.3.0.1

Toutes les données propres à PHONIE restent sous le dossier de l'application :

- réglages dans `config` ;
- diagnostics et Airport Data dans `logs` ;
- échanges radio dans `logs\sessions` ;
- modèle Whisper dans `models\whisper` ;
- dernier PTT dans `recordings` ;
- fichiers temporaires dans `cache`.

Si un dossier n'est pas accessible en écriture, PHONIE doit afficher une erreur au lancement plutôt que d'utiliser silencieusement AppData.
