using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Domain.Entities;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The robot this account shows, or <c>null</c> while it has never been
    /// edited — in which case the look comes from
    /// <see cref="AvatarSeed.FromWallet"/> instead of from this column.
    /// Null is the meaningful "never chosen" state, so do not default it.
    /// </summary>
    public RobotAvatar? Avatar { get; set; }

    public List<Order> Orders { get; set; } = [];
    public List<CouponRedemption> CouponRedemptions { get; set; } = [];
    public Cart? Cart { get; set; }
}
