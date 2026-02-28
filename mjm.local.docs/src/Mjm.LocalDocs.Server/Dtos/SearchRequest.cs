namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record SearchRequest(
    [Required] string Query,
    string? ProjectId = null,
    int Limit = 5);
