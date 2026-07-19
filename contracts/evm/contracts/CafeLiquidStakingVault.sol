// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {ERC4626} from "@openzeppelin/contracts/token/ERC20/extensions/ERC4626.sol";
import {ERC20} from "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import {Ownable2Step} from "@openzeppelin/contracts/access/Ownable2Step.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {IERC165} from "@openzeppelin/contracts/utils/introspection/IERC165.sol";

/// @notice ERC-4626 CAFE vault. Its ERC-20 shares are the transferable stCAFE position.
/// Reward checkpoints run through ERC-20's single update hook, including mint, burn, and transfers.
contract CafeLiquidStakingVault is ERC4626, Ownable2Step, Pausable, ReentrancyGuard {
    using SafeERC20 for IERC20;

    uint256 public constant REWARD_PRECISION = 1e18;
    uint256 public constant MAX_REWARD_DURATION = 365 days;

    IERC20 public immutable rewardToken;
    uint256 public rewardRate;
    uint256 public periodFinish;
    uint256 public lastUpdateTime;
    uint256 public rewardPerShareStored;
    uint256 public totalRewardsFunded;
    uint256 public totalRewardsClaimed;

    mapping(address => uint256) public userRewardPerSharePaid;
    mapping(address => uint256) private _rewards;

    event RewardAdded(uint256 indexed amount, uint256 indexed duration, uint256 rewardRate, uint256 periodFinish);
    event RewardPaid(address indexed account, uint256 indexed reward);
    event DepositsPaused(address indexed account);
    event DepositsUnpaused(address indexed account);

    constructor(address admin, IERC20 cafe, IERC20 coffee)
        ERC20("Staked CAFE", "stCAFE")
        ERC4626(cafe)
        Ownable(admin)
    {
        require(address(cafe) != address(0) && address(coffee) != address(0), "token required");
        require(address(cafe) != address(coffee), "tokens must differ");
        rewardToken = coffee;
        lastUpdateTime = block.timestamp;
    }

    /// @dev CAFE, COFFEE, and stCAFE all use the application's 18-decimal unit.
    /// Keeping the ERC-4626 offset at zero is required by the web and ledger paths.
    function _decimalsOffset() internal pure override returns (uint8) { return 0; }

    function maxDeposit(address) public view override returns (uint256) {
        return paused() ? 0 : type(uint256).max;
    }

    function maxMint(address) public view override returns (uint256) {
        return paused() ? 0 : type(uint256).max;
    }

    function deposit(uint256 assets, address receiver)
        public override nonReentrant whenNotPaused returns (uint256 shares)
    {
        shares = super.deposit(assets, receiver);
    }

    function mint(uint256 shares, address receiver)
        public override nonReentrant whenNotPaused returns (uint256 assets)
    {
        assets = super.mint(shares, receiver);
    }

    function withdraw(uint256 assets, address receiver, address owner)
        public override nonReentrant returns (uint256 shares)
    {
        shares = super.withdraw(assets, receiver, owner);
    }

    function redeem(uint256 shares, address receiver, address owner)
        public override nonReentrant returns (uint256 assets)
    {
        assets = super.redeem(shares, receiver, owner);
    }

    function pauseDeposits() external onlyOwner {
        _pause();
        emit DepositsPaused(msg.sender);
    }

    function unpauseDeposits() external onlyOwner {
        _unpause();
        emit DepositsUnpaused(msg.sender);
    }

    function lastTimeRewardApplicable() public view returns (uint256) {
        return block.timestamp < periodFinish ? block.timestamp : periodFinish;
    }

    function rewardPerShare() public view returns (uint256) {
        if (totalSupply() == 0) return rewardPerShareStored;
        uint256 elapsed = lastTimeRewardApplicable() - lastUpdateTime;
        return rewardPerShareStored + (elapsed * rewardRate * REWARD_PRECISION) / totalSupply();
    }

    function earned(address account) public view returns (uint256) {
        return _rewards[account] +
            (balanceOf(account) * (rewardPerShare() - userRewardPerSharePaid[account])) / REWARD_PRECISION;
    }

    function rewardBalance() public view returns (uint256) {
        return totalRewardsFunded - totalRewardsClaimed;
    }

    function notifyRewardAmount(uint256 amount, uint256 duration)
        external onlyOwner nonReentrant
    {
        require(amount > 0 && duration > 0 && duration <= MAX_REWARD_DURATION, "invalid reward schedule");
        _updateReward(address(0));
        require(rewardToken.balanceOf(address(this)) >= rewardBalance() + amount, "fund rewards first");

        uint256 leftover = block.timestamp < periodFinish ? (periodFinish - block.timestamp) * rewardRate : 0;
        rewardRate = (amount + leftover) / duration;
        require(rewardRate > 0, "reward rate is zero");
        lastUpdateTime = block.timestamp;
        periodFinish = block.timestamp + duration;
        totalRewardsFunded += amount;
        emit RewardAdded(amount, duration, rewardRate, periodFinish);
    }

    function claimRewards() external nonReentrant returns (uint256 reward) {
        _updateReward(msg.sender);
        reward = _rewards[msg.sender];
        require(reward > 0, "no rewards");
        _rewards[msg.sender] = 0;
        totalRewardsClaimed += reward;
        rewardToken.safeTransfer(msg.sender, reward);
        emit RewardPaid(msg.sender, reward);
    }

    function _deposit(address caller, address receiver, uint256 assets, uint256 shares) internal override {
        uint256 beforeBalance = IERC20(asset()).balanceOf(address(this));
        super._deposit(caller, receiver, assets, shares);
        require(IERC20(asset()).balanceOf(address(this)) - beforeBalance == assets, "unsupported asset behavior");
    }

    function _withdraw(address caller, address receiver, address owner, uint256 assets, uint256 shares)
        internal override
    {
        uint256 beforeBalance = IERC20(asset()).balanceOf(receiver);
        super._withdraw(caller, receiver, owner, assets, shares);
        require(IERC20(asset()).balanceOf(receiver) - beforeBalance == assets, "unsupported asset behavior");
    }

    function _update(address from, address to, uint256 value) internal override(ERC20) {
        _updateReward(from);
        if (to != address(0) && to != from) _updateReward(to);
        super._update(from, to, value);
    }

    function _updateReward(address account) internal {
        uint256 current = rewardPerShare();
        rewardPerShareStored = current;
        lastUpdateTime = lastTimeRewardApplicable();
        if (account != address(0)) {
            _rewards[account] = earnedAt(account, current);
            userRewardPerSharePaid[account] = current;
        }
    }

    function earnedAt(address account, uint256 current) internal view returns (uint256) {
        return _rewards[account] +
            (balanceOf(account) * (current - userRewardPerSharePaid[account])) / REWARD_PRECISION;
    }
}
