namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateKnowHowRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(500000)] string Content);
