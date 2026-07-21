// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

/// @title ERC-7683 Destination-Side Fill Prototype
/// @notice Minimal destination-chain fill contract for genuinely separate source/destination
///         deployments (the two-node cross-chain smoke test).
/// @dev ERC7683ResolverFixture conflates the origin (submit/refund) and destination (fill) roles
///      into one contract with shared `isSubmitted` storage — a reasonable simplification when
///      both roles run on the SAME chain instance (as in ERC7683ResolverFixture.test.ts), but
///      unworkable across two genuinely separate chains: a destination-chain contract has no way
///      to observe a source chain's storage without a bridge or light client, which this project's
///      own stack plan explicitly scopes ERC-7683 OUT of being ("must not be used as a bridge,
///      liquidity source, or automatic safety guarantee").
///
///      This contract intentionally omits any `isSubmitted`-style check. Verifying that a
///      corresponding intent was genuinely submitted on the source chain is the solver's
///      responsibility — exactly how real ERC-7683 solvers operate: they independently verify the
///      source-chain intent (by watching its events/state) before risking their own destination-
///      chain liquidity to fill it. ERC7683ResolverFixture is unchanged and remains the correct
///      choice for same-chain testing; use this contract only for the destination side of a
///      genuine two-chain deployment.
contract ERC7683DestinationResolverFixture {
    using SafeERC20 for IERC20;

    struct IntentOrder {
        address user;
        address sourceToken;
        uint256 amountIn;
        uint256 destinationChainId;
        address destinationToken;
        address destinationReceiver;
        uint256 minAmountOut;
        uint256 deadline;
        uint256 nonce;
        address allowedSolver;
    }

    mapping(bytes32 => bool) public isResolved;

    event IntentFilled(bytes32 indexed orderId, address indexed solver, address receiver, uint256 amountOut);

    function getOrderId(IntentOrder memory order) public pure returns (bytes32) {
        return keccak256(abi.encode(
            order.user,
            order.sourceToken,
            order.amountIn,
            order.destinationChainId,
            order.destinationToken,
            order.destinationReceiver,
            order.minAmountOut,
            order.deadline,
            order.nonce,
            order.allowedSolver
        ));
    }

    /// @notice Fills an intent the solver has independently verified was submitted on the source chain.
    function fillIntent(IntentOrder calldata order, uint256 amountOut) external {
        require(amountOut >= order.minAmountOut, "Insufficient output");
        require(block.timestamp <= order.deadline, "Deadline passed");
        if (order.allowedSolver != address(0)) {
            require(msg.sender == order.allowedSolver, "Unauthorized solver");
        }

        bytes32 orderId = getOrderId(order);
        require(!isResolved[orderId], "Already resolved");
        isResolved[orderId] = true;

        IERC20(order.destinationToken).safeTransferFrom(msg.sender, order.destinationReceiver, amountOut);

        emit IntentFilled(orderId, msg.sender, order.destinationReceiver, amountOut);
    }
}
