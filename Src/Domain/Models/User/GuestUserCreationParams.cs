using Domain.Enums;

namespace Domain.Models.User;

public class GuestUserCreationParams
{
    public Roles Role { get; set; } = Roles.Guest;
    public AuthScheme AuthScheme { get; set; } = AuthScheme.Internal;
}