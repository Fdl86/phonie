namespace Phonie.Models;

public enum RadioPolicyKind
{
    Unknown,
    Controlled,
    InformationService,
    AutomaticInformation,
    SelfInformation,
}

public sealed record RadioPolicy(
    RadioPolicyKind Kind,
    string Title,
    string Guidance);
