// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {SimpleAccountFactory} from "@account-abstraction/contracts/samples/SimpleAccountFactory.sol";
import {IEntryPoint} from "@account-abstraction/contracts/interfaces/IEntryPoint.sol";

// Canonical ERC-4337 v0.7.0 account factory, deployed unmodified.
//
// This is a bare subclass of the reference SimpleAccountFactory from the pinned
// account-abstraction contracts package, declared locally only so Hardhat emits an artifact for it
// (artifacts are not generated for node_modules sources). No logic is overridden, reduced, or
// reimplemented — the same arrangement as EntryPointFixture for the EntryPoint.
//
// SimpleAccountFactory/SimpleAccount are the ERC-4337 *reference* account implementation. They are
// appropriate for local development and the acceptance harness. A production deployment should pin
// an audited account implementation instead.
contract CanonicalSimpleAccountFactory is SimpleAccountFactory {
    constructor(IEntryPoint entryPoint_) SimpleAccountFactory(entryPoint_) {}
}
