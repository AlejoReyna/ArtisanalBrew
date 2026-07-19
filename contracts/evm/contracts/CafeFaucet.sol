// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {Ownable2Step} from "@openzeppelin/contracts/access/Ownable2Step.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

contract CafeFaucet is Ownable2Step, ReentrancyGuard {
    using SafeERC20 for IERC20;
    IERC20 public immutable cafeToken;
    uint256 public claimAmount;
    uint256 public cooldownSeconds;
    mapping(address => uint256) public lastClaimAt;

    event Claimed(address indexed account, uint256 amount);
    event ClaimAmountUpdated(uint256 previousAmount, uint256 nextAmount);
    event CooldownSecondsUpdated(uint256 previousCooldown, uint256 nextCooldown);

    constructor(address admin, IERC20 token, uint256 amount, uint256 cooldown)
        Ownable(admin)
    {
        require(address(token) != address(0) && amount > 0, "invalid faucet");
        cafeToken = token;
        claimAmount = amount;
        cooldownSeconds = cooldown;
    }

    function claim() external nonReentrant {
        require(canClaim(msg.sender), "cooldown active or faucet empty");
        lastClaimAt[msg.sender] = block.timestamp;
        cafeToken.safeTransfer(msg.sender, claimAmount);
        emit Claimed(msg.sender, claimAmount);
    }

    function nextClaimAt(address account) public view returns (uint256) {
        return lastClaimAt[account] + cooldownSeconds;
    }

    function canClaim(address account) public view returns (bool) {
        return block.timestamp >= nextClaimAt(account) && cafeToken.balanceOf(address(this)) >= claimAmount;
    }

    function setClaimAmount(uint256 amount) external onlyOwner {
        require(amount > 0, "amount required");
        emit ClaimAmountUpdated(claimAmount, amount);
        claimAmount = amount;
    }

    function setCooldownSeconds(uint256 cooldown) external onlyOwner {
        emit CooldownSecondsUpdated(cooldownSeconds, cooldown);
        cooldownSeconds = cooldown;
    }
}
