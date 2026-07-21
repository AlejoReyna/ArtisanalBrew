// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {SimpleAccountFactory} from "@account-abstraction/contracts/samples/SimpleAccountFactory.sol";
import {VerifyingPaymaster} from "@account-abstraction/contracts/samples/VerifyingPaymaster.sol";
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

// Canonical ERC-4337 v0.7.0 VerifyingPaymaster, deployed unmodified.
//
// Sponsorship is authorised off-chain: `verifyingSigner` signs a hash covering the UserOperation
// plus a validUntil/validAfter window, and the paymaster verifies that signature on-chain. This is
// the reference sponsorship primitive, not a policy engine — quota enforcement, per-user limits,
// and simulation still have to be implemented on top of it.
contract CanonicalVerifyingPaymaster is VerifyingPaymaster {
    constructor(IEntryPoint entryPoint_, address verifyingSigner_)
        VerifyingPaymaster(entryPoint_, verifyingSigner_)
    {}
}
