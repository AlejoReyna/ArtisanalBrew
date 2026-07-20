// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

/// @title ERC-7683 Intent Resolver Prototype
/// @notice A minimal local solver contract for testing cross-chain intents locally.
/// @dev Implements a stub for resolving intents to fund ERC-8183 jobs.
contract ERC7683ResolverFixture {
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

    mapping(bytes32 => bool) public isSubmitted;
    mapping(bytes32 => bool) public isResolved;
    
    event IntentSubmitted(bytes32 indexed orderId, address indexed user, uint256 destinationChainId, uint256 amountIn);
    event IntentFilled(bytes32 indexed orderId, address indexed solver, address receiver, uint256 amountOut);
    event IntentRefunded(bytes32 indexed orderId, address indexed user, uint256 amountIn);

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

    /// @notice Simulates submitting an intent on the source chain
    function submitIntent(IntentOrder calldata order) external {
        require(msg.sender == order.user, "Only user can submit");
        require(block.timestamp <= order.deadline, "Deadline passed");
        require(order.amountIn > 0, "Zero amount");
        require(order.sourceToken != address(0), "Zero source token");
        require(order.destinationToken != address(0), "Zero destination token");
        require(order.destinationReceiver != address(0), "Zero receiver");
        require(order.destinationChainId != 0, "Zero chain ID");
        
        bytes32 orderId = getOrderId(order);
        require(!isSubmitted[orderId], "Already submitted");
        require(!isResolved[orderId], "Already resolved");
        
        isSubmitted[orderId] = true;

        // Pull source tokens into escrow
        IERC20(order.sourceToken).safeTransferFrom(msg.sender, address(this), order.amountIn);
        
        emit IntentSubmitted(orderId, msg.sender, order.destinationChainId, order.amountIn);
    }

    /// @notice Simulates a solver filling the intent on the destination chain
    function fillIntent(IntentOrder calldata order, uint256 amountOut) external {
        require(amountOut >= order.minAmountOut, "Insufficient output");
        require(block.timestamp <= order.deadline, "Deadline passed");
        if (order.allowedSolver != address(0)) {
            require(msg.sender == order.allowedSolver, "Unauthorized solver");
        }
        
        bytes32 orderId = getOrderId(order);
        require(isSubmitted[orderId], "Not submitted");
        require(!isResolved[orderId], "Already resolved");
        isResolved[orderId] = true;
        
        // Solver pays the user on the destination chain
        IERC20(order.destinationToken).safeTransferFrom(msg.sender, order.destinationReceiver, amountOut);
        
        emit IntentFilled(orderId, msg.sender, order.destinationReceiver, amountOut);
    }

    /// @notice Refunds a submitted intent if the deadline has passed without a fill
    function refundIntent(IntentOrder calldata order) external {
        bytes32 orderId = getOrderId(order);
        require(isSubmitted[orderId], "Not submitted");
        require(!isResolved[orderId], "Already resolved");
        require(block.timestamp > order.deadline, "Deadline not passed");

        // Mark as resolved so it cannot be filled or refunded again
        isResolved[orderId] = true;

        // Return tokens to the user
        IERC20(order.sourceToken).safeTransfer(order.user, order.amountIn);

        emit IntentRefunded(orderId, order.user, order.amountIn);
    }
}
