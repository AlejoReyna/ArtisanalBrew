// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

contract CafeStakingPool is Ownable, ReentrancyGuard {
    using SafeERC20 for IERC20;

    uint256 private constant REWARD_PRECISION = 1e18;
    uint256 private constant BPS_DENOMINATOR = 10_000;
    uint256 private constant SECONDS_PER_YEAR = 365 days;

    IERC20 public immutable stakingToken;
    IERC20 public immutable rewardToken;

    uint256 public annualRewardBps;
    uint256 public totalStaked;
    uint256 public rewardPerTokenStored;
    uint256 public lastUpdateTime;

    mapping(address => uint256) private stakedBalances;
    mapping(address => uint256) private rewardPerTokenPaid;
    mapping(address => uint256) private pendingRewards;

    event Staked(address indexed account, uint256 amount);
    event Unstaked(address indexed account, uint256 amount);
    event RewardPaid(address indexed account, uint256 amount);
    event AnnualRewardBpsUpdated(uint256 previousBps, uint256 nextBps);
    event RewardTokenRescued(address indexed recipient, uint256 amount);

    constructor(
        address initialOwner,
        IERC20 stakingToken_,
        IERC20 rewardToken_,
        uint256 annualRewardBps_
    ) Ownable(initialOwner) {
        require(address(stakingToken_) != address(0), "staking token required");
        require(address(rewardToken_) != address(0), "reward token required");

        stakingToken = stakingToken_;
        rewardToken = rewardToken_;
        annualRewardBps = annualRewardBps_;
        lastUpdateTime = block.timestamp;
    }

    function stake(uint256 amount) external nonReentrant updateReward(msg.sender) {
        require(amount > 0, "amount required");

        totalStaked += amount;
        stakedBalances[msg.sender] += amount;
        stakingToken.safeTransferFrom(msg.sender, address(this), amount);

        emit Staked(msg.sender, amount);
    }

    function unstake(uint256 amount) external nonReentrant updateReward(msg.sender) {
        _unstake(msg.sender, amount);
    }

    function withdraw(uint256 amount) external nonReentrant updateReward(msg.sender) {
        _unstake(msg.sender, amount);
    }

    function claimRewards() external nonReentrant updateReward(msg.sender) {
        uint256 reward = pendingRewards[msg.sender];
        require(reward > 0, "no rewards");

        pendingRewards[msg.sender] = 0;
        rewardToken.safeTransfer(msg.sender, reward);

        emit RewardPaid(msg.sender, reward);
    }

    function balanceOf(address account) external view returns (uint256) {
        return stakedBalances[account];
    }

    function earned(address account) public view returns (uint256) {
        return pendingRewards[account] +
            ((stakedBalances[account] * (rewardPerToken() - rewardPerTokenPaid[account])) / REWARD_PRECISION);
    }

    function rewardPerToken() public view returns (uint256) {
        if (totalStaked == 0) {
            return rewardPerTokenStored;
        }

        uint256 elapsed = block.timestamp - lastUpdateTime;
        uint256 annualReward = (totalStaked * annualRewardBps) / BPS_DENOMINATOR;
        uint256 accruedReward = (annualReward * elapsed) / SECONDS_PER_YEAR;

        return rewardPerTokenStored + ((accruedReward * REWARD_PRECISION) / totalStaked);
    }

    function setAnnualRewardBps(uint256 nextBps) external onlyOwner updateReward(address(0)) {
        require(nextBps <= BPS_DENOMINATOR, "reward too high");

        uint256 previousBps = annualRewardBps;
        annualRewardBps = nextBps;

        emit AnnualRewardBpsUpdated(previousBps, nextBps);
    }

    function rescueRewardTokens(address recipient, uint256 amount) external onlyOwner {
        require(recipient != address(0), "recipient required");

        rewardToken.safeTransfer(recipient, amount);

        emit RewardTokenRescued(recipient, amount);
    }

    function _unstake(address account, uint256 amount) private {
        require(amount > 0, "amount required");
        require(stakedBalances[account] >= amount, "insufficient stake");

        stakedBalances[account] -= amount;
        totalStaked -= amount;
        stakingToken.safeTransfer(account, amount);

        emit Unstaked(account, amount);
    }

    modifier updateReward(address account) {
        rewardPerTokenStored = rewardPerToken();
        lastUpdateTime = block.timestamp;

        if (account != address(0)) {
            pendingRewards[account] = earned(account);
            rewardPerTokenPaid[account] = rewardPerTokenStored;
        }

        _;
    }
}
