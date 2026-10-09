namespace WatchTower.Application.Exceptions;

public class DuplicateEmailException(string email)
    : Exception($"Email '{email}' is already registered.");

public class InvalidCredentialsException()
    : Exception("Invalid email or password.");

public class NotFoundException(string message)
    : Exception(message);

public class ValidationException(string message)
    : Exception(message);

public class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
