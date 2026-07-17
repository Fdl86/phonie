using Phonie.Models;

namespace Phonie.Services;

public static class RadioPolicyResolver
{
    public static RadioPolicy Resolve(string? stationType)
    {
        var type = stationType?.Trim().ToUpperInvariant() ?? string.Empty;

        return type switch
        {
            "TWR" or "GND" or "CLR" or "APPR" or "DEP" => new RadioPolicy(
                RadioPolicyKind.Controlled,
                "Organisme contrôlé",
                "PHONIE répondra au pilote selon la situation."),

            "FSS" => new RadioPolicy(
                RadioPolicyKind.InformationService,
                "Service d'information / AFIS",
                "PHONIE pourra répondre et transmettre les paramètres."),

            "ATIS" or "AWS" => new RadioPolicy(
                RadioPolicyKind.AutomaticInformation,
                "Information automatique",
                "PHONIE diffusera l'information sans dialogue."),

            "CTAF" or "UNI" => new RadioPolicy(
                RadioPolicyKind.SelfInformation,
                "Auto-information",
                "PHONIE restera silencieux ; seuls des trafics réalistes pourront être entendus."),

            _ => new RadioPolicy(
                RadioPolicyKind.Unknown,
                "Fréquence non identifiée",
                "Aucune réponse automatique tant que le service n'est pas déterminé."),
        };
    }
}
