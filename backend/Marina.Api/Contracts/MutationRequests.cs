using System.ComponentModel.DataAnnotations;

namespace Marina.Api.Contracts;

public sealed record ForgotPasswordRequest([Required, EmailAddress, MaxLength(256)] string Email);
public sealed record ResendVerificationRequest([Required, EmailAddress, MaxLength(256)] string Email);
public sealed record UpdateCartQuantityRequest([Range(1, 20)] int Quantity);
public sealed record CancelOrderRequest([MaxLength(300)] string? Reason);
public sealed record SetUserActiveRequest([Required] bool? Active);
