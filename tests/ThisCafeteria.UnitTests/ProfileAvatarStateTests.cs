using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Web.Services;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// The header's per-circuit avatar cache.
/// </summary>
public sealed class ProfileAvatarStateTests
{
    private const string UserId = "user-1";
    private static readonly RobotAvatarDto Stored =
        new("night", "moss", "apron", "happy", "toque", "mug");

    [Fact]
    public async Task TheAvatarIsReadOncePerCircuit()
    {
        // The layout mounts once and survives every navigation, so a second
        // read would be a query on every page for a value that cannot change
        // without going through Publish.
        var service = ServiceReturning(Stored);
        var state = StateOver(service);

        await state.GetOrLoadAsync(UserId);
        await state.GetOrLoadAsync(UserId);
        await state.GetOrLoadAsync(UserId);

        service.Verify(
            profile => profile.GetAvatarForApplicationUserAsync(UserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ADifferentAccountInTheSameCircuitDoesNotInheritTheCachedRobot()
    {
        var service = ServiceReturning(Stored);
        var state = StateOver(service);

        await state.GetOrLoadAsync(UserId);
        await state.GetOrLoadAsync("user-2");

        service.Verify(
            profile => profile.GetAvatarForApplicationUserAsync("user-2", It.IsAny<CancellationToken>()),
            Times.Once);
        state.ApplicationUserId.Should().Be("user-2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnAnonymousVisitorGetsNoRobotAndCostsNoQuery(string? userId)
    {
        var service = ServiceReturning(Stored);
        var state = StateOver(service);

        (await state.GetOrLoadAsync(userId)).Should().BeNull();

        service.Verify(
            profile => profile.GetAvatarForApplicationUserAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishingAfterAnEditNotifiesTheHeaderAndSkipsTheNextRead()
    {
        var service = ServiceReturning(Stored);
        var state = StateOver(service);
        await state.GetOrLoadAsync(UserId);

        var notified = 0;
        state.Changed += () => notified++;

        var edited = Stored with { Hat = "crown" };
        state.Publish(UserId, edited);

        notified.Should().Be(1);
        state.Current.Should().Be(edited);

        // And the header does not go back to the database for what it was just told.
        (await state.GetOrLoadAsync(UserId)).Should().Be(edited);
        service.Verify(
            profile => profile.GetAvatarForApplicationUserAsync(UserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearingDropsTheRobotAndForcesAFreshRead()
    {
        var service = ServiceReturning(Stored);
        var state = StateOver(service);
        await state.GetOrLoadAsync(UserId);

        state.Clear();

        state.Current.Should().BeNull();
        state.ApplicationUserId.Should().BeNull();

        await state.GetOrLoadAsync(UserId);
        service.Verify(
            profile => profile.GetAvatarForApplicationUserAsync(UserId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static Mock<IProfileService> ServiceReturning(RobotAvatarDto avatar)
    {
        var service = new Mock<IProfileService>();
        service
            .Setup(profile => profile.GetAvatarForApplicationUserAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(avatar);
        return service;
    }

    /// <summary>
    /// A real container, so the state resolves the service through a real
    /// child scope — the thing that keeps the header off the circuit's shared
    /// DbContext. Handing it the mock directly would not exercise that.
    /// </summary>
    private static ProfileAvatarState StateOver(Mock<IProfileService> service)
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => service.Object)
            .BuildServiceProvider();

        return new ProfileAvatarState(provider.GetRequiredService<IServiceScopeFactory>());
    }
}
