# ATC VFR — DEV0.1 SimConnect

Première base technique de l'application ATC VFR pour Microsoft Flight Simulator 2020 et Microsoft Flight Simulator 2024.

## Objectif de cette version

DEV0.1 ne contient pas encore de reconnaissance vocale ni de contrôleur ATC. Elle valide uniquement la fondation :

- lancement dans n'importe quel ordre avec MSFS ;
- connexion locale automatique à SimConnect ;
- détection MSFS 2020 / MSFS 2024 ;
- reconnexion automatique si le simulateur est fermé ou redémarré ;
- lecture de la position, altitude, cap, vitesses, état sol, COM1 et transpondeur ;
- calcul de la distance à LFBI ;
- application Windows x64 portable et autonome.

## Construction sans installation locale

1. Créer un dépôt GitHub public ou privé.
2. Conserver uniquement le dossier `.git` dans le dossier local du dépôt.
3. Copier tout le contenu de ce zip dans le dossier du dépôt.
4. Pousser sur `main` avec GitHub Desktop.
5. Ouvrir l'onglet **Actions** du dépôt GitHub.
6. Attendre le dernier run vert **Build ATC VFR DEV0.1**.
7. Télécharger l'artifact `ATC-VFR-DEV0.1-win-x64`.
8. Décompresser l'artifact et lancer `ATC-VFR.exe`.

Aucun SDK MSFS, Visual Studio, .NET, Python ou Node.js n'est requis sur le PC de test.

## Dépendance SimConnect

Le projet utilise le paquet NuGet `SimConnect.NET` 0.2.1, sous licence MIT. Le paquet embarque la bibliothèque native SimConnect nécessaire à la publication Windows x64.

## Test conseillé

Voir `TEST-DEV0.1.md`.
