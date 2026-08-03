using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.Application.DTOs;

/// <summary>
/// A complete look submitted by the editor.
/// </summary>
/// <remarks>
/// Every slot is required rather than optional-and-merged: the editor always
/// holds the whole robot on screen, so a partial payload could only come from
/// a caller guessing, and merging would make "take the hat off" indistinguishable
/// from "leave the hat alone". Removing a piece is <c>"none"</c>, not omission.
///
/// Returning to the wallet-seeded look is a different operation entirely —
/// see <c>IProfileService.ResetAvatarAsync</c>, which clears the column so the
/// profile goes back to tracking its seed.
/// </remarks>
public sealed record UpdateAvatarRequest(
    string Backdrop,
    string Chassis,
    string Wear,
    string Visor,
    string Hat,
    string Hold)
{
    public RobotAvatar ToRobotAvatar() => new()
    {
        Backdrop = Backdrop,
        Chassis = Chassis,
        Wear = Wear,
        Visor = Visor,
        Hat = Hat,
        Hold = Hold
    };
}
