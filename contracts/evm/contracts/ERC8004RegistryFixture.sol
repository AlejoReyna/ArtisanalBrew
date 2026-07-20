// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @title ERC-8004 Minimal Local Fixture
/// @notice A minimal honest registry for testing ERC-8004 Agent Identity & Reputation locally.
/// @dev This is NOT the production standard implementation, just a test fixture for the local demo.
contract ERC8004RegistryFixture {
    struct Agent {
        uint256 id;
        address owner;
        string metadataURI;
        bool isActive;
    }

    struct Feedback {
        uint256 jobId;
        address reviewer;
        uint256 score;
        string commentURI;
    }

    mapping(uint256 => Agent) public agents;
    mapping(uint256 => Feedback[]) public agentFeedback;
    uint256 public agentCounter;

    event AgentRegistered(uint256 indexed agentId, address indexed owner, string metadataURI);
    event FeedbackSubmitted(uint256 indexed agentId, address indexed reviewer, uint256 indexed jobId, uint256 score, string commentURI);

    function registerAgent(string calldata metadataURI) external returns (uint256 agentId) {
        agentId = ++agentCounter;
        agents[agentId] = Agent(agentId, msg.sender, metadataURI, true);
        emit AgentRegistered(agentId, msg.sender, metadataURI);
    }

    function submitFeedback(uint256 agentId, uint256 jobId, uint256 score, string calldata commentURI) external {
        require(agents[agentId].isActive, "Agent not active or does not exist");
        require(score <= 100, "Score must be <= 100");
        
        agentFeedback[agentId].push(Feedback(jobId, msg.sender, score, commentURI));
        emit FeedbackSubmitted(agentId, msg.sender, jobId, score, commentURI);
    }
    
    function getFeedbackCount(uint256 agentId) external view returns (uint256) {
        return agentFeedback[agentId].length;
    }
}
