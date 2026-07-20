# Changelog PHONIE DEV0.4.0.2

## Correctif de compilation RC

- typage nullable explicite des champs piste normalisés dans `AirportGroundModelBuilder` afin de corriger l'erreur `CS0173` du workflow GitHub Actions.

## GROUND OPERATIONS RC

- ajout du projet métier indépendant `Phonie.Core` ;
- normalisation Facilities compatible MSFS 2020 et MSFS 2024 ;
- neutralisation des champs piste indéfinis sur les TaxiPaths non-piste de MSFS 2020 ;
- construction du graphe taxi à partir des points, parkings, chemins et noms réels ;
- association des points d'attente aux pistes physiques, valable pour les deux sens d'une même piste ;
- localisation de l'avion sur parking, taxiway, point d'attente, piste ou en vol ;
- interrogation événementielle du trafic avion proche via SimConnect ;
- exclusion des points et segments occupés ;
- routage vers le point d'attente libre accessible le plus proche par coût de chemin ;
- refus d'annoncer un point d'attente sans nom radio fiable ;
- annonce des taxiways réellement traversés ;
- machine d'état du parking au décollage ;
- reconnaissance distincte du roulage, du point d'attente, de l'alignement, du décollage et de la demande combinée ;
- conservation de la dernière clairance et absence de recalcul arbitraire lors d'une demande répétée ;
- indicatif complet au premier contact puis abréviation autorisée, par exemple F-HNNY puis F-NY ;
- silence effectif sur ATIS, AWS, 124.000 LFBI, CTAF, UNICOM et auto-information ;
- ATIS dynamique enrichi avec plafond et point de rosée lorsque disponibles ;
- cache local d'un bulletin ATIS complet par révision ;
- synthèse dynamique de chaque réponse complète du contrôleur ;
- affichage de l'état sol, de la piste, du point d'attente, de la route et de l'occupation ;
- journal JSONL des décisions dans `logs\ground-operations` ;
- tests automatiques synthétiques et tests sur captures réelles LFBI MSFS 2020/2024 ;
- workflow bloquant la publication si un test échoue ;
- version visible `DEV0.4.0.2 - GROUND OPERATIONS RC`.

## Préservé depuis DEV0.3.0.5

SimConnect, scanner radio, PTT clavier/HOTAS, enregistrement, comparaison ASR, Whisper CPU/Vulkan/Turbo, Vosk expérimental, téléchargement et vérification des modèles, préchauffage Turbo, benchmark GPU/VRAM et stockage portable.
