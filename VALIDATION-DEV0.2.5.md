# Validation PHONIE DEV0.2.5

Le build est validé lorsque :

- GitHub Actions compile et publie l'artefact Windows x64 ;
- PHONIE se connecte toujours à MSFS 2020 et MSFS 2024 ;
- le bouton `Lire LFBI` déclenche une demande sans bloquer l'interface ;
- les rapports JSON et TXT sont créés dans `PHONIE\logs\airport-data` ;
- les pistes et fréquences correspondent aux données du simulateur connecté ;
- le PTT clavier, le PTT HOTAS, l'audio et les logs de performance restent fonctionnels ;
- aucune donnée PHONIE n'est volontairement écrite dans AppData ou le registre ;
- aucune fréquence universelle LFBI n'est codée en dur.

Les écarts entre nombres déclarés et nombres effectivement reçus doivent apparaître dans le rapport et être étudiés avant le moteur ATIS.
