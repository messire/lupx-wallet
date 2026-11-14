using FluentAssertions;
using LupexWallet.Funds.Application;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>Free funds and the reserve coverage deficit (ADR-0010 §4, B7; §7).</summary>
public sealed class ReservationCoverageTests
{
    [Theory]
    [InlineData(500, 200, 300)]
    [InlineData(100, 150, -50)]
    [InlineData(0, 0, 0)]
    public void Free_IsBalanceMinusReserved(decimal balance, decimal reserved, decimal expected) =>
        ReservationCoverage.Free(balance, reserved).Should().Be(expected);

    [Theory]
    [InlineData(500, 200, 0)]
    [InlineData(200, 200, 0)]
    [InlineData(150, 200, 50)]
    [InlineData(0, 200, 200)]
    [InlineData(-30, 200, 200)]
    [InlineData(-30, 0, 0)]
    public void WalletDeficit_IsTheUncoveredPartOfTheReserves(decimal balance, decimal reserved, decimal expected) =>
        ReservationCoverage.WalletDeficit(balance, reserved).Should().Be(expected);

    [Theory]
    [InlineData(100, 30, 30)]
    [InlineData(20, 30, 20)]
    [InlineData(20, 0, 0)]
    public void AllocationDeficit_IsCappedAtTheAllocation(decimal allocation, decimal walletDeficit, decimal expected) =>
        ReservationCoverage.AllocationDeficit(allocation, walletDeficit).Should().Be(expected);
}
