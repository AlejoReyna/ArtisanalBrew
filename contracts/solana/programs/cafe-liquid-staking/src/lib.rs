use anchor_lang::prelude::*;
use anchor_lang::solana_program::program_option::COption;
use anchor_spl::token_interface::{
    self, Burn, FreezeAccount, Mint, MintTo, ThawAccount, TokenAccount, TokenInterface,
    TransferChecked,
};

declare_id!("EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh");

pub const VAULT_SEED: &[u8] = b"cafe-liquid-vault-v1";
pub const POSITION_SEED: &[u8] = b"cafe-liquid-position-v1";
pub const REWARD_SCALE: u128 = 1_000_000_000_000_000_000;

#[program]
pub mod cafe_liquid_staking {
    use super::*;

    pub fn initialize(ctx: Context<Initialize>, decimals: u8) -> Result<()> {
        require!(decimals > 0 && decimals <= 9, ErrorCode::InvalidDecimals);
        require!(
            ctx.accounts.cafe_mint.decimals == decimals,
            ErrorCode::InvalidDecimals
        );
        require!(
            ctx.accounts.st_cafe_mint.decimals == decimals,
            ErrorCode::InvalidDecimals
        );
        require!(
            ctx.accounts.st_cafe_mint.mint_authority == COption::Some(ctx.accounts.vault.key()),
            ErrorCode::InvalidMintAuthority
        );
        require!(
            ctx.accounts.st_cafe_mint.freeze_authority == COption::Some(ctx.accounts.vault.key()),
            ErrorCode::InvalidFreezeAuthority
        );
        let vault = &mut ctx.accounts.vault;
        vault.version = 1;
        vault.admin = ctx.accounts.admin.key();
        vault.cafe_mint = ctx.accounts.cafe_mint.key();
        vault.coffee_mint = ctx.accounts.coffee_mint.key();
        vault.st_cafe_mint = ctx.accounts.st_cafe_mint.key();
        vault.decimals = decimals;
        vault.coffee_decimals = ctx.accounts.coffee_mint.decimals;
        vault.bump = ctx.bumps.vault;
        vault.last_reward_slot = Clock::get()?.slot;
        Ok(())
    }

    pub fn deposit(ctx: Context<Deposit>, amount: u64) -> Result<()> {
        require!(!ctx.accounts.vault.paused, ErrorCode::Paused);
        require!(amount > 0, ErrorCode::InvalidAmount);
        let slot = Clock::get()?.slot;
        ctx.accounts.vault.checkpoint(slot)?;
        if ctx.accounts.position.owner == Pubkey::default() {
            ctx.accounts.position.owner = ctx.accounts.owner.key();
        }
        require_keys_eq!(
            ctx.accounts.position.owner,
            ctx.accounts.owner.key(),
            ErrorCode::Unauthorized
        );
        ctx.accounts
            .position
            .accrue(ctx.accounts.vault.reward_per_share)?;
        transfer_checked(
            ctx.accounts.cafe_in(),
            ctx.accounts.token_program.to_account_info(),
            amount,
            ctx.accounts.vault.decimals,
            None,
        )?;
        let bump = ctx.accounts.vault.bump;
        let seeds = [VAULT_SEED, &[bump][..]];
        if ctx.accounts.owner_st_cafe.is_frozen() {
            thaw(
                ctx.accounts.share_thaw(),
                ctx.accounts.token_program.to_account_info(),
                &seeds,
            )?;
        }
        mint_to(
            ctx.accounts.share_mint(),
            ctx.accounts.token_program.to_account_info(),
            amount,
            ctx.accounts.vault.decimals,
            Some(&seeds),
        )?;
        freeze(
            ctx.accounts.share_freeze(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        ctx.accounts.position.shares = ctx
            .accounts
            .position
            .shares
            .checked_add(amount)
            .ok_or(ErrorCode::Overflow)?;
        ctx.accounts.position.reward_per_share_paid = ctx.accounts.vault.reward_per_share;
        ctx.accounts.vault.total_shares = ctx
            .accounts
            .vault
            .total_shares
            .checked_add(amount)
            .ok_or(ErrorCode::Overflow)?;
        emit!(DepositEvent {
            owner: ctx.accounts.owner.key(),
            assets: amount,
            shares: amount,
            slot
        });
        Ok(())
    }

    pub fn redeem(ctx: Context<Redeem>, shares: u64) -> Result<()> {
        require!(shares > 0, ErrorCode::InvalidAmount);
        require!(
            ctx.accounts.position.shares >= shares,
            ErrorCode::InsufficientShares
        );
        require_keys_eq!(
            ctx.accounts.position.owner,
            ctx.accounts.owner.key(),
            ErrorCode::Unauthorized
        );
        let slot = Clock::get()?.slot;
        ctx.accounts.vault.checkpoint(slot)?;
        ctx.accounts
            .position
            .accrue(ctx.accounts.vault.reward_per_share)?;
        require!(
            ctx.accounts.owner_st_cafe.is_frozen(),
            ErrorCode::ReceiptAccountNotFrozen
        );
        let bump = ctx.accounts.vault.bump;
        let seeds = [VAULT_SEED, &[bump][..]];
        thaw(
            ctx.accounts.share_thaw(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        burn(
            ctx.accounts.share_burn(),
            ctx.accounts.token_program.to_account_info(),
            shares,
        )?;
        transfer_checked(
            ctx.accounts.cafe_out(),
            ctx.accounts.token_program.to_account_info(),
            shares,
            ctx.accounts.vault.decimals,
            Some(&seeds),
        )?;
        freeze(
            ctx.accounts.share_freeze(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        ctx.accounts.position.shares -= shares;
        ctx.accounts.position.reward_per_share_paid = ctx.accounts.vault.reward_per_share;
        ctx.accounts.vault.total_shares -= shares;
        emit!(RedeemEvent {
            owner: ctx.accounts.owner.key(),
            assets: shares,
            shares,
            slot
        });
        Ok(())
    }

    pub fn fund_rewards(ctx: Context<FundRewards>, amount: u64, duration_slots: u64) -> Result<()> {
        require!(amount > 0 && duration_slots > 0, ErrorCode::InvalidSchedule);
        require_keys_eq!(
            ctx.accounts.vault.admin,
            ctx.accounts.admin.key(),
            ErrorCode::Unauthorized
        );
        let slot = Clock::get()?.slot;
        require!(
            slot >= ctx.accounts.vault.period_finish,
            ErrorCode::InvalidSchedule
        );
        ctx.accounts.vault.checkpoint(slot)?;
        let before = ctx.accounts.custody_coffee.amount;
        transfer_checked(
            ctx.accounts.reward_in(),
            ctx.accounts.token_program.to_account_info(),
            amount,
            ctx.accounts.vault.coffee_decimals,
            None,
        )?;
        ctx.accounts.custody_coffee.reload()?;
        let received = ctx
            .accounts
            .custody_coffee
            .amount
            .checked_sub(before)
            .ok_or(ErrorCode::Underfunded)?;
        require!(received > 0 && received <= amount, ErrorCode::Underfunded);
        ctx.accounts.vault.reward_rate = received
            .checked_div(duration_slots)
            .ok_or(ErrorCode::InvalidSchedule)?;
        require!(
            ctx.accounts.vault.reward_rate > 0,
            ErrorCode::InvalidSchedule
        );
        ctx.accounts.vault.period_finish = slot
            .checked_add(duration_slots)
            .ok_or(ErrorCode::Overflow)?;
        emit!(RewardFundedEvent {
            amount: received,
            duration_slots,
            slot
        });
        Ok(())
    }

    pub fn checkpoint(ctx: Context<Checkpoint>) -> Result<()> {
        ctx.accounts.vault.checkpoint(Clock::get()?.slot)
    }

    pub fn claim_rewards(ctx: Context<ClaimRewards>) -> Result<()> {
        let slot = Clock::get()?.slot;
        ctx.accounts.vault.checkpoint(slot)?;
        require_keys_eq!(
            ctx.accounts.position.owner,
            ctx.accounts.owner.key(),
            ErrorCode::Unauthorized
        );
        ctx.accounts
            .position
            .accrue(ctx.accounts.vault.reward_per_share)?;
        let reward = ctx.accounts.position.pending_rewards;
        require!(reward > 0, ErrorCode::NoRewards);
        ctx.accounts.position.pending_rewards = 0;
        ctx.accounts.position.reward_per_share_paid = ctx.accounts.vault.reward_per_share;
        let bump = ctx.accounts.vault.bump;
        let seeds = [VAULT_SEED, &[bump][..]];
        transfer_checked(
            ctx.accounts.reward_out(),
            ctx.accounts.token_program.to_account_info(),
            reward,
            ctx.accounts.vault.coffee_decimals,
            Some(&seeds),
        )?;
        emit!(RewardClaimedEvent {
            owner: ctx.accounts.owner.key(),
            reward,
            slot
        });
        Ok(())
    }

    pub fn transfer_st_cafe(ctx: Context<TransferStCafe>, amount: u64) -> Result<()> {
        require!(amount > 0, ErrorCode::InvalidAmount);
        require!(
            ctx.accounts.sender_position.shares >= amount,
            ErrorCode::InsufficientShares
        );
        let slot = Clock::get()?.slot;
        ctx.accounts.vault.checkpoint(slot)?;
        require_keys_neq!(
            ctx.accounts.owner.key(),
            ctx.accounts.recipient.key(),
            ErrorCode::InvalidRecipient
        );
        require_keys_eq!(
            ctx.accounts.sender_position.owner,
            ctx.accounts.owner.key(),
            ErrorCode::Unauthorized
        );
        if ctx.accounts.recipient_position.owner == Pubkey::default() {
            ctx.accounts.recipient_position.owner = ctx.accounts.recipient.key();
        }
        require_keys_eq!(
            ctx.accounts.recipient_position.owner,
            ctx.accounts.recipient.key(),
            ErrorCode::Unauthorized
        );
        ctx.accounts
            .sender_position
            .accrue(ctx.accounts.vault.reward_per_share)?;
        ctx.accounts
            .recipient_position
            .accrue(ctx.accounts.vault.reward_per_share)?;
        require!(
            ctx.accounts.owner_shares.is_frozen(),
            ErrorCode::ReceiptAccountNotFrozen
        );
        let bump = ctx.accounts.vault.bump;
        let seeds = [VAULT_SEED, &[bump][..]];
        thaw(
            ctx.accounts.sender_share_thaw(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        if ctx.accounts.recipient_shares.is_frozen() {
            thaw(
                ctx.accounts.recipient_share_thaw(),
                ctx.accounts.token_program.to_account_info(),
                &seeds,
            )?;
        }
        transfer_checked(
            ctx.accounts.share_transfer(),
            ctx.accounts.token_program.to_account_info(),
            amount,
            ctx.accounts.vault.decimals,
            None,
        )?;
        freeze(
            ctx.accounts.sender_share_freeze(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        freeze(
            ctx.accounts.recipient_share_freeze(),
            ctx.accounts.token_program.to_account_info(),
            &seeds,
        )?;
        ctx.accounts.sender_position.shares -= amount;
        ctx.accounts.recipient_position.shares = ctx
            .accounts
            .recipient_position
            .shares
            .checked_add(amount)
            .ok_or(ErrorCode::Overflow)?;
        ctx.accounts.sender_position.reward_per_share_paid = ctx.accounts.vault.reward_per_share;
        ctx.accounts.recipient_position.reward_per_share_paid = ctx.accounts.vault.reward_per_share;
        emit!(TransferCheckpointEvent {
            sender: ctx.accounts.owner.key(),
            recipient: ctx.accounts.recipient.key(),
            shares: amount,
            slot
        });
        Ok(())
    }

    pub fn set_paused(ctx: Context<AdminAction>, paused: bool) -> Result<()> {
        require_keys_eq!(
            ctx.accounts.vault.admin,
            ctx.accounts.admin.key(),
            ErrorCode::Unauthorized
        );
        ctx.accounts.vault.paused = paused;
        emit!(PauseEvent {
            paused,
            slot: Clock::get()?.slot
        });
        Ok(())
    }
}

fn transfer_checked<'info>(
    accounts: TransferChecked<'info>,
    token_program: AccountInfo<'info>,
    amount: u64,
    decimals: u8,
    signer_seeds: Option<&[&[u8]]>,
) -> Result<()> {
    let cpi = CpiContext::new(token_program, accounts);
    match signer_seeds {
        Some(seeds) => {
            let signer_groups = [seeds];
            token_interface::transfer_checked(cpi.with_signer(&signer_groups), amount, decimals)
        }
        None => token_interface::transfer_checked(cpi, amount, decimals),
    }
}

fn mint_to<'info>(
    accounts: MintTo<'info>,
    token_program: AccountInfo<'info>,
    amount: u64,
    _decimals: u8,
    signer_seeds: Option<&[&[u8]]>,
) -> Result<()> {
    let cpi = CpiContext::new(token_program, accounts);
    match signer_seeds {
        Some(seeds) => {
            let signer_groups = [seeds];
            token_interface::mint_to(cpi.with_signer(&signer_groups), amount)
        }
        None => token_interface::mint_to(cpi, amount),
    }
}

fn burn<'info>(
    accounts: Burn<'info>,
    token_program: AccountInfo<'info>,
    amount: u64,
) -> Result<()> {
    token_interface::burn(CpiContext::new(token_program, accounts), amount)
}

fn thaw<'info>(
    accounts: ThawAccount<'info>,
    token_program: AccountInfo<'info>,
    signer_seeds: &[&[u8]],
) -> Result<()> {
    let signer_groups = [signer_seeds];
    token_interface::thaw_account(
        CpiContext::new(token_program, accounts).with_signer(&signer_groups),
    )
}

fn freeze<'info>(
    accounts: FreezeAccount<'info>,
    token_program: AccountInfo<'info>,
    signer_seeds: &[&[u8]],
) -> Result<()> {
    let signer_groups = [signer_seeds];
    token_interface::freeze_account(
        CpiContext::new(token_program, accounts).with_signer(&signer_groups),
    )
}

#[derive(Accounts)]
pub struct Initialize<'info> {
    #[account(mut)]
    pub admin: Signer<'info>,
    #[account(init, payer = admin, space = 8 + Vault::SIZE, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    pub cafe_mint: InterfaceAccount<'info, Mint>,
    pub coffee_mint: InterfaceAccount<'info, Mint>,
    pub st_cafe_mint: InterfaceAccount<'info, Mint>,
    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct Deposit<'info> {
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    #[account(mut)]
    pub owner: Signer<'info>,
    #[account(init_if_needed, payer = owner, space = 8 + Position::SIZE, seeds = [POSITION_SEED, owner.key().as_ref()], bump)]
    pub position: Account<'info, Position>,
    #[account(mut, constraint = owner_cafe.mint == vault.cafe_mint, constraint = owner_cafe.owner == owner.key())]
    pub owner_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, constraint = custody_cafe.mint == vault.cafe_mint, constraint = custody_cafe.owner == vault.key())]
    pub custody_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, address = vault.st_cafe_mint)]
    pub st_cafe_mint: InterfaceAccount<'info, Mint>,
    #[account(mut, constraint = owner_st_cafe.mint == vault.st_cafe_mint, constraint = owner_st_cafe.owner == owner.key())]
    pub owner_st_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(address = vault.cafe_mint)]
    pub cafe_mint: InterfaceAccount<'info, Mint>,
    pub token_program: Interface<'info, TokenInterface>,
    pub system_program: Program<'info, System>,
}
impl<'info> Deposit<'info> {
    fn cafe_in(&self) -> TransferChecked<'info> {
        TransferChecked {
            from: self.owner_cafe.to_account_info(),
            mint: self.cafe_mint.to_account_info(),
            to: self.custody_cafe.to_account_info(),
            authority: self.owner.to_account_info(),
        }
    }
    fn share_mint(&self) -> MintTo<'info> {
        MintTo {
            mint: self.st_cafe_mint.to_account_info(),
            to: self.owner_st_cafe.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn share_freeze(&self) -> FreezeAccount<'info> {
        FreezeAccount {
            account: self.owner_st_cafe.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn share_thaw(&self) -> ThawAccount<'info> {
        ThawAccount {
            account: self.owner_st_cafe.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
}

#[derive(Accounts)]
pub struct Redeem<'info> {
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    #[account(mut)]
    pub owner: Signer<'info>,
    #[account(mut, seeds = [POSITION_SEED, owner.key().as_ref()], bump)]
    pub position: Account<'info, Position>,
    #[account(mut, constraint = custody_cafe.mint == vault.cafe_mint, constraint = custody_cafe.owner == vault.key())]
    pub custody_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, constraint = owner_cafe.mint == vault.cafe_mint, constraint = owner_cafe.owner == owner.key())]
    pub owner_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, address = vault.st_cafe_mint)]
    pub st_cafe_mint: InterfaceAccount<'info, Mint>,
    #[account(mut, constraint = owner_st_cafe.mint == vault.st_cafe_mint, constraint = owner_st_cafe.owner == owner.key())]
    pub owner_st_cafe: InterfaceAccount<'info, TokenAccount>,
    #[account(address = vault.cafe_mint)]
    pub cafe_mint: InterfaceAccount<'info, Mint>,
    pub token_program: Interface<'info, TokenInterface>,
}
impl<'info> Redeem<'info> {
    fn share_burn(&self) -> Burn<'info> {
        Burn {
            mint: self.st_cafe_mint.to_account_info(),
            from: self.owner_st_cafe.to_account_info(),
            authority: self.owner.to_account_info(),
        }
    }
    fn cafe_out(&self) -> TransferChecked<'info> {
        TransferChecked {
            from: self.custody_cafe.to_account_info(),
            mint: self.cafe_mint.to_account_info(),
            to: self.owner_cafe.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn share_freeze(&self) -> FreezeAccount<'info> {
        FreezeAccount {
            account: self.owner_st_cafe.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn share_thaw(&self) -> ThawAccount<'info> {
        ThawAccount {
            account: self.owner_st_cafe.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
}

#[derive(Accounts)]
pub struct FundRewards<'info> {
    pub admin: Signer<'info>,
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    #[account(mut, constraint = admin_coffee.mint == vault.coffee_mint, constraint = admin_coffee.owner == admin.key())]
    pub admin_coffee: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, constraint = custody_coffee.mint == vault.coffee_mint, constraint = custody_coffee.owner == vault.key())]
    pub custody_coffee: InterfaceAccount<'info, TokenAccount>,
    #[account(address = vault.coffee_mint)]
    pub coffee_mint: InterfaceAccount<'info, Mint>,
    pub token_program: Interface<'info, TokenInterface>,
}
impl<'info> FundRewards<'info> {
    fn reward_in(&self) -> TransferChecked<'info> {
        TransferChecked {
            from: self.admin_coffee.to_account_info(),
            mint: self.coffee_mint.to_account_info(),
            to: self.custody_coffee.to_account_info(),
            authority: self.admin.to_account_info(),
        }
    }
}

#[derive(Accounts)]
pub struct Checkpoint<'info> {
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
}

#[derive(Accounts)]
pub struct ClaimRewards<'info> {
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    #[account(mut)]
    pub owner: Signer<'info>,
    #[account(mut, seeds = [POSITION_SEED, owner.key().as_ref()], bump)]
    pub position: Account<'info, Position>,
    #[account(mut, constraint = custody_coffee.mint == vault.coffee_mint, constraint = custody_coffee.owner == vault.key())]
    pub custody_coffee: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, constraint = owner_coffee.mint == vault.coffee_mint, constraint = owner_coffee.owner == owner.key())]
    pub owner_coffee: InterfaceAccount<'info, TokenAccount>,
    #[account(address = vault.coffee_mint)]
    pub coffee_mint: InterfaceAccount<'info, Mint>,
    pub token_program: Interface<'info, TokenInterface>,
}
impl<'info> ClaimRewards<'info> {
    fn reward_out(&self) -> TransferChecked<'info> {
        TransferChecked {
            from: self.custody_coffee.to_account_info(),
            mint: self.coffee_mint.to_account_info(),
            to: self.owner_coffee.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
}

#[derive(Accounts)]
pub struct TransferStCafe<'info> {
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
    #[account(mut)]
    pub owner: Signer<'info>,
    /// CHECK: the recipient is bound to the recipient position PDA and token owner constraint below.
    pub recipient: UncheckedAccount<'info>,
    #[account(mut, seeds = [POSITION_SEED, owner.key().as_ref()], bump)]
    pub sender_position: Account<'info, Position>,
    #[account(init_if_needed, payer = owner, space = 8 + Position::SIZE, seeds = [POSITION_SEED, recipient.key().as_ref()], bump)]
    pub recipient_position: Account<'info, Position>,
    #[account(mut, constraint = owner_shares.mint == vault.st_cafe_mint, constraint = owner_shares.owner == owner.key())]
    pub owner_shares: InterfaceAccount<'info, TokenAccount>,
    #[account(mut, constraint = recipient_shares.mint == vault.st_cafe_mint, constraint = recipient_shares.owner == recipient.key())]
    pub recipient_shares: InterfaceAccount<'info, TokenAccount>,
    #[account(address = vault.st_cafe_mint)]
    pub st_cafe_mint: InterfaceAccount<'info, Mint>,
    pub token_program: Interface<'info, TokenInterface>,
    pub system_program: Program<'info, System>,
}
impl<'info> TransferStCafe<'info> {
    fn share_transfer(&self) -> TransferChecked<'info> {
        TransferChecked {
            from: self.owner_shares.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            to: self.recipient_shares.to_account_info(),
            authority: self.owner.to_account_info(),
        }
    }
    fn sender_share_freeze(&self) -> FreezeAccount<'info> {
        FreezeAccount {
            account: self.owner_shares.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn sender_share_thaw(&self) -> ThawAccount<'info> {
        ThawAccount {
            account: self.owner_shares.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn recipient_share_freeze(&self) -> FreezeAccount<'info> {
        FreezeAccount {
            account: self.recipient_shares.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
    fn recipient_share_thaw(&self) -> ThawAccount<'info> {
        ThawAccount {
            account: self.recipient_shares.to_account_info(),
            mint: self.st_cafe_mint.to_account_info(),
            authority: self.vault.to_account_info(),
        }
    }
}

#[derive(Accounts)]
pub struct AdminAction<'info> {
    pub admin: Signer<'info>,
    #[account(mut, seeds = [VAULT_SEED], bump)]
    pub vault: Account<'info, Vault>,
}

#[account]
pub struct Vault {
    pub version: u8,
    pub decimals: u8,
    pub coffee_decimals: u8,
    pub paused: bool,
    pub bump: u8,
    pub admin: Pubkey,
    pub cafe_mint: Pubkey,
    pub coffee_mint: Pubkey,
    pub st_cafe_mint: Pubkey,
    pub total_shares: u64,
    pub reward_per_share: u128,
    pub reward_rate: u64,
    pub period_finish: u64,
    pub last_reward_slot: u64,
}
impl Vault {
    pub const SIZE: usize = 1 + 4 + 32 * 4 + 8 + 16 + 8 * 3;
    fn checkpoint(&mut self, slot: u64) -> Result<()> {
        require!(slot >= self.last_reward_slot, ErrorCode::InvalidSlot);
        let end = slot.min(self.period_finish);
        if end > self.last_reward_slot && self.total_shares > 0 {
            let elapsed = end - self.last_reward_slot;
            let reward = (elapsed as u128)
                .checked_mul(self.reward_rate as u128)
                .ok_or(ErrorCode::Overflow)?;
            let increment = reward
                .checked_mul(REWARD_SCALE)
                .ok_or(ErrorCode::Overflow)?
                .checked_div(self.total_shares as u128)
                .ok_or(ErrorCode::Overflow)?;
            self.reward_per_share = self
                .reward_per_share
                .checked_add(increment)
                .ok_or(ErrorCode::Overflow)?;
        }
        self.last_reward_slot = slot;
        Ok(())
    }
}

#[account]
pub struct Position {
    pub owner: Pubkey,
    pub shares: u64,
    pub reward_per_share_paid: u128,
    pub pending_rewards: u64,
}
impl Position {
    pub const SIZE: usize = 32 + 8 + 16 + 8;
    fn accrue(&mut self, current: u128) -> Result<()> {
        require!(
            current >= self.reward_per_share_paid,
            ErrorCode::InvalidSlot
        );
        let delta = current - self.reward_per_share_paid;
        let earned = (self.shares as u128)
            .checked_mul(delta)
            .ok_or(ErrorCode::Overflow)?
            .checked_div(REWARD_SCALE)
            .ok_or(ErrorCode::Overflow)?;
        self.pending_rewards = self
            .pending_rewards
            .checked_add(u64::try_from(earned).map_err(|_| ErrorCode::Overflow)?)
            .ok_or(ErrorCode::Overflow)?;
        self.reward_per_share_paid = current;
        Ok(())
    }
}

#[event]
pub struct DepositEvent {
    pub owner: Pubkey,
    pub assets: u64,
    pub shares: u64,
    pub slot: u64,
}
#[event]
pub struct RedeemEvent {
    pub owner: Pubkey,
    pub assets: u64,
    pub shares: u64,
    pub slot: u64,
}
#[event]
pub struct RewardFundedEvent {
    pub amount: u64,
    pub duration_slots: u64,
    pub slot: u64,
}
#[event]
pub struct RewardClaimedEvent {
    pub owner: Pubkey,
    pub reward: u64,
    pub slot: u64,
}
#[event]
pub struct TransferCheckpointEvent {
    pub sender: Pubkey,
    pub recipient: Pubkey,
    pub shares: u64,
    pub slot: u64,
}
#[event]
pub struct PauseEvent {
    pub paused: bool,
    pub slot: u64,
}

#[error_code]
pub enum ErrorCode {
    #[msg("vault is paused")]
    Paused,
    #[msg("amount must be positive")]
    InvalidAmount,
    #[msg("invalid decimals")]
    InvalidDecimals,
    #[msg("stCAFE mint authority must be the vault PDA")]
    InvalidMintAuthority,
    #[msg("stCAFE freeze authority must be the vault PDA")]
    InvalidFreezeAuthority,
    #[msg("stCAFE token accounts must remain frozen outside vault-mediated transfers")]
    ReceiptAccountNotFrozen,
    #[msg("invalid reward schedule")]
    InvalidSchedule,
    #[msg("unauthorized")]
    Unauthorized,
    #[msg("no rewards")]
    NoRewards,
    #[msg("insufficient shares")]
    InsufficientShares,
    #[msg("recipient must differ from sender")]
    InvalidRecipient,
    #[msg("reward transfer was underfunded")]
    Underfunded,
    #[msg("arithmetic overflow")]
    Overflow,
    #[msg("slot moved backwards")]
    InvalidSlot,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vault() -> Vault {
        Vault {
            version: 1,
            decimals: 9,
            coffee_decimals: 9,
            paused: false,
            bump: 255,
            admin: Pubkey::default(),
            cafe_mint: Pubkey::default(),
            coffee_mint: Pubkey::default(),
            st_cafe_mint: Pubkey::default(),
            total_shares: 100,
            reward_per_share: 0,
            reward_rate: 10,
            period_finish: 20,
            last_reward_slot: 0,
        }
    }

    #[test]
    fn checkpoint_is_capped_at_period_finish() {
        let mut v = vault();
        v.checkpoint(100).unwrap();
        assert_eq!(v.last_reward_slot, 100);
        assert_eq!(v.reward_per_share, 2 * REWARD_SCALE);
    }

    #[test]
    fn position_accrual_conserves_fractional_rewards() {
        let mut v = vault();
        v.checkpoint(10).unwrap();
        let mut p = Position {
            owner: Pubkey::default(),
            shares: 25,
            reward_per_share_paid: 0,
            pending_rewards: 0,
        };
        p.accrue(v.reward_per_share).unwrap();
        assert_eq!(p.pending_rewards, 25);
        assert_eq!(p.reward_per_share_paid, v.reward_per_share);
    }

    #[test]
    fn zero_supply_does_not_create_rewards() {
        let mut v = vault();
        v.total_shares = 0;
        v.checkpoint(10).unwrap();
        assert_eq!(v.reward_per_share, 0);
    }
}
