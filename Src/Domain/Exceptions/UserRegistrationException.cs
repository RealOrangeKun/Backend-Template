namespace Domain.Exceptions;

public class UserRegistrationException(string message) : DomainException(message)
{
}
