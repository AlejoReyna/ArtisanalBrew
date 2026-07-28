// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

import {ERC20} from "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

contract CafePaymentToken is ERC20, Ownable {
    constructor(address initialOwner, uint256 initialSupplyWholeTokens)
        ERC20("Cafe Payment Token", "CAFE")
        Ownable(initialOwner)
    {
        _mint(initialOwner, initialSupplyWholeTokens * 10 ** decimals());
    }

    function mint(address to, uint256 amount) external onlyOwner {
        _mint(to, amount);
    }

    function mintWholeTokens(address to, uint256 wholeTokens) external onlyOwner {
        _mint(to, wholeTokens * 10 ** decimals());
    }
}

contract CoffeeCoin is ERC20, Ownable {
    constructor(address initialOwner)
        ERC20("CoffeeCoin", "COFFEE")
        Ownable(initialOwner)
    {}

    function mint(address to, uint256 amount) external onlyOwner {
        _mint(to, amount);
    }

    function mintWholeTokens(address to, uint256 wholeTokens) external onlyOwner {
        _mint(to, wholeTokens * 10 ** decimals());
    }
}