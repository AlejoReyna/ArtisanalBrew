// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {SimpleAccountFactory} from "@account-abstraction/contracts/samples/SimpleAccountFactory.sol";
import {VerifyingPaymaster} from "@account-abstraction/contracts/samples/VerifyingPaymaster.sol";
import {EntryPointSimulations} from "@account-abstraction/contracts/core/EntryPointSimulations.sol";
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

// Canonical ERC-4337 v0.7.0 EntryPointSimulations, deployed unmodified.
//
// This contract is declared locally only so Hardhat compiles it and emits an artifact carrying its
// deployed bytecode. It is NEVER deployed on any chain, local or public — its own constructor
// refuses after block 100, and the upstream comment is explicit that "this contract should never
// be deployed on-chain". Its bytecode is used only as an `eth_call` state override: a caller can
// substitute it for the real EntryPoint's code for the duration of a single read-only call, run
// `simulateHandleOp`, and get back real validation/execution gas figures without ever touching
// chain state or requiring a deployment. See UserOperationSimulator (Infrastructure) for the C#
// side of this.
contract CanonicalEntryPointSimulations is EntryPointSimulations {}
