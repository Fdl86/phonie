# PHONIE DEV0.4.2.0 - Radio Intent & Contextual ASR

## Station et fréquence

- La fréquence active est l'autorité principale pour déterminer l'organisme dialogué.
- Le nom de station est facultatif, notamment après le premier contact.
- Suppression des détecteurs ouverts capables de transformer des fragments de phraséologie en station appelée.
- Appariement exclusivement avec les stations connues de la base SIA active et du contexte courant.
- Rapprochement tolérant d'une station active déformée, sans créer une station inconnue.
- Un fragment non apparié est traité comme aucune station appelée et ne provoque jamais de silence.

## Correction d'une mauvaise station appelée

- Une autre station connue explicitement appelée ne provoque plus un silence déroutant.
- PHONIE annonce l'identifiant de la station réellement accordée.
- La fréquence cible est annoncée seulement lorsqu'elle est officielle, unique et exploitable.
- En cas d'ambiguïté, PHONIE demande de vérifier la fréquence sans en inventer une.
- Nouveaux codes : `CALLED_STATION_CORRECTED_WITH_FREQUENCY` et `CALLED_STATION_CORRECTED_CHECK_FREQUENCY`.

## Analyse canonique

- Ajout de `RadioUtteranceAnalysis` dans `Phonie.Core`.
- Une seule analyse transporte le texte normalisé/corrigé, la station, l'intention, la salutation, les corrections et la confiance sémantique.
- L'interface et `GroundOperationsEngine` consomment la même analyse.
- Une intention trouvée par l'analyse de phraséologie peut désormais atteindre le moteur opérationnel au lieu d'être perdue.

## Intentions sol et départ

- Reconnaissance renforcée de `point d'attente + départ + intersection`.
- Une demande intersection peut être récupérée même si Whisper transforme « prêt », avec validation opérationnelle ultérieure par le moteur.
- Ajout de « paré » et de formulations de départ contextuelles.
- Ajout de « retour avec vous » sans exiger « de ».
- Les prompts après contact se concentrent sur l'intention, sans pousser à répéter le nom de la station.

## ASR contextuel

- Ajout de `SpeechRecognitionContext` transmis jusqu'à Whisper.
- Prompt dynamique court : station au premier contact seulement, indicatif complet/abrégé, piste, point d'attente et expressions plausibles selon l'état.
- Activation de `WithNoContext`, `WithSingleSegment`, `WithNoSpeechThreshold` et `WithProbabilities` sur Whisper.net 1.7.4.
- Rejet avant décision des PTT trop courts ou trop faibles.
- Rejet des descriptions de bruit et des sorties qui recopient essentiellement le prompt.
- Conservation du modèle chargé entre les transmissions.

## Diagnostics et tests

- Journalisation du brut, du nettoyé, du prompt, de la probabilité moyenne, de la durée et du niveau audio.
- Journalisation de la station détectée, de la confiance, de l'intention source et des corrections lexicales.
- Ajout d'un corpus de 26 transmissions réelles provenant de DEV0.4.1.9.
- Tests dédiés aux stations fantômes, aux stations déformées, aux corrections de fréquence et au départ intersection sans le mot « prêt ».

## Préservé

- Base radio officielle SIA sans fréquence d'aérodrome codée en dur dans la production.
- Arbitrage Facilities/SIA des canaux partagés.
- Historique par organisme et remise à zéro de session de vol.
- Silence sur ATIS, messages enregistrés et auto-information.
- Stockage portable sous le dossier PHONIE.

## Différé

- Vosk à grammaire contrainte en chemin nominal.
- Escalade automatique Small vers Turbo.
- Modèle français distillé.
- Réduction de `audio_ctx`.
- Validation sémantique complète des collationnements.

## Correctif packaging R2

- Le script de renormalisation enregistre désormais les suppressions issues d'une extraction propre (`git add -A`) avant `git add --renormalize`.
- Correction de l'échec `unable to stat` lorsqu'un fichier historique suivi par Git n'existe plus dans le nouveau ZIP.
- `.gitattributes` impose désormais ses propres fins de ligne LF pour supprimer l'avertissement `LF will be replaced by CRLF`.
