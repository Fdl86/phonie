# PHONIE DEV0.3.0.1 - STARTUP HOTFIX

Cause confirmée du démarrage silencieux de DEV0.3.0 :

- le `CheckBox` de transcription automatique était initialisé à `True` dans le XAML ;
- son événement `Checked` se déclenchait pendant `InitializeComponent()` ;
- le journal portable recevait bien la première ligne ;
- le gestionnaire tentait ensuite d'écrire dans `LogBox`, qui n'était pas encore créé ;
- une `NullReferenceException` fermait l'application avant l'affichage de la fenêtre.

Correctifs :

- inhibition de tous les événements de réglages pendant la construction XAML ;
- garde supplémentaire sur l'initialisation du contrôle ;
- journal UI tolérant lorsque `LogBox` n'existe pas encore ;
- capture des exceptions de démarrage et création de `logs\startup-fatal.log` ;
- message visible en cas de nouvelle erreur fatale.
