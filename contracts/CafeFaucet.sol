// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

/// @notice Lets any wallet claim a fixed amount of CAFE on a per-address cooldown, so
/// testers can get staking tokens without the app holding a mint/transfer private key.
/// Fund it by transferring CAFE to this contract's address after deployment.
contract CafeFaucet is Ownable, ReentrancyGuard {
    using SafeERC20 for IERC20;

    IERC20 public immutable cafeToken;

    uint256 public claimAmount;
    uint256 public cooldownSeconds;

    mapping(address => uint256) public lastClaimAt;

    event Claimed(address indexed account, uint256 amount);
    event ClaimAmountUpdated(uint256 previousAmount, uint256 nextAmount);
    event CooldownSecondsUpdated(uint256 previousCooldown, uint256 nextCooldown);
    event TokensRescued(address indexed recipient, uint256 amount);

    constructor(
        address initialOwner,
        IERC20 cafeToken_,
        uint256 claimAmount_,
        uint256 cooldownSeconds_
    ) Ownable(initialOwner) {
        require(address(cafeToken_) != address(0), "cafe token required");
        require(claimAmount_ > 0, "claim amount required");

        cafeToken = cafeToken_;
        claimAmount = claimAmount_;
        cooldownSeconds = cooldownSeconds_;
    }

    function claim() external nonReentrant {
        require(canClaim(msg.sender), "Cooldown active or faucet empty");

        lastClaimAt[msg.sender] = block.timestamp;
        cafeToken.safeTransfer(msg.sender, claimAmount);

        emit Claimed(msg.sender, claimAmount);
    }

    function nextClaimAt(address account) public view returns (uint256) {
        uint256 last = lastClaimAt[account];
        return last == 0 ? 0 : last + cooldownSeconds;
    }

    function canClaim(address account) public view returns (bool) {
        return block.timestamp >= nextClaimAt(account) &&
            cafeToken.balanceOf(address(this)) >= claimAmount;
    }

    function setClaimAmount(uint256 nextAmount) external onlyOwner {
        require(nextAmount > 0, "claim amount required");

        uint256 previousAmount = claimAmount;
        claimAmount = nextAmount;

        emit ClaimAmountUpdated(previousAmount, nextAmount);
    }

    function setCooldownSeconds(uint256 nextCooldown) external onlyOwner {
        uint256 previousCooldown = cooldownSeconds;
        cooldownSeconds = nextCooldown;

        emit CooldownSecondsUpdated(previousCooldown, nextCooldown);
    }

    function rescueTokens(address recipient, uint256 amount) external onlyOwner {
        require(recipient != address(0), "recipient required");

        cafeToken.safeTransfer(recipient, amount);

        emit TokensRescued(recipient, amount);
    }
}
