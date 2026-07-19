// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {ERC2771Context} from "@openzeppelin/contracts/metatx/ERC2771Context.sol";

/// @notice Non-upgradeable, no-hook local implementation of the ERC-8183 draft.
/// @dev Draft reference: EIP-8183, February 2026. The optional hook surface is
/// deliberately disabled for the first slice so expiry refunds remain recoverable.
contract AgenticCommerceEscrow is ERC2771Context, ReentrancyGuard {
    using SafeERC20 for IERC20;

    uint256 public constant MAX_FEE_BPS = 500;

    enum JobStatus { Open, Funded, Submitted, Completed, Rejected, Expired }

    struct Job {
        uint256 id;
        address client;
        address provider;
        address evaluator;
        string description;
        uint256 budget;
        uint256 expiredAt;
        JobStatus status;
    }

    IERC20 public immutable paymentToken;
    address public immutable platformTreasury;
    uint256 public immutable platformFeeBps;
    mapping(uint256 => Job) public jobs;
    uint256 public jobCounter;

    error InvalidJob();
    error WrongStatus();
    error Unauthorized();
    error ZeroAddress();
    error ExpiryTooShort();
    error ZeroBudget();
    error ProviderNotSet();
    error BudgetMismatch();
    error FeeTooHigh();
    error Underfunded(uint256 expected, uint256 received);

    event JobCreated(uint256 indexed jobId, address indexed client, address indexed provider, address evaluator, uint256 expiredAt);
    event ProviderSet(uint256 indexed jobId, address indexed provider);
    event BudgetSet(uint256 indexed jobId, uint256 amount);
    event JobFunded(uint256 indexed jobId, address indexed client, uint256 amount);
    event JobSubmitted(uint256 indexed jobId, address indexed provider, bytes32 deliverable);
    event JobCompleted(uint256 indexed jobId, address indexed evaluator, bytes32 reason);
    event JobRejected(uint256 indexed jobId, address indexed rejector, bytes32 reason);
    event JobExpired(uint256 indexed jobId);
    event PaymentReleased(uint256 indexed jobId, address indexed provider, uint256 amount);
    event Refunded(uint256 indexed jobId, address indexed client, uint256 amount);

    constructor(IERC20 paymentToken_, address treasury_, uint256 platformFeeBps_, address trustedForwarder_)
        ERC2771Context(trustedForwarder_)
    {
        if (address(paymentToken_) == address(0) || treasury_ == address(0)) revert ZeroAddress();
        if (platformFeeBps_ > MAX_FEE_BPS) revert FeeTooHigh();
        paymentToken = paymentToken_;
        platformTreasury = treasury_;
        platformFeeBps = platformFeeBps_;
    }

    function createJob(address provider, address evaluator, uint256 expiredAt, string calldata description)
        external returns (uint256 jobId)
    {
        if (evaluator == address(0)) revert ZeroAddress();
        if (expiredAt <= block.timestamp + 5 minutes) revert ExpiryTooShort();
        jobId = ++jobCounter;
        jobs[jobId] = Job(jobId, _msgSender(), provider, evaluator, description, 0, expiredAt, JobStatus.Open);
        emit JobCreated(jobId, _msgSender(), provider, evaluator, expiredAt);
    }

    function setProvider(uint256 jobId, address provider_) external {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Open || job.provider != address(0)) revert WrongStatus();
        if (_msgSender() != job.client) revert Unauthorized();
        if (provider_ == address(0)) revert ZeroAddress();
        job.provider = provider_;
        emit ProviderSet(jobId, provider_);
    }

    function setBudget(uint256 jobId, uint256 amount, bytes calldata) external {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Open || job.provider == address(0)) revert WrongStatus();
        address sender = _msgSender();
        if (sender != job.provider && sender != job.client) revert Unauthorized();
        if (amount == 0) revert ZeroBudget();
        job.budget = amount;
        emit BudgetSet(jobId, amount);
    }

    function fund(uint256 jobId, uint256 expectedBudget, bytes calldata) external nonReentrant {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Open || block.timestamp >= job.expiredAt) revert WrongStatus();
        if (_msgSender() != job.client) revert Unauthorized();
        if (job.provider == address(0)) revert ProviderNotSet();
        if (job.budget == 0) revert ZeroBudget();
        if (job.budget != expectedBudget) revert BudgetMismatch();
        job.status = JobStatus.Funded;
        uint256 beforeBalance = paymentToken.balanceOf(address(this));
        paymentToken.safeTransferFrom(job.client, address(this), job.budget);
        uint256 received = paymentToken.balanceOf(address(this)) - beforeBalance;
        if (received != job.budget) revert Underfunded(job.budget, received);
        emit JobFunded(jobId, job.client, job.budget);
    }

    function submit(uint256 jobId, bytes32 deliverable, bytes calldata) external {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Funded) revert WrongStatus();
        if (_msgSender() != job.provider) revert Unauthorized();
        job.status = JobStatus.Submitted;
        emit JobSubmitted(jobId, job.provider, deliverable);
    }

    function complete(uint256 jobId, bytes32 reason, bytes calldata) external nonReentrant {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Submitted) revert WrongStatus();
        if (_msgSender() != job.evaluator) revert Unauthorized();
        job.status = JobStatus.Completed;
        uint256 fee = (job.budget * platformFeeBps) / 10_000;
        uint256 payout = job.budget - fee;
        if (fee != 0) paymentToken.safeTransfer(platformTreasury, fee);
        paymentToken.safeTransfer(job.provider, payout);
        emit JobCompleted(jobId, _msgSender(), reason);
        emit PaymentReleased(jobId, job.provider, payout);
    }

    function reject(uint256 jobId, bytes32 reason, bytes calldata) external nonReentrant {
        Job storage job = _job(jobId);
        address sender = _msgSender();
        if (job.status == JobStatus.Open) {
            if (sender != job.client) revert Unauthorized();
        } else if (job.status == JobStatus.Funded || job.status == JobStatus.Submitted) {
            if (sender != job.evaluator) revert Unauthorized();
        } else revert WrongStatus();
        JobStatus previous = job.status;
        job.status = JobStatus.Rejected;
        // Open jobs have no escrow; funded/submitted jobs refund the client.
        if (job.budget != 0 && (previous == JobStatus.Funded || previous == JobStatus.Submitted)) {
            paymentToken.safeTransfer(job.client, job.budget);
            emit Refunded(jobId, job.client, job.budget);
        }
        emit JobRejected(jobId, sender, reason);
    }

    function claimRefund(uint256 jobId) external nonReentrant {
        Job storage job = _job(jobId);
        if (job.status != JobStatus.Funded && job.status != JobStatus.Submitted) revert WrongStatus();
        if (block.timestamp < job.expiredAt) revert WrongStatus();
        job.status = JobStatus.Expired;
        paymentToken.safeTransfer(job.client, job.budget);
        emit Refunded(jobId, job.client, job.budget);
        emit JobExpired(jobId);
    }

    function _job(uint256 jobId) internal view returns (Job storage job) {
        job = jobs[jobId];
        if (job.id == 0) revert InvalidJob();
    }
}
