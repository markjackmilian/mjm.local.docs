namespace Mjm.LocalDocs.Server.Dtos;

using System.ComponentModel.DataAnnotations;

public sealed record CreateTokenRequest(
    [Required] string Name,
    DateTimeOffset? ExpiresAt = null);
