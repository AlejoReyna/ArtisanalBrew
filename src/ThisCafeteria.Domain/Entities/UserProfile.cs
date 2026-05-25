using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Domain.Entities;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Order> Orders { get; set; } = [];
    public Cart? Cart { get; set; }
}
