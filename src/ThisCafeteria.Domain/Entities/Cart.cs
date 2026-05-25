namespace ThisCafeteria.Domain.Entities;

public sealed class Cart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserProfileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<CartItem> Items { get; set; } = [];

    public UserProfile? UserProfile { get; set; }
}
