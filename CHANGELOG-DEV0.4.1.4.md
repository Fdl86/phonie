# Changelog DEV0.4.1.4

## C# Nullable Compile Hotfix

- Correction de l'erreur `CS0173` dans `OfficialRadioCatalogService.cs`.
- La date optionnelle du prochain cycle AIRAC est maintenant explicitement typée `DateTimeOffset?`.
- Aucun changement fonctionnel du moteur radio ou Ground Operations.

## Workflow de compilation

- La restauration NuGet, la compilation avec avertissements traités comme erreurs et les tests C# passent avant la génération réseau SIA.
- Une erreur source est désormais visible immédiatement au lieu d'apparaître après la collecte des 420 aérodromes.
- Le payload SIA généré reste validé et contrôlé avant la publication Windows x64.

## État de la base SIA observé sur DEV0.4.1.3

Le dernier workflow a atteint 420 aérodromes et 672 fréquences, puis a validé la base et l'absence de fréquences codées en dur. DEV0.4.1.4 conserve cette chaîne sans modification.
