namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateKnowHowRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(500000)] string Content);
