namespace WatchTower.Application.DTOs;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record RegisterResponse(Guid UserId, string Email);

public record LoginResponse(string AccessToken, int ExpiresIn, string TokenType);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);
