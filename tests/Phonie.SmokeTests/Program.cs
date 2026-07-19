using Phonie.Services;

var failures = new List<string>();

Check(
    "Phrase laboratoire exacte",
    "Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, au parking pour tours de piste.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Variante laboratoire roulage",
    "Poitiers Tour, Fox Hotel novembre novembre yankee, au parking aviation générale, demande roulage.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking aviation générale",
    expectedIntention: "roulage");

Check(
    "Transcription Whisper réelle",
    "Poitiers tour, Fox Hotel, Novembre, Bonne-Novembre, et on quibonjour. Au parking, galesson général pour des tours de pisse avec l'information Alpha.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste",
    expectedAtis: "A");

Check(
    "Transcription Vosk réelle",
    "poitiers tour de fox hôtel novembre deux qui bonjour au parking son général pour des tours de piste l'information alpes",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste",
    expectedAtis: "A");

Check(
    "Indicatif précédent F-GABC",
    "Poitiers tour, Fox-Colf, Alfa Bravo, Charlie au parking, demande des tours de pistes.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Immatriculation écrite avec tiret",
    "Poitiers Tour, F-HNNY, au parking pour tours de piste.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Mots fusionnés FoxGolf",
    "Poitiers Tour, FoxGolf Alfa Bravo Charlie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Transcription Fabre Beauchardie",
    "Poitiers Tour, Fox Gold Fabre Beauchardie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Transcription va pas",
    "Poitiers Tour, Fox Gold va pas Bravo Charlie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

var stationOnly = PhraseologyService.Analyze("Poitiers Tour, bonjour.", "F-HNNY");
if (stationOnly.Callsign is not null)
{
    failures.Add($"Station seule : aucun indicatif attendu, obtenu {stationOnly.Callsign}.");
}

var wrongAircraft = PhraseologyService.Analyze(
    "Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking pour tours de piste.",
    "F-HNNY");
if (wrongAircraft.Callsign is not null)
{
    failures.Add($"Indicatif d'un autre avion : rejet attendu, obtenu {wrongAircraft.Callsign}.");
}

var noContext = PhraseologyService.Analyze(
    "Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking pour tours de piste.");
if (!string.Equals(noContext.Callsign, "F-GABC", StringComparison.Ordinal))
{
    failures.Add($"Sans ATC ID : F-GABC attendu, obtenu {noContext.Callsign ?? "-"}.");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("PHONIE phraseology smoke tests FAILED");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("PHONIE phraseology smoke tests OK");

void Check(
    string name,
    string phrase,
    string simulatorCallsign,
    string? expectedStation = null,
    string? expectedCallsign = null,
    string? expectedPosition = null,
    string? expectedIntention = null,
    string? expectedAtis = null)
{
    var analysis = PhraseologyService.Analyze(phrase, simulatorCallsign);
    Assert(name, "station", expectedStation, analysis.CalledStation);
    Assert(name, "indicatif", expectedCallsign, analysis.Callsign);
    Assert(name, "position", expectedPosition, analysis.Position);
    Assert(name, "intention", expectedIntention, analysis.Intention);
    Assert(name, "ATIS", expectedAtis, analysis.AtisLetter);
}

void Assert(string name, string field, string? expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        failures.Add($"{name} - {field} : {expected ?? "-"} attendu, {actual ?? "-"} obtenu.");
    }
}
