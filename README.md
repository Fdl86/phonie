# PHONIE — DEV0.2.3 PTT HOTAS & légèreté mesurée

PHONIE est une application Windows de contrôle aérien vocal VFR destinée à Microsoft Flight Simulator 2020 et Microsoft Flight Simulator 2024.

## Contenu de DEV0.2.3

- conservation du socle SimConnect, audio, météo locale et scanner radio validé en DEV0.2.2 ;
- interface sombre inspirée de CAP CLAIR, avec thème clair optionnel ;
- affectation directe d'un bouton joystick/HOTAS depuis l'interface ;
- PTT clavier conservé en secours et utilisable simultanément ;
- mémorisation du périphérique HOTAS par constructeur, produit et nom Windows ;
- reconnexion automatique du HOTAS après débranchement/rebranchement ;
- lecture des boutons à 20 Hz uniquement sur les périphériques réellement connectés ;
- aucun SDK, pilote ou logiciel supplémentaire requis pour le PTT HOTAS ;
- log de légèreté automatique, prêt à être envoyé après une batterie de tests ;
- mesures toutes les cinq secondes : CPU courant/moyen/maximal, mémoire, threads, handles et cadence SimConnect ;
- résumé de session écrit à la fermeture de PHONIE ;
- bouton **Marquer le test** pour placer un repère dans le log pendant une manipulation ;
- bouton **Ouvrir les logs** pour accéder immédiatement au fichier ;
- vumètre audio optimisé : le périphérique Windows n'est plus rouvert à chaque rafraîchissement ;
- enregistrement audio optimisé : suppression des écritures forcées sur disque à chaque paquet audio ;
- météo locale lue toutes les 30 secondes au lieu d'être redemandée chaque seconde ;
- journal visuel limité à 250 lignes, tandis que le log de session reste complet sur disque.

## Emplacement du log diagnostic

Chaque lancement crée un fichier autonome dans :

```text
%LOCALAPPDATA%\PHONIE\Diagnostics
```

Nom type :

```text
PHONIE-DEV0.2.3-20260717-071530.log
```

Le fichier contient des événements lisibles et des lignes `PERF` séparées par tabulations. À la fermeture, un résumé indique le CPU moyen/maximal, la mémoire maximale, le nombre de snapshots SimConnect et les PTT enregistrés.

## Construction

1. Conserver uniquement `.git` dans le dossier local du dépôt.
2. Copier le contenu complet du zip dans le dépôt.
3. Commit puis push sur `main` avec GitHub Desktop.
4. Attendre le run vert **Build PHONIE DEV0.2.3**.
5. Télécharger l'artifact `PHONIE-DEV0.2.3-win-x64`.
6. Décompresser puis lancer `PHONIE.exe`.

Aucun SDK MSFS, Visual Studio, .NET, Python ou Node.js n'est requis sur le PC de test.

## Prérequis dans MSFS

Désactiver les communications radio IA et les voix de l'ATC intégré afin d'éviter que PHONIE et l'ATC de Microsoft Flight Simulator parlent en même temps.

Voir `TEST-DEV0.2.3.md` pour le protocole conseillé.
