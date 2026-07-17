# Architecture de légèreté — DEV0.2.3

## Cadences

- SimConnect principal : environ 1 Hz ;
- météo locale : 1 lecture toutes les 30 secondes, valeur mise en cache entre deux lectures ;
- vumètre : 8 Hz avec périphérique WASAPI conservé en cache ;
- HOTAS : 20 Hz uniquement sur les périphériques connectés ;
- recherche de périphériques HOTAS : toutes les 2 secondes ;
- diagnostic CPU/mémoire : toutes les 5 secondes.

## Règles

- aucune boucle active sans temporisation ;
- aucun rescan audio continu ;
- aucun flush disque par paquet microphone ;
- aucune reconstruction ATIS dans cette version ;
- journal écran circulaire limité ;
- mesures de performance écrites dans un fichier compact ;
- les futurs moteurs Whisper et voix seront isolés dans des workers séparés et ne seront activés qu'à la demande.
